using Files.Api.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Files.Api
{
	public partial class FileService : IFileService
	{
		private readonly IDirectories _directories;
		private readonly IAuthorizationService _authorizationService;
		private readonly IHttpContextAccessor _httpContextAccessor;

		public FileService(
			IDirectories directories,
			IAuthorizationService authorizationService,
			IHttpContextAccessor httpContextAccessor)
		{
			_directories = directories;
			_authorizationService = authorizationService;
			_httpContextAccessor = httpContextAccessor;
		}

		//[LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		//[return: MarshalAs(UnmanagedType.Bool)]
		//private static partial bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

		//public void CreateFileLinks(IEnumerable<FileLink> fileLinks)
		//{
		//	foreach(var fileLink in fileLinks)
		//	{
		//		var source = Path.Combine(this.baseFolder, fileLink.SourcePath.TrimStart('/'));
		//		var target = Path.Combine(this.wwwRootFolder, fileLink.TargetPath.TrimStart('/'));

		//		if (File.Exists(source))// && !File.Exists(target))
		//		{
		//			var targetFolder = Path.GetDirectoryName(target);

		//			if (!Directory.Exists(targetFolder))
		//			{
		//				Directory.CreateDirectory(targetFolder!);
		//			}

		//			if(!CreateHardLink(Path.GetFullPath(target), Path.GetFullPath(source), IntPtr.Zero))
		//			{
		//				throw new Exception(Marshal.GetLastPInvokeErrorMessage());
		//			}
		//		}
		//	}
		//}

		public void ChangeItemName(ChangeItemNameModel changeItemNameModel)
		{
			var path = Path.Combine(_directories.LibraryDirectory, changeItemNameModel.Target.TrimStart('/'));
			var newPath = Path.Combine(Path.GetDirectoryName(path)!, changeItemNameModel.Name);

			if (changeItemNameModel.Type == "directory")
			{
				var dir = new DirectoryInfo(path);
				if (dir.Exists)
				{
					if (dir.FullName.Equals(newPath, StringComparison.OrdinalIgnoreCase))
					{
						// If wwe are just changing the casing we must go via a temp name, or MoveTo will throw an exception.
						var tempPath = Path.Combine(Path.GetDirectoryName(newPath)!, Guid.NewGuid().ToString());
						dir.MoveTo(tempPath);
						Directory.Move(tempPath, newPath);
					}
					else
					{
						dir.MoveTo(newPath);
					}

				}
			}
			else if (changeItemNameModel.Type == "file")
			{
				var file = new FileInfo(path);
				if (file.Exists)
				{
					file.MoveTo(newPath);
				}
			}
		}

		public void ToggleItemHidden(ToggleItemHiddenModel toggleItemHiddenModel)
		{
			var target = Path.Combine(_directories.LibraryDirectory, toggleItemHiddenModel.Target.TrimStart('/'));
			if (toggleItemHiddenModel.Type == "directory")
			{
				var di = new DirectoryInfo(target);
				di.Attributes = di.Attributes ^ FileAttributes.Hidden;
			}
			else if (toggleItemHiddenModel.Type == "file")
			{
				File.SetAttributes(target, File.GetAttributes(target) ^ FileAttributes.Hidden);
			}
		}

		public void DeleteItem(DeleteItemModel deleteItemModel)
		{
			var target = Path.Combine(_directories.LibraryDirectory, deleteItemModel.Target.TrimStart('/'));

			if (deleteItemModel.Type == "directory")
			{
				var dir = new DirectoryInfo(target);
				if (dir.Exists)
				{
					dir.Delete(recursive: true);
				}
			}
			else if (deleteItemModel.Type == "file")
			{
				var file = new FileInfo(target);
				if (file.Exists)
				{
					file.Delete();
				}
			}
		}

		public void CreateFolder(CreateFolderModel createFolderModel)
		{
			var dir = new DirectoryInfo(
				 string.IsNullOrWhiteSpace(createFolderModel.Location)
				 ? _directories.LibraryDirectory
				 : Path.Combine(_directories.LibraryDirectory, createFolderModel.Location));

			if (!dir.Exists)
			{
				throw new DirectoryNotFoundException();
			}

			dir.CreateSubdirectory(createFolderModel.FolderName);
		}

		public async Task<ContentModel> GetContentAsync(string? path = null, uint page = 1, int pageSize = 20)
		{
			var fullPath = !string.IsNullOrWhiteSpace(path) ? Path.Combine(_directories.LibraryDirectory, path) : path;
			if (File.Exists(fullPath))
			{
				return GetFile(fullPath);
			}
			else
			{
				return await GetDirectoryAsync(path, page, pageSize);
			}
		}

		private ContentModel GetFile(string path)
		{
			var mimeProvider = new FileExtensionContentTypeProvider();

			return new ContentModel
			{
				Data = File.ReadAllBytes(path),
				ContentType = mimeProvider.TryGetContentType(path, out var contentType)
					? contentType
					: "application/octet-stream",
				FileName = Path.GetFileName(path),
				Type = "file"
			};
		}

		private async Task<ContentModel> GetDirectoryAsync(string? path = null, uint page = 1, int pageSize = 20)
		{
			var dir = new DirectoryInfo(
				string.IsNullOrWhiteSpace(path)
				? _directories.LibraryDirectory
				: Path.Combine(_directories.LibraryDirectory, path));

			if (!dir.Exists)
			{
				return new ContentModel { Data = null, Type = "directory" };
			}

			var isAdminCheck = await _authorizationService.AuthorizeAsync(
				_httpContextAccessor.HttpContext!.User,
				"Admin");

			var attributesToSkip = FileAttributes.System;
			if (!isAdminCheck.Succeeded)
			{
				attributesToSkip |= FileAttributes.Hidden;
			}

			var allItems = dir.EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = attributesToSkip })
				.AsEnumerable<FileSystemInfo>()
				.Concat(dir.EnumerateFiles().AsEnumerable<FileSystemInfo>())
				.ToArray();

			var allItemsCount = allItems.Length;

			object? pagination = null;

			if (page > 0 && pageSize > 0)
			{
				int skip = (int)((page - 1) * pageSize);

				allItems = allItems
					.Skip(skip)
					.Take(pageSize)
					.ToArray();

				pagination = new
				{
					Page = page,
					PageTotal = (int)Math.Ceiling(allItemsCount / (pageSize * 1.0)),
				};
			}

			var items = allItems
				.Select(i => i switch
				{
					DirectoryInfo di => new
					{
						Type = "directory",
						di.Name,
						di.FullName,
						LastChanged = di.LastWriteTime,
						Path = Path.GetRelativePath(_directories.LibraryDirectory, di.FullName).Replace('\\', '/'),
						IsHidden = di.Attributes.HasFlag(FileAttributes.Hidden)
					},
					FileInfo fi => (object)new
					{
						Type = "file",
						fi.Name,
						fi.FullName,
						LastChanged = fi.LastWriteTime,
						Path = Path.GetRelativePath(_directories.LibraryDirectory, fi.FullName).Replace('\\', '/'),
						Size = fi.Length,
						fi.Extension,
						NameWithoutExtension = Path.GetFileNameWithoutExtension(fi.Name),
						IsHidden = fi.Attributes.HasFlag(FileAttributes.Hidden)
					},
					_ => null
				})
				.ToArray();

			var breadcrumbs = new List<string> { "Library" };

			foreach (var element in path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
			{
				breadcrumbs.Add(element);
			}

			var parentPath = Path.GetRelativePath(_directories.LibraryDirectory, dir.Parent!.FullName).Replace('\\', '/') switch
			{
				".." => null,
				"." => null,
				var pp => pp
			};

			return new ContentModel
			{
				Data = new
				{
					Path = path,
					ParentPath = parentPath,
					Breadcrumbs = breadcrumbs,
					Items = items,
					Pagination = pagination
				},
				Type = "directory"
			}; ;
		}

		public async Task<IEnumerable<PathAndStreamFactory>> GetPathsAndStreamFactoriesAsync(IEnumerable<string> paths)
		{
			var isAdminCheck = await _authorizationService.AuthorizeAsync(
				_httpContextAccessor.HttpContext!.User,
				"Admin");

			var attributesToSkip = FileAttributes.System;
			if (!isAdminCheck.Succeeded)
			{
				attributesToSkip |= FileAttributes.Hidden;
			}

			IEnumerable<PathAndStreamFactory> GetPathsAndStreamFactories(IEnumerable<FileSystemInfo> fileSystemInfos)
			{
				foreach (var fsi in fileSystemInfos)
				{
					if (fsi is FileInfo fileInfo)
					{
						yield return new PathAndStreamFactory(
							Path.GetRelativePath(_directories.LibraryDirectory, fileInfo.FullName),
							() => new FileStream(fsi.FullName, FileMode.Open, FileAccess.Read));
					}
					else if (fsi is DirectoryInfo directoryInfo)
					{
						var allItems = directoryInfo.EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = attributesToSkip })
							.AsEnumerable<FileSystemInfo>()
							.Concat(directoryInfo.EnumerateFiles().AsEnumerable<FileSystemInfo>())
							.ToArray();

						foreach (var result in GetPathsAndStreamFactories(allItems))
						{
							yield return result;
						}
					}
				}
			}

			var items = paths.Select<string, FileSystemInfo>(p =>
			{
				var path = string.IsNullOrWhiteSpace(p)
					? _directories.LibraryDirectory
					: Path.Combine(_directories.LibraryDirectory, p);

				if (File.Exists(path))
				{
					return new FileInfo(path);
				}
				else
				{
					return new DirectoryInfo(path);
				}
			});

			return GetPathsAndStreamFactories(items);

		}

		public async Task UploadFileChunkAsync(UploadFileModel uploadFileModel)
		{
			var isFirstChunk = uploadFileModel.ChunkNumber == 1;
			var isLastChunk = uploadFileModel.ChunkNumber == uploadFileModel.NumberOfChunks;

			var fileDataIndex = uploadFileModel.FileData.IndexOf(";base64,") + 8;
			var fileData = Convert.FromBase64String(uploadFileModel.FileData[fileDataIndex..]);

			var target = Path.Combine(_directories.LibraryDirectory, uploadFileModel.Target.TrimStart('/').Replace('/', '\\'));
			var fileName = Path.GetFileName(uploadFileModel.Target);
			var fileNameWithoutExtention = Path.GetFileNameWithoutExtension(fileName);
			var destination = Path.GetDirectoryName(target);
			var destinationHash = destination!.GetHashCode();

			var tempPath = Path.Combine(
				_directories.TempDirectory,
				$"{fileNameWithoutExtention}_{destinationHash}.tmp");

			if (!Directory.Exists(_directories.TempDirectory))
			{
				Directory.CreateDirectory(_directories.TempDirectory);
			}

			using (var fs = new FileStream(tempPath, isFirstChunk ? FileMode.Create : FileMode.Append, FileAccess.Write))
			{
				await fs.WriteAsync(fileData, 0, fileData.Length);
			}

			if (isLastChunk)
			{
				if (!Directory.Exists(destination))
				{
					Directory.CreateDirectory(destination);
				}

				File.Move(tempPath, target, true);
			}
		}

		public void MoveItem(MoveItemModel moveItemModel)
		{
			var target = Path.Combine(_directories.LibraryDirectory, moveItemModel.Target.TrimStart('/'));
			var destinationFolder = Path.Combine(_directories.LibraryDirectory, moveItemModel.Destination.TrimStart('/'));

			if (moveItemModel.Type == "directory")
			{
				var dir = new DirectoryInfo(target);
				var destination = Path.Combine(destinationFolder, dir.Name);

				if (dir.Exists && Directory.Exists(destinationFolder))
				{
					dir.MoveTo(destination);
				}
			}
			else if (moveItemModel.Type == "file")
			{
				var file = new FileInfo(target);
				var destination = Path.Combine(destinationFolder, file.Name);

				if (file.Exists && Directory.Exists(destinationFolder))
				{
					file.MoveTo(destination);
				}
			}
		}

		private static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
		{
			// Get information about the source directory
			var dir = new DirectoryInfo(sourceDir);

			// Check if the source directory exists
			if (!dir.Exists)
			{
				throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
			}

			// Cache directories before we start copying
			DirectoryInfo[] dirs = dir.GetDirectories();

			// Create the destination directory
			Directory.CreateDirectory(destinationDir);

			// Get the files in the source directory and copy to the destination directory
			foreach (FileInfo file in dir.GetFiles())
			{
				string targetFilePath = Path.Combine(destinationDir, file.Name);
				file.CopyTo(targetFilePath);
			}

			// If recursive and copying subdirectories, recursively call this method
			if (recursive)
			{
				foreach (DirectoryInfo subDir in dirs)
				{
					string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
					CopyDirectory(subDir.FullName, newDestinationDir, true);
				}
			}
		}

		public void CopyItem(CopyItemModel copyItemModel)
		{
			var target = Path.Combine(_directories.LibraryDirectory, copyItemModel.Target.TrimStart('/'));
			var destinationFolder = Path.Combine(_directories.LibraryDirectory, copyItemModel.Destination.TrimStart('/'));

			if (copyItemModel.Type == "directory")
			{
				var dir = new DirectoryInfo(target);
				var destination = Path.Combine(destinationFolder, dir.Name);

				if (dir.Exists && Directory.Exists(destinationFolder))
				{
					CopyDirectory(target, destination, true);
				}
			}
			else if (copyItemModel.Type == "file")
			{
				var file = new FileInfo(target);
				var destination = Path.Combine(destinationFolder, file.Name);

				if (file.Exists && Directory.Exists(destinationFolder))
				{
					file.CopyTo(destination);
				}
			}
		}

		public string GetType(string path)
		{
			var fullPath = !string.IsNullOrWhiteSpace(path) ? Path.Combine(_directories.LibraryDirectory, path) : path;
			if (File.Exists(fullPath))
			{
				return "file";
			}
			else if (Directory.Exists(fullPath))
			{
				return "directory";
			}
			else
			{
				return "";
			}
		}
	}
}
