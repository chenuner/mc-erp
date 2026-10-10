using System.ComponentModel.DataAnnotations;
using MC.Erp.Common.Security;
using MC.Erp.Features.Identity.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.Identity.Endpoints;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Identity");

        // 登錄
        group.MapPost("/login", LoginAsync);

        // 用戶管理
        group.MapGet("/users", ListUsersAsync).RequireAuthorization("admin");
        group.MapPost("/users", CreateUserAsync).RequireAuthorization("admin");
        group.MapPut("/users/{id:guid}", UpdateUserAsync).RequireAuthorization("admin");
        group.MapDelete("/users/{id:guid}", DeleteUserAsync).RequireAuthorization("admin");
        group.MapPost("/users/{id:guid}/reset-password", ResetPasswordAsync).RequireAuthorization("admin");

        // 角色管理
        group.MapGet("/roles", ListRolesAsync).RequireAuthorization();
        group.MapPost("/roles", CreateRoleAsync).RequireAuthorization("admin");
        group.MapPut("/roles/{id:guid}", UpdateRoleAsync).RequireAuthorization("admin");
        group.MapDelete("/roles/{id:guid}", DeleteRoleAsync).RequireAuthorization("admin");

        // 用戶-角色關聯
        group.MapPost("/users/{userId:guid}/roles/{roleId:guid}", AssignRoleAsync).RequireAuthorization("admin");
        group.MapDelete("/users/{userId:guid}/roles/{roleId:guid}", RemoveRoleAsync).RequireAuthorization("admin");
    }

    private static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest req, AuthService auth, CancellationToken ct)
    {
        var token = await auth.LoginAsync(req.Username, req.Password, ct);
        return token is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new LoginResponse(token));
    }

    private static async Task<Ok<PagedResult<UserDto>>> ListUsersAsync(
        UserService svc, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await svc.ListAsync(page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<UserDto>(
            items.Select(u => new UserDto(u.Id, u.Username, u.Email, u.DisplayName, u.IsActive)).ToList(),
            total, page, pageSize));
    }

    private static async Task<Results<Created<UserDto>, BadRequest<string>>> CreateUserAsync(
        CreateUserRequest req, UserService svc, CancellationToken ct)
    {
        try
        {
            var user = await svc.CreateAsync(req.Username, req.Password, req.Email, req.DisplayName, ct);
            return TypedResults.Created($"/api/identity/users/{user.Id}",
                new UserDto(user.Id, user.Username, user.Email, user.DisplayName, user.IsActive));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<UserDto>, NotFound>> UpdateUserAsync(
        Guid id, UpdateUserRequest req, UserService svc, CancellationToken ct)
    {
        var user = await svc.UpdateAsync(id, req.Email, req.DisplayName, req.IsActive, ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new UserDto(user.Id, user.Username, user.Email, user.DisplayName, user.IsActive));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteUserAsync(
        Guid id, UserService svc, CancellationToken ct)
    {
        return await svc.DeleteAsync(id, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> ResetPasswordAsync(
        Guid id, ResetPasswordRequest req, UserService svc, CancellationToken ct)
    {
        return await svc.ResetPasswordAsync(id, req.NewPassword, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Ok<PagedResult<RoleDto>>> ListRolesAsync(
        RoleService svc, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await svc.ListAsync(page, pageSize, ct);
        return TypedResults.Ok(new PagedResult<RoleDto>(
            items.Select(r => new RoleDto(r.Id, r.Name, r.Description)).ToList(),
            total, page, pageSize));
    }

    private static async Task<Results<Created<RoleDto>, BadRequest<string>>> CreateRoleAsync(
        CreateRoleRequest req, RoleService svc, CancellationToken ct)
    {
        try
        {
            var role = await svc.CreateAsync(req.Name, req.Description, ct);
            return TypedResults.Created($"/api/identity/roles/{role.Id}",
                new RoleDto(role.Id, role.Name, role.Description));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<RoleDto>, NotFound>> UpdateRoleAsync(
        Guid id, UpdateRoleRequest req, RoleService svc, CancellationToken ct)
    {
        var role = await svc.UpdateAsync(id, req.Name, req.Description, ct);
        return role is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new RoleDto(role.Id, role.Name, role.Description));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteRoleAsync(
        Guid id, RoleService svc, CancellationToken ct)
    {
        return await svc.DeleteAsync(id, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<NoContent> AssignRoleAsync(
        Guid userId, Guid roleId, RoleService svc, CancellationToken ct)
    {
        await svc.AssignToUserAsync(userId, roleId, ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RemoveRoleAsync(
        Guid userId, Guid roleId, RoleService svc, CancellationToken ct)
    {
        await svc.RemoveFromUserAsync(userId, roleId, ct);
        return TypedResults.NoContent();
    }
}

// DTOs
public sealed record LoginRequest([Required] string Username, [Required] string Password);
public sealed record LoginResponse(string Token);
public sealed record CreateUserRequest([Required] string Username, [Required] string Password, string? Email, string? DisplayName);
public sealed record UpdateUserRequest(string? Email, string? DisplayName, bool? IsActive);
public sealed record ResetPasswordRequest([Required] string NewPassword);
public sealed record CreateRoleRequest([Required] string Name, string? Description);
public sealed record UpdateRoleRequest(string? Name, string? Description);
public sealed record UserDto(Guid Id, string Username, string? Email, string? DisplayName, bool IsActive);
public sealed record RoleDto(Guid Id, string Name, string? Description);
public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
