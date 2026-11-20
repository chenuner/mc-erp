using System.Text.Json;
using MC.Erp.Common;
using MC.Erp.Common.Errors;
using MC.Erp.Features.Accounting.Entities;
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
            return TypedResults.Ok(new PagedResult<ReceiptResponse>(
                result.Items.Select(ReceiptResponse.From).ToList(), result.Total, currentPage, size));
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
            var receipt = await service.FindAsync(id);
            if (receipt.FileItemId is not { } fileId) throw new KeyNotFoundException("原圖不存在");
            var download = await files.下载Async(fileId) ?? throw new KeyNotFoundException("原圖不存在");
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

public sealed record RecognizeResponse(ReceiptResponse Receipt, OcrSuggestion Fields);

public sealed record ReceiptResponse(Guid Id, Guid Version, Guid? FileItemId, string? FileName, string? Md5,
    long? SizeBytes, Guid DocumentTypeDictId, string DocumentTypeLabel,
    Guid Type, string TypeLabel, Guid? SiteCategoryDictId, string? SiteCategoryLabel,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError,
    string? OcrText, JsonElement? OcrResult)
{
    public static ReceiptResponse From(Receipt receipt) => new(receipt.Id, receipt.Version,
        receipt.FileItemId, receipt.FileItem?.OriginalName, receipt.FileItem?.Md5, receipt.FileItem?.SizeBytes,
        receipt.DocumentTypeDictId, receipt.DocumentTypeDict.Label,
        receipt.Type, receipt.TypeDict.Label, receipt.SiteCategoryDictId, receipt.SiteCategoryDict?.Label,
        receipt.ReceiptDate, receipt.Amount, receipt.CurrencyCode, receipt.MerchantName,
        receipt.InvoiceNumber, receipt.Remark, receipt.OcrStatus, receipt.OcrError, receipt.OcrText,
        receipt.OcrResultJson == null ? null : JsonSerializer.Deserialize<JsonElement>(receipt.OcrResultJson));
}