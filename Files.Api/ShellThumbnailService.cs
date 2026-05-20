using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Files.Api
{
    public class ShellThumbnailService : IThumbnailService
    {
        private readonly ILogger<ShellThumbnailService> _logger;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(4);
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private bool _ffmpegChecked;
        private bool _ffmpegAvailable;

        private record CacheEntry(string DataUri, DateTime LastWriteUtc, DateTime ExpireAt);

        public ShellThumbnailService(ILogger<ShellThumbnailService> logger)
        {
            _logger = logger;
        }

        public async Task<string?> GetThumbnailDataUriAsync(string path, int size = 128)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var cacheKey = $"{path}|{size}|{lastWrite.Ticks}";

                if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
                {
                    return cached.DataUri;
                }

                await _semaphore.WaitAsync();
                try
                {
                    // Preferred: Windows Shell thumbnail (Explorer-style)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var shellThumb = await GetShellThumbnailDataUriAsync(path, size);
                        if (!string.IsNullOrEmpty(shellThumb))
                        {
                            var entry = new CacheEntry(shellThumb, lastWrite, DateTime.UtcNow.AddMinutes(15));
                            _cache[cacheKey] = entry;
                            return shellThumb;
                        }

                        // Fallback: System.Drawing for images
                        if (IsImageExt(Path.GetExtension(path)))
                        {
                            try
                            {
                                using var img = Image.FromFile(path);
                                using var thumb = img.GetThumbnailImage(size, size, () => false, IntPtr.Zero);
                                using var ms = new MemoryStream();
                                thumb.Save(ms, ImageFormat.Png);
                                var base64 = Convert.ToBase64String(ms.ToArray());
                                var dataUri = $"data:image/png;base64,{base64}";
                                var entry = new CacheEntry(dataUri, lastWrite, DateTime.UtcNow.AddMinutes(15));
                                _cache[cacheKey] = entry;
                                return dataUri;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "System.Drawing fallback failed for {Path}", path);
                            }
                        }
                    }

                    // FFmpeg fallback for videos or images
                    if (!_ffmpegChecked)
                    {
                        _ffmpegAvailable = CheckFfmpegAvailable();
                        _ffmpegChecked = true;
                    }

                    if (_ffmpegAvailable)
                    {
                        var ffThumb = await RunFfmpegThumbnailAsync(path, size);
                        if (!string.IsNullOrEmpty(ffThumb))
                        {
                            var entry = new CacheEntry(ffThumb, lastWrite, DateTime.UtcNow.AddMinutes(15));
                            _cache[cacheKey] = entry;
                            return ffThumb;
                        }
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Thumbnail generation failed for {Path}", path);
            }

            return null;
        }

        private static bool IsImageExt(string ext) => ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff";
        private static bool IsVideoExt(string ext) => ext is ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm";

        private static bool CheckFfmpegAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("ffmpeg", "-version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(1000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> RunFfmpegThumbnailAsync(string path, int size)
        {
            try
            {
                var args = $"-ss 00:00:01 -i \"{path}\" -vframes 1 -vf scale={size}:-1 -f image2 -";
                var psi = new ProcessStartInfo("ffmpeg", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                using var ms = new MemoryStream();
                await proc.StandardOutput.BaseStream.CopyToAsync(ms);
                await proc.WaitForExitAsync();
                if (ms.Length == 0)
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    _logger.LogDebug("ffmpeg failed for {Path}: {Err}", path, err);
                    return null;
                }
                var base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:image/png;base64,{base64}";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ffmpeg thumbnail failed for {Path}", path);
                return null;
            }
        }

        // Windows Shell interop (to get Explorer-style thumbnails)
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [Flags]
        private enum SIIGBF : uint
        {
            RESIZETOFIT = 0x0,
            BIGGERSIZEOK = 0x1,
            MEMORYONLY = 0x2,
            ICONONLY = 0x4,
            THUMBNAILONLY = 0x8,
            INCACHEONLY = 0x10,
            CROPTOSQUARE = 0x20,
            WIDETHUMBNAILS = 0x40
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private Task<T?> RunOnStaThread<T>(Func<T?> func)
        {
            var tcs = new TaskCompletionSource<T?>();
            var th = new Thread(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            return tcs.Task;
        }

        private string? GetShellThumbnailSync(string path, int size)
        {
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, out var obj);
                if (obj == null) return null;
                var factory = obj as IShellItemImageFactory;
                if (factory == null) { Marshal.ReleaseComObject(obj); return null; }

                IntPtr hBitmap = IntPtr.Zero;
                try
                {
                    var s = new SIZE { cx = size, cy = size };
                    factory.GetImage(s, SIIGBF.RESIZETOFIT | SIIGBF.CROPTOSQUARE, out hBitmap);
                    if (hBitmap == IntPtr.Zero) return null;
                    using var image = Image.FromHbitmap(hBitmap);
                    using var ms = new MemoryStream();
                    image.Save(ms, ImageFormat.Png);
                    var base64 = Convert.ToBase64String(ms.ToArray());
                    return $"data:image/png;base64,{base64}";
                }
                finally
                {
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    try { Marshal.ReleaseComObject(factory); } catch { }
                    try { Marshal.ReleaseComObject(obj); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Shell thumbnail failed for {Path}", path);
                return null;
            }
        }

        private Task<string?> GetShellThumbnailDataUriAsync(string path, int size)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Task.FromResult<string?>(null);
            return RunOnStaThread(() => GetShellThumbnailSync(path, size));
        }
    }
}