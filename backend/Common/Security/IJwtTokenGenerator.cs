using System.Security.Claims;

namespace MC.Erp.Common.Security;

public interface IJwtTokenGenerator
{
    string Generate(Guid userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions, string? displayName = null);
}
