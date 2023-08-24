using Microsoft.AspNetCore.Hosting;

namespace Files.Api
{
	public class Directories : IDirectories
	{
		public Directories(IWebHostEnvironment environment)
		{
			var rootPath = environment.ContentRootPath;
			LibraryDirectory = Path.Combine(new DirectoryInfo(rootPath).Parent!.FullName, @"data\files\library");
			TempDirectory = Path.Combine(new DirectoryInfo(rootPath).Parent!.FullName, @"data\files\temp");
		}

		public string LibraryDirectory { get; }

		public string TempDirectory { get; }
	}
}
