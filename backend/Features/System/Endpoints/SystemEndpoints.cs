using System.ComponentModel.DataAnnotations;
using MC.Erp.Common;
using MC.Erp.Common.Errors;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Repositories;
using MC.Erp.Features.System.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.System.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");
        group.AddEndpointFilter<ExceptionProblemFilter>();

        // SysDictGroup CRUD
        group.MapGet("/dict-groups", ListSysDictGroupsAsync).RequireAuthorization();
        group.MapPost("/dict-groups", CreateSysDictGroupAsync).RequireAuthorization("admin");
        group.MapPut("/dict-groups/{id:guid}", UpdateSysDictGroupAsync).RequireAuthorization("admin");
        group.MapDelete("/dict-groups/{id:guid}", DeleteSysDictGroupAsync).RequireAuthorization("admin");

        // SysDict CRUD
        group.MapGet("/dicts", ListSysDictsAsync).RequireAuthorization();
        group.MapGet("/dicts/group/{groupName}", ListByGroupAsync).RequireAuthorization();
        group.MapGet("/dicts/{id:guid}", GetByIdAsync).RequireAuthorization();
        group.MapPost("/dicts", CreateSysDictAsync).RequireAuthorization("admin");
        group.MapPut("/dicts/reorder", ReorderSysDictsAsync).RequireAuthorization("admin");
        group.MapPut("/dicts/{id:guid}", UpdateSysDictAsync).RequireAuthorization("admin");
        group.MapDelete("/dicts/{id:guid}", DeleteSysDictAsync).RequireAuthorization("admin");
    }

    private static async Task<Ok<List<SysDictGroupDto>>> ListSysDictGroupsAsync(
        SysDictGroupService svc, CancellationToken ct = default)
    {
        var items = await svc.ListAsync(ct);
        return TypedResults.Ok(items.Select(ToDto).ToList());
    }

    private static async Task<Created<SysDictGroupDto>> CreateSysDictGroupAsync(
        CreateSysDictGroupRequest req, SysDictGroupService svc, CancellationToken ct = default)
    {
        var group = await svc.CreateAsync(req.Name, req.Code, req.Sort, req.Remark, ct);
        return TypedResults.Created($"/api/system/dict-groups/{group.Id}",
            new SysDictGroupDto(group.Id, group.Code, group.Name, group.Sort, group.Remark, group.IsSystem, 0, 0));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateSysDictGroupAsync(
        Guid id, UpdateSysDictGroupRequest req, SysDictGroupService svc, CancellationToken ct = default)
    {
        var group = await svc.UpdateAsync(id, req.Name, req.Sort, req.Remark, ct);
        return group is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteSysDictGroupAsync(
        Guid id, SysDictGroupService svc, CancellationToken ct = default)
        => await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Ok<PagedResult<SysDictDto>>> ListSysDictsAsync(
        SysDictService svc, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await svc.ListAsync(page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<SysDictDto>(
            items.Select(ToDto).ToList(),
            total, page, pageSize));
    }

    private static async Task<Ok<List<SysDictDto>>> ListByGroupAsync(
        string groupName, SysDictService svc, CancellationToken ct = default)
    {
        var items = await svc.ListByGroupAsync(groupName, ct);
        return TypedResults.Ok(items.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<SysDictDto>, NotFound>> GetByIdAsync(
        Guid id, SysDictService svc, CancellationToken ct = default)
    {
        var dict = await svc.GetByIdAsync(id, ct);
        return dict is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(dict));
    }

    private static async Task<Created<SysDictDto>> CreateSysDictAsync(
        CreateSysDictRequest req, SysDictService svc, CancellationToken ct = default)
    {
        var dict = await svc.CreateAsync(req.GroupName, req.Label, req.Value, req.Sort, req.Enabled, req.Remark,
            req.ParentId, ct);
        return TypedResults.Created($"/api/system/dicts/{dict.Id}", ToDto(dict));
    }

    private static async Task<Ok<List<SysDictDto>>> ReorderSysDictsAsync(
        ReorderSysDictRequest req, SysDictService svc, CancellationToken ct = default)
    {
        var items = await svc.ReorderAsync(req.GroupName, req.Ids, ct);
        return TypedResults.Ok(items.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<SysDictDto>, NotFound>> UpdateSysDictAsync(
        Guid id, UpdateSysDictRequest req, SysDictService svc, CancellationToken ct = default)
    {
        var dict = await svc.UpdateAsync(id, req.Label, req.Value, req.Sort, req.Enabled, req.Remark,
            req.ParentId is null, req.ParentId, ct);
        return dict is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(dict));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteSysDictAsync(
        Guid id, SysDictService svc, CancellationToken ct = default)
    {
        try
        {
            return await svc.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: 409);
        }
    }

    private static SysDictGroupDto ToDto(SysDictGroupSummary summary)
        => new(summary.Group.Id, summary.Group.Code, summary.Group.Name, summary.Group.Sort,
            summary.Group.Remark, summary.Group.IsSystem, summary.ItemCount, summary.EnabledCount);

    private static SysDictDto ToDto(SysDict dict)
        => new(dict.Id, dict.GroupName, dict.Label, dict.Value, dict.Sort, dict.Enabled, dict.Remark, dict.ParentId);
}

// DTOs
public sealed record CreateSysDictGroupRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(50)] string? Code = null,
    int? Sort = null,
    [MaxLength(255)] string? Remark = null);

public sealed record UpdateSysDictGroupRequest(
    [MaxLength(100)] string? Name,
    int? Sort,
    [MaxLength(255)] string? Remark);

public sealed record SysDictGroupDto(
    Guid Id, string Code, string Name, int Sort, string? Remark, bool IsSystem, int ItemCount, int EnabledCount);

public sealed record CreateSysDictRequest(
    [Required, MaxLength(50)] string GroupName,
    [Required, MaxLength(100)] string Label,
    [MaxLength(50)] string? Value = null,
    int? Sort = null,
    bool? Enabled = null,
    [MaxLength(255)] string? Remark = null,
    /// <summary>上級字典項 ID，留空為頂層</summary>
    Guid? ParentId = null);

public sealed record UpdateSysDictRequest(
    [MaxLength(100)] string? Label,
    [MaxLength(50)] string? Value,
    int? Sort,
    bool? Enabled,
    [MaxLength(255)] string? Remark,
    /// <summary>上級字典項 ID；不傳或傳 null 即改為頂層</summary>
    Guid? ParentId = null);

public sealed record ReorderSysDictRequest(
    [Required, MaxLength(50)] string GroupName,
    [Required] List<Guid> Ids);

// ParentId 為上級字典項（同一分組內的多層結構），頂層為 null
public sealed record SysDictDto(
    Guid Id, string GroupName, string Label, string Value, int Sort, bool Enabled, string? Remark, Guid? ParentId);
