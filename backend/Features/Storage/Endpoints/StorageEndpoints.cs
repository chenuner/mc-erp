using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Storage.Services;
using MC.Erp.Features.System.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Storage.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage").WithTags("Storage");

        // 文件
        group.MapGet("/files", ListFilesAsync).RequireAuthorization();
        group.MapPost("/files", UploadFileAsync)
            .DisableAntiforgery()
            .RequireAuthorization();
        group.MapGet("/files/{id:guid}/download", DownloadFileAsync).RequireAuthorization();
        group.MapDelete("/files/{id:guid}", DeleteFileAsync).RequireAuthorization();
    }

    private static async Task<Ok<List<FileDto>>> ListFilesAsync(
        Guid? categoryId, FileService svc, CancellationToken ct)
    {
        var items = await svc.列表Async(categoryId, ct);
        return TypedResults.Ok(items.Select(x => FileDto.From(x.File, x.Category)).ToList());
    }

    private static async Task<Results<Created<FileDto>, BadRequest<string>>> UploadFileAsync(
        Guid categoryId, IFormFile file, FileService svc, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return TypedResults.BadRequest("文件為空");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var item = await svc.上传Async(categoryId, file.FileName, file.ContentType, file.Length, stream, ct);
            return TypedResults.Created($"/api/storage/files/{item.Id}", FileDto.From(item, item.Category));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadFileAsync(
        Guid id, FileService svc, CancellationToken ct)
    {
        var result = await svc.下载Async(id, ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.File(result.Value.Content, result.Value.File.ContentType, result.Value.File.OriginalName);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteFileAsync(
        Guid id, FileService svc, CancellationToken ct)
    {
        return await svc.删除Async(id, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}

public sealed record FileDto(
    Guid Id, Guid CategoryId, string SiteName, DateTime EventDate, string EventReason,
    string OriginalName, string ContentType, long SizeBytes, string? UploaderName, DateTime CreatedAt)
{
    public static FileDto From(FileItem f, ArchiveCategory c) =>
        new(f.Id, f.CategoryId, c.SiteName, c.EventDate, c.EventReason,
            f.OriginalName, f.ContentType, f.SizeBytes, f.UploaderName, f.CreatedAt);
}
