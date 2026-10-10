using MC.Erp.Features.Identity.Entities;

namespace MC.Erp.Features.Identity.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<(List<User> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task DeleteAsync(User user, CancellationToken ct = default);
    Task<List<string>> GetPermissionsAsync(Guid userId, CancellationToken ct = default);
}
