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

        // SysDict CRUD
        group.MapGet("/dicts", ListSysDictsAsync).RequireAuthorization();
        group.MapGet("/dicts/group/{groupName}", ListByGroupAsync).RequireAuthorization();
        group.MapGet("/dicts/{id:guid}", GetByIdAsync).RequireAuthorization();
        group.MapPost("/dicts", CreateSysDictAsync).RequireAuthorization("admin");
        group.MapPut("/dicts/{id:guid}", UpdateSysDictAsync).RequireAuthorization("admin");
        group.MapDelete("/dicts/{id:guid}", DeleteSysDictAsync).RequireAuthorization("admin");
    }

    private static async Task<Ok<PagedResult<SysDictDto>>> ListSysDictsAsync(
        SysDictService svc, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await svc.ListAsync(page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<SysDictDto>(
            items.Select(d => new SysDictDto(d.Id, d.GroupName, d.Label, d.Value, d.Sort, d.Enabled, d.Remark)).ToList(),
            total, page, pageSize));
    }

    private static async Task<Ok<List<SysDictDto>>> ListByGroupAsync(
        string groupName, SysDictService svc, CancellationToken ct = default)
    {
        var items = await svc.ListByGroupAsync(groupName, ct);
        return TypedResults.Ok(items.Select(d => new SysDictDto(d.Id, d.GroupName, d.Label, d.Value, d.Sort, d.Enabled, d.Remark)).ToList());
    }

    private static async Task<Results<Ok<SysDictDto>, NotFound>> GetByIdAsync(
        Guid id, SysDictService svc, CancellationToken ct = default)
    {
        var dict = await svc.GetByIdAsync(id, ct);
        return dict is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SysDictDto(dict.Id, dict.GroupName, dict.Label, dict.Value, dict.Sort, dict.Enabled, dict.Remark));
    }

    private static async Task<Results<Created<SysDictDto>, BadRequest<string>>> CreateSysDictAsync(
        CreateSysDictRequest req, SysDictService svc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.GroupName) || string.IsNullOrWhiteSpace(req.Label) || string.IsNullOrWhiteSpace(req.Value))
            return TypedResults.BadRequest("GroupName, Label and Value are required");

        var dict = await svc.CreateAsync(req.GroupName, req.Label, req.Value, req.Sort, req.Enabled, req.Remark, ct);
        return TypedResults.Created($"/api/system/dicts/{dict.Id}", new SysDictDto(dict.Id, dict.GroupName, dict.Label, dict.Value, dict.Sort, dict.Enabled, dict.Remark));
    }

    private static async Task<Results<Ok<SysDictDto>, NotFound>> UpdateSysDictAsync(
        Guid id, UpdateSysDictRequest req, SysDictService svc, CancellationToken ct = default)
    {
        var dict = await svc.UpdateAsync(id, req.Label, req.Value, req.Sort, req.Enabled, req.Remark, ct);
        return dict is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new SysDictDto(dict.Id, dict.GroupName, dict.Label, dict.Value, dict.Sort, dict.Enabled, dict.Remark));
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
}

// DTOs
public sealed record CreateSysDictRequest([Required] string GroupName, [Required] string Label, [Required] string Value, int Sort = 0, bool Enabled = true, string? Remark = null);
public sealed record UpdateSysDictRequest(string? Label, string? Value, int? Sort, bool? Enabled, string? Remark);
public sealed record SysDictDto(Guid Id, string GroupName, string Label, string Value, int Sort, bool Enabled, string? Remark);
public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
