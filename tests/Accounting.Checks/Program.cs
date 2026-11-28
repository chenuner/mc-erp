using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.DocumentIntelligence;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Features.Accounting.Endpoints;
using MC.Erp.Features.Accounting.Entities;
using MC.Erp.Features.Accounting.Repositories;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Features.System.Entities;
using MC.Erp.Features.System.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

OcrResult Parse(string json, string type, double threshold = AzureDocumentOcrService.ConfidenceThreshold)
{
    var sdkResult = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(json))
        ?? throw new InvalidOperationException("Cannot read analyze result");
    return AzureDocumentOcrService.ParseResult(sdkResult, type, threshold);
}

var receipt = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"cement","documents":[{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{
"MerchantName":{"type":"string","valueString":"Builder","confidence":0.97},
"TransactionDate":{"type":"date","valueDate":"2026-12-31","confidence":0.95},
"Total":{"type":"currency","valueCurrency":{"amount":123.45,"currencyCode":"HKD"},"confidence":0.72},
"ReceiptType":{"type":"string","valueString":"Supplies","confidence":0.9}}}]}
""", "receipt");
Check(receipt.Total.Value == 123.45m && receipt.CurrencyCode.Value == "HKD", "Receipt currency mapping");
Check(receipt.TransactionDate.Value == new DateOnly(2026, 12, 31), "Receipt date mapping");
Check(receipt.MerchantName.Value == "Builder", "Merchant mapping");
Check(receipt.MerchantName.Confidence == 0.97f, "Merchant confidence carried through");
Check(receipt.ReviewFields.SequenceEqual(new[] { "amount" }), "Low-confidence amount flagged for review");
Check(receipt.NeedsReview, "Low confidence marks the result as needing review");
Check(receipt.ReviewFields.Contains("merchantName") == false, "High-confidence merchant not flagged");
Check(Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{
"Total":{"type":"currency","valueCurrency":{"amount":123.45,"currencyCode":"HKD"},"confidence":0.72}}}]}
""", "receipt", 0.5).NeedsReview == false, "Threshold is configurable per call");
Check(ReceiptService.SuggestCategory(receipt) == "materials", "Materials suggestion");
var invoice = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-invoice","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[{"docType":"invoice","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{
"VendorName":{"type":"string","valueString":"Supplier","confidence":0.95},
"InvoiceDate":{"type":"date","valueDate":"2026-01-01","confidence":0.95},
"InvoiceId":{"type":"string","valueString":"INV-1","confidence":0.95},
"InvoiceTotal":{"type":"currency","valueCurrency":{"amount":110},"confidence":0.95},
"AmountDue":{"type":"currency","valueCurrency":{"amount":610},"confidence":0.95}}}]}
""", "invoice");
Check(invoice.Total.Value == 110m, "AmountDue must not replace InvoiceTotal");
Check(invoice.CurrencyCode.Value == null, "Ambiguous currency stays null");
Check(invoice.InvoiceNumber.Value == "INV-1" && invoice.MerchantName.Value == "Supplier", "Invoice fields mapping");
Check(invoice.NeedsReview == false, "High-confidence invoice needs no review");
var missing = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{}}]}
""", "receipt");
Check(missing.Total.Value == null && missing.TransactionDate.Value == null, "Missing fields must not default to zero/today");
Check(missing.NeedsReview == false, "Missing fields are not low-confidence fields");
// 實測案例：收據「合 計 / 879」很清楚，Azure 的 Total 只回 content="$"、confidence 0.659，沒有 valueCurrency
OcrResult WithTotalField(string content) => Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"__CONTENT__","documents":[{"docType":"receipt","confidence":0.97,"spans":[],"boundingRegions":[],"fields":{
"Total":{"type":"currency","content":"$","confidence":0.659},
"TransactionDate":{"type":"date","valueDate":"2020-11-01","confidence":0.979}}}]}
""".Replace("__CONTENT__", content), "receipt");

var recovered = WithTotalField(@"合 計\n879");
Check(recovered.Total.Value == 879m, "Total recovered from 合計 line");
Check(recovered.Total.Confidence == AzureDocumentOcrService.TextFallbackConfidence, "Text fallback keeps low confidence");
Check(recovered.ReviewFields.SequenceEqual(new[] { "amount" }), "Text fallback still needs review");
var symbolOnly = WithTotalField("TOTAL $");
Check(symbolOnly.Total.Value == null, "Symbol-only total stays empty");
Check(symbolOnly.Total.Confidence == 0.659f, "Field confidence kept when value is missing");
Check(symbolOnly.NeedsReview, "Unreadable amount needs review");
Check(WithTotalField("TOTAL $ 1,234.50").Total.Value == 1234.50m, "Inline total with thousands separator");
Check(WithTotalField(@"TOTAL\n2026.11.21 23:56").Total.Value == null, "Dates are not taken as totals");
// 實測樣本（現沽單照片）的完整識別文字：金額要落在 879，不能抓到底下的相機水印
var realText = """
M.
現 沽 單
1
CASH MEMO
A
2020年11月1日
貨號
NO.
貨
DESCRIPTION
名
數量
單價
折扣
金 額
QUANTITY
UNIT PRICE
DISCOUNT
AMOUNT
古费
E FOOD IS GOOD
合 計
879
經手人
TOTAL $
SIGNATURE
xiaomi 17 Pro Max
Leica
23mm f/1.67 1/60s ISO160
2026.11.21 23:56
22°31'6"N 114°4'8"E
""";
Check(AzureDocumentOcrService.TotalFromText(realText).Value == 879m, "Real receipt text resolves the total");
var multiDocumentRejected = false;
try
{
    Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[
{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{}},
{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{}}]}
""", "receipt");
}
catch (InvalidOperationException) { multiDocumentRejected = true; }
Check(multiDocumentRejected, "Multiple documents rejected");
Check(ReceiptService.SuggestCategory(receipt with { Text = "cement rental", ReceiptType = new OcrField<string?>(null, null) }) == null, "Conflicting categories stay pending");
Check(ReceiptService.SuggestCategory(receipt with { ReceiptType = new OcrField<string?>("Transportation.Parking", null) }) == "transport", "Azure receipt type mapping");
// 回應只帶票據欄位：新資料存識別建議、可原樣回傳；舊的 Azure 原始 JSON 不回前端；字典名稱由前端按 id 映射
ReceiptDetail StoredDetail(string json) => new(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null,
    Guid.NewGuid(), Guid.NewGuid(), null,
    new DateOnly(2026, 11, 18), 1234.50m, "HKD", "陳記", "INV-1", "備註", "Succeeded", null,
    "整段原文", json);
var storedResponse = ReceiptResponse.From(StoredDetail(JsonSerializer.Serialize(new OcrSuggestion(
    new OcrField<string?>("陳記", 0.93f), new OcrField<DateOnly?>(new DateOnly(2026, 11, 18), 0.91f),
    new OcrField<decimal?>(1234.50m, 0.62f), new OcrField<string?>("HKD", 0.62f),
    new OcrField<string?>(null, null), null, new[] { "amount" }))));
Check(storedResponse.OcrResult?.Amount.Value == 1234.50m, "Stored suggestion round-trips");
Check(storedResponse.OcrResult?.ReviewFields.Contains("amount") == true, "Stored review fields survive");
// 列表回應的型別本身不含 OCR 原文和識別建議：序列化後連 key 都不該出現
var listedJson = JsonSerializer.Serialize(ReceiptListResponse.From(new ReceiptListItem(Guid.NewGuid(), Guid.NewGuid(),
    null, null, null, null, null, Guid.NewGuid(), Guid.NewGuid(), null,
    new DateOnly(2026, 11, 18), 1234.50m, "HKD", null, null, null, "Succeeded", null)),
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
Check(!listedJson.Contains("ocrText") && !listedJson.Contains("ocrResult"), "List response has no OCR fields");
Check(listedJson.Contains("\"ocrStatus\":\"Succeeded\"", StringComparison.Ordinal), "List still carries receipt fields");
Check(ReceiptResponse.From(StoredDetail("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","documents":[]}
""")).OcrResult == null, "Legacy Azure payload is not returned to the client");
var unsafeName = ReceiptService.SafeName("../../bad\\image:*.png");
Check(!unsafeName.Contains("..") && !unsafeName.Contains('/') && !unsafeName.Contains('\\'), "ZIP path sanitization");
// 列表篩選一律含下級：子樹按 ParentId 收集（含自己、含停用項）；髒資料成環也不能死循環
var subtreeRoot = new SysDict { Id = Guid.NewGuid(), Enabled = true };
var subtreeChild = new SysDict { Id = Guid.NewGuid(), ParentId = subtreeRoot.Id, Enabled = false };
var subtreeGrand = new SysDict { Id = Guid.NewGuid(), ParentId = subtreeChild.Id, Enabled = true };
var subtree = SysDictService.SubtreeIds(subtreeRoot.Id, [subtreeRoot, subtreeChild, subtreeGrand,
    new SysDict { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), Enabled = true }]);
Check(subtree.SequenceEqual([subtreeRoot.Id, subtreeChild.Id, subtreeGrand.Id]),
    "Subtree ids include self, children and disabled items");
subtreeRoot.ParentId = subtreeChild.Id; // 髒資料成環
Check(SysDictService.SubtreeIds(subtreeRoot.Id, [subtreeRoot, subtreeChild, subtreeGrand]).Count == 3,
    "Cyclic parent links cannot loop the subtree walk");
Console.WriteLine($"PASS: {checks} accounting checks (no Azure calls)");
if (args.Contains("--integration")) await IntegrationChecks.RunAsync();
