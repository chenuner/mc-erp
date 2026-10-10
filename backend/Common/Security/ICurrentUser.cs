namespace MC.Erp.Common.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Username { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
    bool IsAuthenticated { get; }
}
