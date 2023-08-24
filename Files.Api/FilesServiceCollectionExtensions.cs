
using Files.Api;

namespace Microsoft.Extensions.DependencyInjection;

public static class FilesServiceCollectionExtensions
{
	public static IServiceCollection AddFiles(
		this IServiceCollection services)
	{
		services.AddScoped<IFileService, FileService>();
		services.AddSingleton<IDirectories, Directories>();

		return services;
	}
}

