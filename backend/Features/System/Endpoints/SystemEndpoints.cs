using System.ComponentModel.DataAnnotations;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.System.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        // 歸檔分類字典（系統級共享，地盤 / 時間 / 事件原因）
        group.MapGet("/categories", ListCategoriesAsync).RequireAuthorization();
        group.MapPost("/categories", CreateCategoryAsync).RequireAuthorization();
        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync).RequireAuthorization();
        group.MapDelete("/categories/{id:guid}", DeleteCategoryAsync).RequireAuthorization();
    }

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
            return TypedResults.Created($"/api/system/categories/{category.Id}", CategoryDto.From(category, 0));
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
}

// DTOs
public sealed record CategoryDto(
    Guid Id, string SiteName, DateTime EventDate, string EventReason, string? Remark, int FileCount)
{
    public static CategoryDto From(ArchiveCategory c, int fileCount) =>
        new(c.Id, c.SiteName, c.EventDate, c.EventReason, c.Remark, fileCount);
}

// Requests
public sealed record CreateCategoryRequest(
    [Required] string SiteName,
    [Required] DateTime EventDate,
    [Required] string EventReason,
    string? Remark);

public sealed record UpdateCategoryRequest(string? SiteName, DateTime? EventDate, string? EventReason, string? Remark);
