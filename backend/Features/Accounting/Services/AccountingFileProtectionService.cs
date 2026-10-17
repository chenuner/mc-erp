using MC.Erp.Features.Accounting.Repositories;

namespace MC.Erp.Features.Accounting.Services;

public sealed class AccountingFileProtectionService(ReceiptRepository repo)
{
    public Task<bool> IsReferencedAsync(Guid fileId) => repo.IsFileReferencedAsync(fileId);
}