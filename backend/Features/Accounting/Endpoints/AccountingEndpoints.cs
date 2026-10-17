using System.Text.Json;
using MC.Erp.Features.Accounting.Entities;
using MC.Erp.Features.Accounting.Repositories;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Features.Storage.Services;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Features.Accounting.Endpoints;

public static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounting").WithTags("Accounting").RequireAuthorization("admin");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (KeyNotFoundException exception) { return TypedResults.Problem(exception.Message, statusCode: 404); }
            catch (InvalidOperationException exception) { return TypedResults.Problem(exception.Message, statusCode: 400); }
            catch (DbUpdateConcurrencyException) { return TypedResults.Problem("資料已更新或識別進行中，請重新載入", statusCode: 409); }
            catch (IOException) { return TypedResults.Problem("原圖讀取或打包失敗，請檢查文件存儲後重試", statusCode: 500); }
        });
        group.MapGet("/status", (ReceiptService service) => TypedResults.Ok(new { ocrConfigured = service.OcrConfigured }));
        group.MapGet("/receipts", async (Guid? siteId, Guid? documentTypeDictId, Guid? type,
            Guid? siteCategoryDictId, DateOnly? start, DateOnly? end, bool? isConfirmed,
            int? page, int? pageSize, ReceiptService service) =>
        {
            var filter = Filter(siteId, documentTypeDictId, type, siteCategoryDictId, start, end, isConfirmed);
            var currentPage = Math.Clamp(page ?? 1, 1, 1000000);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var result = await service.ListAsync(filter, currentPage, size);
            return TypedResults.Ok(new { items = result.Items.Select(ReceiptResponse.From), result.Total, page = currentPage, pageSize = size });
        });
        group.MapPost("/receipts", async (Guid siteId, Guid documentTypeDictId, IFormFile file, ReceiptService service) =>
        {
            var receipt = await service.UploadAsync(siteId, documentTypeDictId, file);
            return TypedResults.Created($"/api/accounting/receipts/{receipt.Id}", ReceiptResponse.From(receipt));
        }).DisableAntiforgery();
        group.MapGet("/receipts/export", async (Guid? siteId, Guid? documentTypeDictId, Guid? type,
            Guid? siteCategoryDictId, DateOnly? start, DateOnly? end, bool? isConfirmed, ReceiptService service) =>
        {
            var archive = await service.ExportAsync(Filter(siteId, documentTypeDictId, type, siteCategoryDictId, start, end, isConfirmed));
            return TypedResults.File(archive.Content, "application/zip", archive.Name);
        });
        group.MapGet("/receipts/{id:guid}", async (Guid id, ReceiptService service) => TypedResults.Ok(ReceiptResponse.From(await service.FindAsync(id))));
        group.MapPut("/receipts/{id:guid}", async (Guid id, UpdateReceiptRequest request, ReceiptService service) =>
            TypedResults.Ok(ReceiptResponse.From(await service.UpdateAsync(id, request))));
        group.MapPost("/receipts/{id:guid}/recognize", async (Guid id, Guid? documentTypeDictId, ReceiptService service) =>
            TypedResults.Ok(ReceiptResponse.From(await service.RecognizeAsync(id, documentTypeDictId))));
        group.MapPost("/receipts/{id:guid}/confirm", async (Guid id, ConfirmReceiptRequest request, ReceiptService service) =>
            TypedResults.Ok(ReceiptResponse.From(await service.ConfirmAsync(id, request.Version))));
        group.MapGet("/receipts/{id:guid}/image", async (Guid id, ReceiptService service, FileService files) =>
        {
            var receipt = await service.FindAsync(id);
            var download = await files.下载Async(receipt.FileItemId) ?? throw new KeyNotFoundException("原圖不存在");
            return TypedResults.File(download.Content, download.File.ContentType, download.File.OriginalName);
        });
    }

    private static ReceiptFilter Filter(Guid? siteId, Guid? documentType, Guid? type, Guid? siteCategory,
        DateOnly? start, DateOnly? end, bool? confirmed)
    {
        if (start > end) throw new InvalidOperationException("開始日期不能晚於結束日期");
        return new(siteId, documentType, type, siteCategory, start, end, confirmed);
    }
}

public sealed record ConfirmReceiptRequest(Guid Version);

public sealed record ReceiptResponse(Guid Id, Guid Version, Guid FileItemId, string FileName, string? Md5,
    long SizeBytes, Guid SiteId, string SiteName, Guid DocumentTypeDictId, string DocumentTypeLabel,
    Guid? Type, string? TypeLabel, Guid? SiteCategoryDictId, string? SiteCategoryLabel,
    DateOnly? ReceiptDate, decimal? Amount, string? CurrencyCode, string? MerchantName,
    string? InvoiceNumber, string? Remark, string OcrStatus, string? OcrError,
    string? OcrText, JsonElement? OcrResult, bool ManuallyEdited, bool IsConfirmed)
{
    public static ReceiptResponse From(Receipt receipt) => new(receipt.Id, receipt.Version,
        receipt.FileItemId, receipt.FileItem.OriginalName, receipt.FileItem.Md5, receipt.FileItem.SizeBytes,
        receipt.SiteId, receipt.Site.Name, receipt.DocumentTypeDictId, receipt.DocumentTypeDict.Label,
        receipt.FileItem.Type, receipt.FileItem.TypeDict?.Label, receipt.SiteCategoryDictId, receipt.SiteCategoryDict?.Label,
        receipt.ReceiptDate, receipt.Amount, receipt.CurrencyCode, receipt.MerchantName,
        receipt.InvoiceNumber, receipt.Remark, receipt.OcrStatus, receipt.OcrError, receipt.OcrText,
        receipt.OcrResultJson == null ? null : JsonSerializer.Deserialize<JsonElement>(receipt.OcrResultJson), receipt.ManuallyEdited, receipt.IsConfirmed);
}