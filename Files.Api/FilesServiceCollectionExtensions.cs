
using Files.Api;

namespace Microsoft.Extensions.DependencyInjection;

public static class FilesServiceCollectionExtensions
{
	public static IServiceCollection AddFiles(
		this IServiceCollection services,
		Action<FilesApiOptions>? configureOptions = null)
	{
		if (configureOptions != null)
		{
			services.Configure(configureOptions);
		}

		services.AddScoped<IFileService, FileService>();
		services.AddSingleton<IDirectories, Directories>();

		return services;
	}
}

