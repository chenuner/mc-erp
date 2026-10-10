using MC.Erp.Common.Security;
using MC.Erp.Features.Identity.Repositories;

namespace MC.Erp.Features.Identity.Services;

public sealed class AuthService(
    IUserRepository userRepo,
    IRoleRepository roleRepo,
    IPasswordHasher hasher,
    IJwtTokenGenerator tokenGenerator)
{
    public async Task<string?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await userRepo.GetByUsernameAsync(username, ct);
        if (user is null || !user.IsActive)
            return null;

        if (!hasher.Verify(password, user.PasswordHash))
            return null;

        var roles = await roleRepo.GetRoleNamesAsync(user.Id, ct);
        var permissions = await userRepo.GetPermissionsAsync(user.Id, ct);

        return tokenGenerator.Generate(user.Id, user.Username, roles, permissions);
    }
}
