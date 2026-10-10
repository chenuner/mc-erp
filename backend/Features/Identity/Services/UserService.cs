using MC.Erp.Common.Security;
using MC.Erp.Features.Identity.Entities;
using MC.Erp.Features.Identity.Repositories;

namespace MC.Erp.Features.Identity.Services;

public sealed class UserService(IUserRepository userRepo, IPasswordHasher hasher)
{
    public async Task<User> CreateAsync(string username, string password, string? email, string? displayName, CancellationToken ct = default)
    {
        var existing = await userRepo.GetByUsernameAsync(username, ct);
        if (existing is not null)
            throw new InvalidOperationException("用戶名已存在");

        var user = new User
        {
            Username = username,
            PasswordHash = hasher.Hash(password),
            Email = email,
            DisplayName = displayName
        };

        await userRepo.AddAsync(user, ct);
        return user;
    }

    public async Task<User?> UpdateAsync(Guid id, string? email, string? displayName, bool? isActive, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return null;

        if (email is not null) user.Email = email;
        if (displayName is not null) user.DisplayName = displayName;
        if (isActive is not null) user.IsActive = isActive.Value;

        await userRepo.UpdateAsync(user, ct);
        return user;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return false;

        await userRepo.DeleteAsync(user, ct);
        return true;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => userRepo.GetByIdAsync(id, ct);

    public Task<(List<User> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default)
        => userRepo.ListAsync(page, pageSize, ct);

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return false;

        user.PasswordHash = hasher.Hash(newPassword);
        await userRepo.UpdateAsync(user, ct);
        return true;
    }
}
