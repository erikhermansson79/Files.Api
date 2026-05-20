using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Files.Api
{
    public class ThumbnailService : IThumbnailService
    {
        private readonly ILogger<ThumbnailService> _logger;
        private bool _ffmpegChecked;
        private bool _ffmpegAvailable;

        public ThumbnailService(ILogger<ThumbnailService> logger)
        {
            _logger = logger;
        }

        public async Task<string?> GetThumbnailDataUriAsync(string path, int size = 128)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                var ext = Path.GetExtension(path).ToLowerInvariant();

                if (IsImageExt(ext))
                {
                    // Prefer OS/windowing-system approach on Windows using System.Drawing (supported on Windows)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        try
                        {
                            using var img = System.Drawing.Image.FromFile(path);
                            int thumbWidth = size;
                            int thumbHeight = size;
                            using var thumb = img.GetThumbnailImage(thumbWidth, thumbHeight, () => false, IntPtr.Zero);
                            using var ms = new MemoryStream();
                            thumb.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            var base64 = Convert.ToBase64String(ms.ToArray());
                            return $"data:image/png;base64,{base64}";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to generate image thumbnail using System.Drawing for {Path}", path);
                        }
                    }

                    // Fallback to ffmpeg if available (works for images as well)
                    if (!_ffmpegChecked)
                    {
                        _ffmpegAvailable = CheckFfmpegAvailable();
                        _ffmpegChecked = true;
                    }
                    if (_ffmpegAvailable)
                    {
                        return await RunFfmpegThumbnailAsync(path, size);
                    }

                    return null;
                }
                else if (IsVideoExt(ext))
                {
                    if (!_ffmpegChecked)
                    {
                        _ffmpegAvailable = CheckFfmpegAvailable();
                        _ffmpegChecked = true;
                    }
                    if (!_ffmpegAvailable) return null;
                    return await RunFfmpegThumbnailAsync(path, size);
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail generation failed for {Path}", path);
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
                    _logger.LogWarning("ffmpeg failed for {Path}: {Err}", path, err);
                    return null;
                }
                var base64 = Convert.ToBase64String(ms.ToArray());
                return $"data:image/png;base64,{base64}";
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}
