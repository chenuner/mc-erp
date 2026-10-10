using System.ComponentModel.DataAnnotations;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Storage.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Storage.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage").WithTags("Storage");

        // 分類字典
        group.MapGet("/categories", ListCategoriesAsync).RequireAuthorization();
        group.MapPost("/categories", CreateCategoryAsync).RequireAuthorization();
        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync).RequireAuthorization();
        group.MapDelete("/categories/{id:guid}", DeleteCategoryAsync).RequireAuthorization();

        // 文件
        group.MapGet("/files", ListFilesAsync).RequireAuthorization();
        group.MapPost("/files", UploadFileAsync)
            .DisableAntiforgery()
            .RequireAuthorization();
        group.MapGet("/files/{id:guid}/download", DownloadFileAsync).RequireAuthorization();
        group.MapDelete("/files/{id:guid}", DeleteFileAsync).RequireAuthorization();
    }

    // --- 分類字典 ---
    private static async Task<Ok<List<CategoryDto>>> ListCategoriesAsync(CategoryService svc, CancellationToken ct)
    {
        var items = await svc.列表Async(ct);
        return TypedResults.Ok(items.Select(x => CategoryDto.From(x.Category, x.FileCount)).ToList());
    }

    private static async Task<Results<Created<CategoryDto>, BadRequest<string>>> CreateCategoryAsync(
        CreateCategoryRequest req, CategoryService svc, CancellationToken ct)
    {
        try
        {
            var category = await svc.创建Async(req.SiteName, req.EventDate, req.EventReason, req.Remark, ct);
            return TypedResults.Created($"/api/storage/categories/{category.Id}", CategoryDto.From(category, 0));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<CategoryDto>, NotFound>> UpdateCategoryAsync(
        Guid id, UpdateCategoryRequest req, CategoryService svc, CancellationToken ct)
    {
        var category = await svc.更新Async(id, req.SiteName, req.EventDate, req.EventReason, req.Remark, ct);
        return category is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CategoryDto.From(category, -1));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> DeleteCategoryAsync(
        Guid id, CategoryService svc, CancellationToken ct)
    {
        try
        {
            return await svc.删除Async(id, ct)
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    // --- 文件 ---
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

// DTOs
public sealed record CategoryDto(Guid Id, string SiteName, DateTime EventDate, string EventReason, string? Remark, int FileCount)
{
    public static CategoryDto From(StorageCategory c, int fileCount) =>
        new(c.Id, c.SiteName, c.EventDate, c.EventReason, c.Remark, fileCount);
}
public sealed record FileDto(
    Guid Id, Guid CategoryId, string SiteName, DateTime EventDate, string EventReason,
    string OriginalName, string ContentType, long SizeBytes, string? UploaderName, DateTime CreatedAt)
{
    public static FileDto From(FileItem f, StorageCategory c) =>
        new(f.Id, f.CategoryId, c.SiteName, c.EventDate, c.EventReason,
            f.OriginalName, f.ContentType, f.SizeBytes, f.UploaderName, f.CreatedAt);
}

// Requests
public sealed record CreateCategoryRequest(
    [Required] string SiteName,
    [Required] DateTime EventDate,
    [Required] string EventReason,
    string? Remark);
public sealed record UpdateCategoryRequest(string? SiteName, DateTime? EventDate, string? EventReason, string? Remark);
