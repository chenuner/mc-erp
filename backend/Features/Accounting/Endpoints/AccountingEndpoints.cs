using System.Text.Json;
using MC.Erp.Common;
using MC.Erp.Common.Errors;
using MC.Erp.Features.Accounting.Repositories;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Features.Storage.Services;

namespace MC.Erp.Features.Accounting.Endpoints;

public static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounting").WithTags("Accounting").RequireAuthorization("admin")
            .AddEndpointFilter<ExceptionProblemFilter>();
        group.MapGet("/status", (ReceiptService service) => TypedResults.Ok(new { ocrConfigured = service.OcrConfigured }));
        group.MapGet("/receipts", async (Guid? documentTypeDictId, Guid? type,
            Guid? siteCategoryDictId, DateOnly? start, DateOnly? end,
            int? page, int? pageSize, ReceiptService service) =>
        {
            var filter = Filter(documentTypeDictId, type, siteCategoryDictId, start, end);
            var currentPage = Math.Clamp(page ?? 1, 1, 1000000);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var result = await service.ListAsync(filter, currentPage, size);
            // totals 按整份篩選結果算（不是當前頁）；金額按幣種分開，不混加
            return TypedResults.Ok(new PagedResult<ReceiptListResponse, ReceiptTotals>(
                result.Items.Select(ReceiptListResponse.From).ToList(), result.Total, currentPage, size, result.Totals));
        });
        group.MapPost("/receipts", async (CreateReceiptRequest request, ReceiptService service) =>
        {
            var receipt = await service.CreateAsync(request.DocumentTypeDictId);
            return TypedResults.Created($"/api/accounting/receipts/{receipt.Id}", ReceiptResponse.From(receipt));
        });
        group.MapPost("/receipts/{id:guid}/image", async (Guid id, IFormFile file, ReceiptService service) =>
            TypedResults.Ok(ReceiptResponse.From(await service.AttachImageAsync(id, file)))).DisableAntiforgery();
        group.MapGet("/receipts/export", async (Guid? documentTypeDictId, Guid? type,
            Guid? siteCategoryDictId, DateOnly? start, DateOnly? end, ReceiptService service) =>
        {
            var archive = await service.ExportAsync(Filter(documentTypeDictId, type, siteCategoryDictId, start, end));
            return TypedResults.File(archive.Content, "application/zip", archive.Name);
        });
        group.MapGet("/receipts/{id:guid}", async (Guid id, ReceiptService service) => TypedResults.Ok(ReceiptResponse.From(await service.FindAsync(id))));
        group.MapPut("/receipts/{id:guid}", async (Guid id, UpdateReceiptRequest request, ReceiptService service) =>
            TypedResults.Ok(ReceiptResponse.From(await service.UpdateAsync(id, request))));
        group.MapPost("/receipts/{id:guid}/recognize", async (Guid id, Guid? documentTypeDictId, ReceiptService service) =>
        {
            // 回票據（投影過，和 GET 詳情口徑一致）＋本次識別建議，前端要後者去填空白欄位
            var (receipt, fields) = await service.RecognizeAsync(id, documentTypeDictId);
            return TypedResults.Ok(new RecognizeResponse(ReceiptResponse.From(receipt), fields));
        });
        group.MapDelete("/receipts/{id:guid}", async (Guid id, ReceiptService service) =>
        {
            await service.DeleteAsync(id);
            return TypedResults.NoContent();
        });
        group.MapGet("/receipts/{id:guid}/image", async (Guid id, ReceiptService service, FileService files) =>
        {
            var fileId = (await service.FindAsync(id)).FileItemId;
            if (fileId is null) throw new KeyNotFoundException("原圖不存在");
            var download = await files.下载Async(fileId.Value) ?? throw new KeyNotFoundException("原圖不存在");
            return TypedResults.File(download.Content, download.File.ContentType, download.File.OriginalName);
        });
    }

    private static ReceiptFilter Filter(Guid? documentType, Guid? type, Guid? siteCategory,
        DateOnly? start, DateOnly? end)
    {
        if (start > end) throw new InvalidOperationException("開始日期不能晚於結束日期");
        return new(documentType, type, siteCategory, start, end);
    }
}

public sealed record CreateReceiptRequest(Guid DocumentTypeDictId);

/// <summary>識別接口的回應：票據本體（詳情口徑）＋本次識別建議。</summary>
public sealed record RecognizeResponse(ReceiptResponse Receipt, OcrSuggestion Fields);

/// <summary>列表一行：只有票據欄位。OCR 原文和識別建議不在型別裡，打開詳情再拿。</summary>
public sealed record ReceiptListResponse(Guid Id, Guid Version, Guid? FileItemId, string? FileName, string? Md5,
    long? SizeBytes, Guid DocumentTypeDictId, string DocumentTypeLabel,
    Guid Type, string TypeLabel, Guid? SiteCategoryDictId, string? SiteCategoryLabel,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError)
{
    public static ReceiptListResponse From(ReceiptListItem item) => new(item.Id, item.Version,
        item.FileItemId, item.FileName, item.Md5, item.SizeBytes,
        item.DocumentTypeDictId, item.DocumentTypeLabel, item.Type, item.TypeLabel,
        item.SiteCategoryDictId, item.SiteCategoryLabel, item.ReceiptDate, item.Amount, item.CurrencyCode,
        item.MerchantName, item.InvoiceNumber, item.Remark, item.OcrStatus, item.OcrError);
}

public sealed record ReceiptResponse(Guid Id, Guid Version, Guid? FileItemId, string? FileName, string? Md5,
    long? SizeBytes, Guid DocumentTypeDictId, string DocumentTypeLabel,
    Guid Type, string TypeLabel, Guid? SiteCategoryDictId, string? SiteCategoryLabel,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError,
    string? OcrText, OcrSuggestion? OcrResult)
{
    public static ReceiptResponse From(ReceiptDetail detail) => new(detail.Id, detail.Version,
        detail.FileItemId, detail.FileName, detail.Md5, detail.SizeBytes,
        detail.DocumentTypeDictId, detail.DocumentTypeLabel, detail.Type, detail.TypeLabel,
        detail.SiteCategoryDictId, detail.SiteCategoryLabel, detail.ReceiptDate, detail.Amount, detail.CurrencyCode,
        detail.MerchantName, detail.InvoiceNumber, detail.Remark, detail.OcrStatus, detail.OcrError,
        detail.OcrText, ReadSuggestion(detail.OcrResultJson));

    /// <summary>只回票據欄位。舊資料存的是 Azure 原始 JSON，解析不出欄位就當沒有，避免把整包原文回給前端。</summary>
    private static OcrSuggestion? ReadSuggestion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var suggestion = JsonSerializer.Deserialize<OcrSuggestion>(json);
            return suggestion?.ReviewFields is null ? null : suggestion;
        }
        catch (JsonException) { return null; }
    }
}