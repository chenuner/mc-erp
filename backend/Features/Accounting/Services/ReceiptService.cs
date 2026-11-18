using System.Text.RegularExpressions;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Common.Security;
using MC.Erp.Features.Accounting.Entities;
using MC.Erp.Features.Accounting.Repositories;
using MC.Erp.Features.Attendance.Services;
using MC.Erp.Features.Storage.Services;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Accounting.Services;

public sealed record UpdateReceiptRequest(Guid Version, Guid SiteId, Guid DocumentTypeDictId,
    Guid Type, Guid? SiteCategoryDictId, DateOnly? ReceiptDate, decimal? Amount,
    string? CurrencyCode, string? MerchantName, string? InvoiceNumber, string? Remark);

public sealed class ReceiptService(ReceiptRepository repo, FileService files, SysDictService dicts,
    AttendanceServices attendance, IOcrService ocr, ICurrentUser user, ILogger<ReceiptService> logger)
{
    public bool OcrConfigured => ocr.IsConfigured;
    public Task<(List<Receipt> Items, int Total)> ListAsync(ReceiptFilter filter, int page, int pageSize)
        => repo.ListAsync(filter, page, pageSize);
    public async Task<Receipt> FindAsync(Guid id) => await repo.FindAsync(id) ?? throw new KeyNotFoundException("票據不存在");

    private async Task<SysDict> DictAsync(Guid id, string group)
    {
        var dict = await dicts.GetByIdAsync(id);
        if (dict is null || !dict.Enabled || dict.GroupName != group) throw new InvalidOperationException("請選擇有效且啟用的分類");
        return dict;
    }

    private async Task CheckSiteAsync(Guid id)
    {
        if (!(await attendance.GetSitesAsync()).Any(site => site.Id == id && site.IsActive))
            throw new InvalidOperationException("請選擇有效且啟用的實際地盤");
    }

    public async Task<Receipt> CreateAsync(Guid siteId, Guid documentTypeDictId)
    {
        await CheckSiteAsync(siteId);
        var documentType = await DictAsync(documentTypeDictId, "AccountingDocumentType");
        CheckModel(documentType.Value);
        var type = await PendingTypeAsync();
        var receipt = new Receipt { SiteId = siteId, DocumentTypeDictId = documentType.Id, Type = type.Id };
        await repo.AddAsync(receipt);
        return await FindAsync(receipt.Id);
    }

    /// <summary>補上或更換票據原圖；有原圖後才可 OCR。</summary>
    public async Task<Receipt> AttachImageAsync(Guid id, IFormFile image)
    {
        var receipt = await FindAsync(id);
        if (receipt.IsConfirmed) throw new InvalidOperationException("已確認票據不可更換原圖");
        if (receipt.OcrStatus == "Processing" && receipt.OcrStartedAt > DateTime.UtcNow.AddMinutes(-3))
            throw new InvalidOperationException("識別進行中，請稍後再更換原圖");
        var mime = await CheckImageAsync(image);
        var previous = receipt.FileItemId;
        await using var upload = image.OpenReadStream();
        var file = await files.上传Async(receipt.Type, Path.GetFileName(image.FileName), mime, image.Length, upload);
        receipt.FileItemId = file.Id;
        receipt.FileItem = file;
        receipt.OcrStatus = "NotStarted";
        receipt.OcrError = null;
        receipt.OcrResultJson = null;
        receipt.OcrText = null;
        receipt.OcrStartedAt = null;
        try { await repo.SaveAsync(receipt); }
        catch
        {
            try { await files.删除Async(file.Id); }
            catch (Exception cleanup) { logger.LogWarning(cleanup, "Receipt attach cleanup failed for {FileId}", file.Id); }
            throw;
        }
        if (previous is { } old)
        {
            // 舊圖已不再被票據引用，替換成功後清理，不影響本次保存
            try { await files.删除Async(old); }
            catch (Exception cleanup) { logger.LogWarning(cleanup, "Receipt image replacement cleanup failed for {FileId}", old); }
        }
        return receipt;
    }

    /// <summary>刪除票據並清理原圖；端點已限定管理員。</summary>
    public async Task DeleteAsync(Guid id)
    {
        var receipt = await FindAsync(id);
        if (receipt.OcrStatus == "Processing" && receipt.OcrStartedAt > DateTime.UtcNow.AddMinutes(-3))
            throw new InvalidOperationException("識別進行中，請稍後再刪除");
        var fileId = receipt.FileItemId;
        await repo.DeleteAsync(receipt);
        if (fileId is { } file)
        {
            // 票據已刪除，原圖不再被引用；清理失敗只記日誌，不影響刪除結果
            try { await files.删除Async(file); }
            catch (Exception cleanup) { logger.LogWarning(cleanup, "Receipt delete cleanup failed for {FileId}", file); }
        }
    }

    private async Task<SysDict> PendingTypeAsync() =>
        (await dicts.ListByGroupAsync("Accounting")).FirstOrDefault(dict => dict.Enabled && dict.Value == "pending")
        ?? throw new InvalidOperationException("請先配置 Accounting 的 pending 字典");

    private static async Task<string> CheckImageAsync(IFormFile image)
    {
        if (image.Length == 0 || image.Length > 4 * 1024 * 1024) throw new InvalidOperationException("圖片須介乎 1 byte 至 4 MB");
        await using var stream = image.OpenReadStream();
        var header = new byte[32];
        var read = await stream.ReadAsync(header);
        return read >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255 ? "image/jpeg"
            : read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : throw new InvalidOperationException("第一版請使用 JPEG 或 PNG 圖片");
    }

    private static void CheckModel(string value)
    {
        if (value is not ("receipt" or "invoice")) throw new InvalidOperationException("票據類型只支援 receipt / invoice");
    }

    public async Task<Receipt> UpdateAsync(Guid id, UpdateReceiptRequest request)
    {
        var receipt = await FindAsync(id);
        if (receipt.Version != request.Version) throw new DbUpdateConcurrencyException();
        if (receipt.OcrStatus == "Processing" && receipt.OcrStartedAt > DateTime.UtcNow.AddMinutes(-3))
            throw new InvalidOperationException("識別進行中，請稍後保存");
        await CheckSiteAsync(request.SiteId);
        var documentType = await DictAsync(request.DocumentTypeDictId, "AccountingDocumentType");
        CheckModel(documentType.Value);
        var type = await DictAsync(request.Type, "Accounting");
        if (request.SiteCategoryDictId is { } siteCategory) await DictAsync(siteCategory, "AccountingSiteCategory");
        // 必填規則與前端一致：保存與確認都要求完整資料
        if (request.ReceiptDate is null) throw new InvalidOperationException("請選擇票據日期");
        if (request.Amount is null or <= 0) throw new InvalidOperationException("請填寫正數金額");
        if (string.IsNullOrWhiteSpace(request.CurrencyCode)) throw new InvalidOperationException("請選擇幣種");
        if (request.SiteCategoryDictId is null) throw new InvalidOperationException("請選擇導出地盤分類");
        if (type.Value == "pending") throw new InvalidOperationException("請選擇實際支出分類，不能是「待分類」");
        if (request.Amount is < 0 or > 9999999999999999.99m || request.Amount != decimal.Round(request.Amount ?? 0, 2) && request.Amount != null)
            throw new InvalidOperationException("金額須為非負數且最多兩位小數");
        var currency = request.CurrencyCode?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(currency) && !Regex.IsMatch(currency, "^[A-Z]{3}$")) throw new InvalidOperationException("幣種須為三位英文字母，例如 HKD");
        if (request.MerchantName?.Length > 255 || request.InvoiceNumber?.Length > 100 || request.Remark?.Length > 255)
            throw new InvalidOperationException("商戶及備註最多 255 字，票據號碼最多 100 字");
        var modelChanged = receipt.DocumentTypeDictId != request.DocumentTypeDictId;
        receipt.SiteId = request.SiteId;
        receipt.DocumentTypeDictId = documentType.Id;
        receipt.DocumentTypeDict = documentType;
        receipt.Type = type.Id;
        receipt.TypeDict = type;
        receipt.SiteCategoryDictId = request.SiteCategoryDictId;
        receipt.SiteCategoryDict = request.SiteCategoryDictId is { } categoryId ? await DictAsync(categoryId, "AccountingSiteCategory") : null;
        receipt.ReceiptDate = request.ReceiptDate;
        receipt.Amount = request.Amount;
        receipt.CurrencyCode = string.IsNullOrEmpty(currency) ? null : currency;
        receipt.MerchantName = request.MerchantName?.Trim();
        receipt.InvoiceNumber = request.InvoiceNumber?.Trim();
        receipt.Remark = request.Remark?.Trim();
        receipt.ManuallyEdited = true;
        receipt.IsConfirmed = false;
        receipt.ConfirmedAt = null;
        receipt.ConfirmedById = null;
        if (modelChanged || receipt.OcrStatus == "Processing") receipt.OcrStatus = "NotStarted";
        await repo.InTransactionAsync(async () =>
        {
            if (receipt.FileItemId is { } fileId) await files.设置类别Async(fileId, type.Id);
            await repo.SaveAsync(receipt);
        });
        return receipt;
    }

    public async Task<Receipt> ConfirmAsync(Guid id, Guid version)
    {
        var receipt = await FindAsync(id);
        if (receipt.Version != version) throw new DbUpdateConcurrencyException();
        if (receipt.OcrStatus == "Processing") throw new InvalidOperationException("識別進行中");
        await CheckSiteAsync(receipt.SiteId);
        await DictAsync(receipt.DocumentTypeDictId, "AccountingDocumentType");
        var type = await DictAsync(receipt.Type, "Accounting");
        if (receipt.SiteCategoryDictId is { } category) await DictAsync(category, "AccountingSiteCategory");
        if (receipt.ReceiptDate is null || receipt.Amount is null or <= 0 || string.IsNullOrEmpty(receipt.CurrencyCode)
            || receipt.SiteCategoryDictId is null || type.Value == "pending")
            throw new InvalidOperationException("請填寫日期、正數金額、幣種、支出分類及導出地盤分類");
        receipt.IsConfirmed = true;
        receipt.ConfirmedAt = DateTime.UtcNow;
        receipt.ConfirmedById = user.UserId;
        await repo.SaveAsync(receipt);
        return receipt;
    }

    public async Task<Receipt> RecognizeAsync(Guid id, Guid? documentTypeDictId = null)
    {
        var receipt = await FindAsync(id);
        if (receipt.IsConfirmed) throw new InvalidOperationException("已確認票據不可重新識別");
        if (receipt.FileItemId is null) throw new InvalidOperationException("尚未上傳原圖，請先上傳圖片再識別");
        if (!ocr.IsConfigured) throw new InvalidOperationException("Azure 識別服務尚未配置，原圖已保存，可手動填寫");
        var documentType = await DictAsync(documentTypeDictId ?? receipt.DocumentTypeDictId, "AccountingDocumentType");
        CheckModel(documentType.Value);
        if (!await repo.StartRecognitionAsync(receipt)) throw new DbUpdateConcurrencyException();
        receipt.DocumentTypeDictId = documentType.Id;
        receipt.DocumentTypeDict = documentType;
        try
        {
            var download = await files.下载Async(receipt.FileItemId.Value) ?? throw new InvalidOperationException("原圖不存在");
            await using var content = download.Content;
            var result = await ocr.RecognizeAsync(content, documentType.Value);
            receipt.OcrResultJson = result.Json;
            receipt.OcrText = result.Text;
            receipt.OcrStatus = "Succeeded";
            if (!receipt.ManuallyEdited)
            {
                receipt.MerchantName = result.MerchantName is { } merchant ? merchant[..Math.Min(merchant.Length, 255)] : null;
                receipt.ReceiptDate = result.Date;
                receipt.Amount = result.Amount;
                receipt.CurrencyCode = result.CurrencyCode is { Length: 3 } ? result.CurrencyCode.ToUpperInvariant() : null;
                receipt.InvoiceNumber = result.InvoiceNumber is { } number ? number[..Math.Min(number.Length, 100)] : null;
                var suggested = SuggestCategory(result);
                var category = (await dicts.ListByGroupAsync("Accounting")).FirstOrDefault(dict => dict.Enabled && dict.Value == suggested);
                if (category != null)
                {
                    receipt.Type = category.Id;
                    receipt.TypeDict = category;
                    await repo.InTransactionAsync(async () =>
                    {
                        await files.设置类别Async(receipt.FileItemId.Value, category.Id);
                        await repo.SaveRecognitionAsync(receipt);
                    });
                    return receipt;
                }
            }
        }
        catch (DbUpdateConcurrencyException) { throw; }
        catch (Exception exception) when (exception is Azure.RequestFailedException or OperationCanceledException or IOException or InvalidOperationException)
        {
            logger.LogWarning("OCR failed for receipt {ReceiptId}: {ErrorType}", receipt.Id, exception.GetType().Name);
            receipt.OcrStatus = "Failed";
            receipt.OcrError = exception is OperationCanceledException ? "識別超時或請求已取消，原圖保留，請重試"
                : exception is InvalidOperationException ? exception.Message : "識別服務失敗，原圖保留，請重試或手動填寫";
        }
        await repo.SaveRecognitionAsync(receipt);
        return receipt;
    }

    public static string? SuggestCategory(OcrResult result)
    {
        if (result.ReceiptType == "Meal") return "meals";
        if (result.ReceiptType?.StartsWith("Fuel&Energy", StringComparison.OrdinalIgnoreCase) == true) return "fuel";
        if (result.ReceiptType?.StartsWith("Transportation", StringComparison.OrdinalIgnoreCase) == true) return "transport";
        var matches = new List<string>();
        var text = result.Text;
        foreach (var (category, keywords) in new (string, string[])[]
        {
            ("materials", ["水泥", "鋼筋", "木材", "cement", "timber", "building material"]),
            ("transport", ["運輸", "運費", "transport", "delivery charge"]),
            ("rental", ["租賃", "租金", "rental", "equipment hire"]),
            ("fuel", ["汽油", "柴油", "diesel", "petrol"]),
            ("meals", ["餐費", "餐廳", "restaurant"])
        })
            if (keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))) matches.Add(category);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<(Stream Content, string Name)> ExportAsync(ReceiptFilter filter)
    {
        var receipts = await repo.ExportAsync(filter);
        if (receipts.Count == 0) throw new KeyNotFoundException("沒有符合條件的圖片");
        if (receipts.Count > 5000 || receipts.Sum(receipt => receipt.FileItem!.SizeBytes) > 250L * 1024 * 1024)
            throw new InvalidOperationException("提取上限為 5000 張 / 250 MB，請縮小日期範圍");
        var entries = receipts.Select(receipt => (receipt.FileItemId!.Value,
            $"{SafeName(receipt.SiteCategoryDict?.Label ?? "未分類地盤")}/{SafeName(receipt.TypeDict.Label)}/"
                + $"{receipt.ReceiptDate:yyyy-MM-dd}_{receipt.Id:N}_{SafeName(receipt.FileItem!.OriginalName)}")).ToList();
        return (await files.打包Async(entries), $"accounting-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }

    public static string SafeName(string name)
    {
        var cleaned = Regex.Replace(name, "[\\\\/:*?\"<>|\\x00-\\x1f]", "_").Replace("..", "_").Trim(' ', '.');
        return string.IsNullOrEmpty(cleaned) ? "image" : cleaned[..Math.Min(cleaned.Length, 100)];
    }
}