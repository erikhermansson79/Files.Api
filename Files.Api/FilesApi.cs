using Files.Api.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using System.IO.Compression;

namespace Files.Api
{
	public static class FilesApi
	{
		public static RouteGroupBuilder MapFiles(this IEndpointRouteBuilder routes, FilesApiOptions? options = null)
		{
			options ??= new FilesApiOptions();

			var group = routes.MapGroup("/files").WithGroupName("Files");

			group.MapMethods("{**path}", new[] { "Get", "Head" },
				async (string? path, [FromQuery] uint? page, [FromQuery] uint? pageSize, IFileService fileService, IHttpContextAccessor httpContextAccessor) =>
			{
				var contentModel = await fileService.GetContentAsync(path, page, pageSize);

				if (contentModel.Data == null)
				{
					return Results.NotFound();
				}

				var download = httpContextAccessor.HttpContext?.Request.Query.Keys.Contains("download", StringComparer.OrdinalIgnoreCase);

				return contentModel.Type switch
				{
					"file" => download == true
						? Results.File((byte[])contentModel.Data, contentModel.ContentType!, contentModel.FileName!)
						: Results.File((byte[])contentModel.Data, contentModel.ContentType!),
					_ => Results.Ok(contentModel.Data),
				};
			})
			.RequireAuthorization();

			group.MapPost("/download", async (HttpRequest request, [FromServices] IFileService fileService) =>
			{
				var form = await request.ReadFormAsync();
				var paths = form["paths"].ToArray<string>();
				switch (paths.Length)
				{
					case 0: return Results.Problem("\"paths\" cannot be empty.", statusCode: StatusCodes.Status400BadRequest);
					case 1 when fileService.GetType(paths[0]) == "file":
						{
							var contentModel = await fileService.GetContentAsync(paths[0], null, null);
							if (contentModel.Data == null)
							{
								return Results.NotFound();
							}

							return Results.File((byte[])contentModel.Data, contentModel.ContentType!, contentModel.FileName!);
						}
					default:
						var syncIOFeature = request.HttpContext.Features.Get<IHttpBodyControlFeature>();
						syncIOFeature!.AllowSynchronousIO = true;
						return Results.Stream(async outputStream =>
						{
							using var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Create);
							foreach (var pathAndStreamFactory in await fileService.GetPathsAndStreamFactoriesAsync(paths))
							{
								var zipEntry = zipArchive.CreateEntry(pathAndStreamFactory.Path);
								using var zipStream = zipEntry.Open();
								using var stream = pathAndStreamFactory.StreamFactory();
								try
								{
									await stream.CopyToAsync(zipStream);
								}
								catch (Exception ex)
								{
								}
							}
						}, "application/zip", $"Download-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.zip");
				}
			})
			.RequireAuthorization();

			group.MapPost("/CreateFolder", ([FromBody] CreateFolderModel createFolderModel, IFileService fileService) =>
			{
				fileService.CreateFolder(createFolderModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/ChangeItemName", ([FromBody] ChangeItemNameModel changeItemNameModel, IFileService fileService) =>
			{
				fileService.ChangeItemName(changeItemNameModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/DeleteItem", ([FromBody] DeleteItemModel deleteItemModel, IFileService fileService) =>
			{
				fileService.DeleteItem(deleteItemModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/MoveItem", ([FromBody] MoveItemModel moveItemModel, IFileService fileService) =>
			{
				fileService.MoveItem(moveItemModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/CopyItem", ([FromBody] CopyItemModel copyItemModel, IFileService fileService) =>
			{
				fileService.CopyItem(copyItemModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/ToggleItemHidden", ([FromBody] ToggleItemHiddenModel toggleItemHiddenModel, IFileService fileService) =>
			{
				fileService.ToggleItemHidden(toggleItemHiddenModel);
			})
			.RequireAuthorization(options.AdminPolicyName);

			group.MapPost("/UploadFileChunk", async ([FromBody] UploadFileModel uploadFileModel, IFileService fileService) =>
			{
				try
				{
					await fileService.UploadFileChunkAsync(uploadFileModel);

					return Results.Ok();
				}
				catch (Exception ex)
				{
					return Results.Content(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
				}
			})
			.RequireAuthorization(options.AdminPolicyName);

			return group;
		}
	}
}
