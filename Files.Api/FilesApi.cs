using Files.Api.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.IO.Compression;

namespace Files.Api
{
    public static class FilesApi
    {
        private static readonly string[] s_httpMethods = ["Get", "Head"];

        public static RouteGroupBuilder MapFiles(this IEndpointRouteBuilder routes, bool requireAuthorization = true)
        {
            var options = routes.ServiceProvider.GetRequiredService<IOptions<FilesApiOptions>>();

            var group = routes.MapGroup("/files").WithTags("Files");

            var catchAllHandler = group.MapMethods("{**path}", s_httpMethods,
                async (string? path, [FromQuery] uint? page, [FromQuery] int? pageSize, IFileService fileService, IHttpContextAccessor httpContextAccessor) =>
            {
                var contentModel = await fileService.GetContentAsync(path, page ?? 1, pageSize ?? 20);

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
            });

            var downloadHandler = group.MapPost("/download", async (HttpRequest request, [FromServices] IFileService fileService) =>
            {
                var form = await request.ReadFormAsync();
                var paths = form["paths"].ToArray<string>();
                switch (paths.Length)
                {
                    case 0: return Results.Problem("\"paths\" cannot be empty.", statusCode: StatusCodes.Status400BadRequest);
                    case 1 when fileService.GetType(paths[0]) == "file":
                        {
                            var contentModel = await fileService.GetContentAsync(paths[0], 0, -1);
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
            });

            var thumbnailHandler = group.MapGet("/Thumbnail/{**path}", async (string? path, [FromQuery] int? size, IThumbnailService thumbnailService, IDirectories directories) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Results.Problem("\"path\" cannot be empty.", statusCode: StatusCodes.Status400BadRequest);
                }

                var rel = path.TrimStart('/');
                try
                {
                    // URL-decode percent-encoded characters (e.g. %20 -> space)
                    rel = Uri.UnescapeDataString(rel);
                }
                catch { }

                var fullPath = string.IsNullOrWhiteSpace(rel)
                    ? directories.LibraryDirectory
                    : Path.Combine(directories.LibraryDirectory, rel.Replace('/', Path.DirectorySeparatorChar));

                var thumb = await thumbnailService.GetThumbnailDataUriAsync(fullPath, size ?? 128);
                if (thumb == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new { Path = path, Thumbnail = thumb });
            });

            var createFolderHandler = group.MapPost("/CreateFolder", ([FromBody] CreateFolderModel createFolderModel, IFileService fileService) =>
            {
                fileService.CreateFolder(createFolderModel);
            });

            var createUrlHandler = group.MapPost("/CreateURL", async ([FromBody] CreateURLModel createURLModel, IFileService fileService) =>
            {
                await fileService.CreateURLAsync(createURLModel);
            });

            var changeItemNameHandler = group.MapPost("/ChangeItemName", ([FromBody] ChangeItemNameModel changeItemNameModel, IFileService fileService) =>
            {
                fileService.ChangeItemName(changeItemNameModel);
            });

            var deleteItemHandler = group.MapPost("/DeleteItem", ([FromBody] DeleteItemModel deleteItemModel, IFileService fileService) =>
            {
                fileService.DeleteItem(deleteItemModel);
            });

            var moveItemHandler = group.MapPost("/MoveItem", ([FromBody] MoveItemModel moveItemModel, IFileService fileService) =>
            {
                fileService.MoveItem(moveItemModel);
            });

            var copyItemHandler = group.MapPost("/CopyItem", ([FromBody] CopyItemModel copyItemModel, IFileService fileService) =>
            {
                fileService.CopyItem(copyItemModel);
            });

            var toggleItemHiddenHandler = group.MapPost("/ToggleItemHidden", ([FromBody] ToggleItemHiddenModel toggleItemHiddenModel, IFileService fileService) =>
            {
                fileService.ToggleItemHidden(toggleItemHiddenModel);
            });

            var uploadFileChunkHandler = group.MapPost("/UploadFileChunk", async ([FromBody] UploadFileModel uploadFileModel, IFileService fileService) =>
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
            });

            if (requireAuthorization)
            {
                catchAllHandler.RequireAuthorization();
                downloadHandler.RequireAuthorization();
                thumbnailHandler.RequireAuthorization();
                createFolderHandler.RequireAuthorization(options.Value.AdminPolicyName);
                createUrlHandler.RequireAuthorization(options.Value.AdminPolicyName);
                changeItemNameHandler.RequireAuthorization(options.Value.AdminPolicyName);
                deleteItemHandler.RequireAuthorization(options.Value.AdminPolicyName);
                moveItemHandler.RequireAuthorization(options.Value.AdminPolicyName);
                copyItemHandler.RequireAuthorization(options.Value.AdminPolicyName);
                toggleItemHiddenHandler.RequireAuthorization(options.Value.AdminPolicyName);
                uploadFileChunkHandler.RequireAuthorization(options.Value.AdminPolicyName);
            }

            return group;
        }
    }
}
