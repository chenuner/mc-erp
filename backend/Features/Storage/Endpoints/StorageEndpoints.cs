using MC.Erp.Common;
using MC.Erp.Common.Errors;
using MC.Erp.Features.Storage.Entities;
using MC.Erp.Features.Storage.Services;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Storage.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage").WithTags("Storage");
        group.AddEndpointFilter<ExceptionProblemFilter>();

        // 文件
        group.MapGet("/files", ListFilesAsync).RequireAuthorization();
        // 可以當文件類別的字典項（含分組），前端用它在篩選與上傳裡建樹
        group.MapGet("/types", ListTypesAsync).RequireAuthorization();
        group.MapPost("/files", UploadFileAsync)
            .DisableAntiforgery()
            .RequireAuthorization();
        group.MapGet("/files/{id:guid}/download", DownloadFileAsync).RequireAuthorization();
        // 內容尋址：同 MD5 的物理文件唯一（上傳已去重），前端用它做快取
        group.MapGet("/files/by-md5/{md5}/download", DownloadFileByMd5Async).RequireAuthorization();
        group.MapDelete("/files/{id:guid}", DeleteFileAsync).RequireAuthorization();
    }

    private static async Task<Ok<PagedResult<FileDto>>> ListFilesAsync(
        string? type, int? page, int? pageSize, FileService svc, CancellationToken ct)
    {
        var currentPage = Math.Clamp(page ?? 1, 1, 1000000);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var types = await svc.解析類別Async(type, ct);
        var (items, total) = await svc.列表Async(types, currentPage, size, ct);
        return TypedResults.Ok(new PagedResult<FileDto>(items.Select(FileDto.From).ToList(), total, currentPage, size));
    }

    private static async Task<Ok<StorageTypesDto>> ListTypesAsync(FileService svc, CancellationToken ct)
    {
        var (groups, dicts) = await svc.類別選項Async(ct);
        return TypedResults.Ok(new StorageTypesDto(
            groups.Select(group => ToDto(group, dicts)).ToList(),
            dicts.Select(ToDto).ToList()));
    }

    private static async Task<Results<Created<FileDto>, ValidationProblem>> UploadFileAsync(
        Guid type, IFormFile file, FileService svc, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["文件為空"] });
        }

        await using var stream = file.OpenReadStream();
        var item = await svc.上传Async(type, file.FileName, file.ContentType, file.Length, stream, ct);
        return TypedResults.Created($"/api/storage/files/{item.Id}", FileDto.From(item));
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadFileAsync(
        Guid id, FileService svc, CancellationToken ct)
    {
        var result = await svc.下载Async(id, ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.File(result.Value.Content, result.Value.File.ContentType, result.Value.File.OriginalName);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadFileByMd5Async(
        string md5, FileService svc, CancellationToken ct)
    {
        var result = await svc.按Md5下载Async(md5, ct);
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

    private static SysDictGroupDto ToDto(SysDictGroup group, List<SysDict> dicts)
    {
        var items = dicts.Where(dict => dict.GroupName == group.Code).ToList();
        return new SysDictGroupDto(group.Id, group.Code, group.Name, group.Sort, group.Remark, group.IsSystem,
            items.Count, items.Count(dict => dict.Enabled));
    }

    private static SysDictDto ToDto(SysDict dict)
        => new(dict.Id, dict.GroupName, dict.Label, dict.Value, dict.Sort, dict.Enabled, dict.Remark, dict.ParentId);
}

public sealed record FileDto(
    Guid Id, Guid? Type, string OriginalName, string ContentType, long SizeBytes, string? UploaderName, DateTime CreatedAt, string? Md5)
{
    public static FileDto From(FileItem f) =>
        new(f.Id, f.Type, f.OriginalName, f.ContentType, f.SizeBytes, f.UploaderName, f.CreatedAt, f.Md5);
}

/// <summary>文件類別可選項：白名單分組 + 它們底下的字典項（前端據此建樹，不必再拉整份字典）。</summary>
public sealed record StorageTypesDto(List<SysDictGroupDto> Groups, List<SysDictDto> Dicts);
