using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Storage.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Storage.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage").WithTags("Storage");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return TypedResults.Forbid(); }
            catch (InvalidOperationException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
        });

        // 文件
        group.MapGet("/files", ListFilesAsync).RequireAuthorization();
        group.MapPost("/files", UploadFileAsync)
            .DisableAntiforgery()
            .RequireAuthorization();
        group.MapGet("/files/{id:guid}/download", DownloadFileAsync).RequireAuthorization();
        group.MapDelete("/files/{id:guid}", DeleteFileAsync).RequireAuthorization();
    }

    private static async Task<Ok<List<FileDto>>> ListFilesAsync(
        Guid? type, FileService svc, CancellationToken ct)
    {
        var items = await svc.列表Async(type, ct);
        return TypedResults.Ok(items.Select(FileDto.From).ToList());
    }

    private static async Task<Results<Created<FileDto>, BadRequest<string>>> UploadFileAsync(
        Guid type, IFormFile file, FileService svc, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return TypedResults.BadRequest("文件為空");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var item = await svc.上传Async(type, file.FileName, file.ContentType, file.Length, stream, ct);
            return TypedResults.Created($"/api/storage/files/{item.Id}", FileDto.From(item));
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
    Guid Id, Guid? Type, string? TypeLabel, string OriginalName, string ContentType, long SizeBytes, string? UploaderName, DateTime CreatedAt)
{
    public static FileDto From(FileItem f) =>
        new(f.Id, f.Type, f.TypeDict?.Label, f.OriginalName, f.ContentType, f.SizeBytes, f.UploaderName, f.CreatedAt);
}
