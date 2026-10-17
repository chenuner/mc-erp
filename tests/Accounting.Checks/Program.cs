using System.Text.Json;
using System.ClientModel.Primitives;
using Azure.AI.DocumentIntelligence;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Features.Accounting.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

OcrResult Parse(string json, string type)
{
    var sdkResult = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(json))!;
    using var result = JsonDocument.Parse(ModelReaderWriter.Write(sdkResult).ToString());
    return AzureDocumentOcrService.ParseResult(result.RootElement, type);
}

var receipt = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"cement","documents":[{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{
"MerchantName":{"type":"string","valueString":"Builder"},
"TransactionDate":{"type":"date","valueDate":"2026-12-31"},
"Total":{"type":"currency","valueCurrency":{"amount":123.45,"currencyCode":"HKD"}},
"ReceiptType":{"type":"string","valueString":"Supplies"}}}]}
""", "receipt");
Check(receipt.Amount == 123.45m && receipt.CurrencyCode == "HKD", "Receipt currency mapping");
Check(receipt.Date == new DateOnly(2026, 12, 31), "Receipt date mapping");
Check(receipt.MerchantName == "Builder", "Merchant mapping");
Check(ReceiptService.SuggestCategory(receipt) == "materials", "Materials suggestion");
var invoice = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-invoice","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[{"docType":"invoice","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{
"VendorName":{"type":"string","valueString":"Supplier"},
"InvoiceDate":{"type":"date","valueDate":"2026-01-01"},
"InvoiceId":{"type":"string","valueString":"INV-1"},
"InvoiceTotal":{"type":"currency","valueCurrency":{"amount":110}},
"AmountDue":{"type":"currency","valueCurrency":{"amount":610}}}}]}
""", "invoice");
Check(invoice.Amount == 110m, "AmountDue must not replace InvoiceTotal");
Check(invoice.CurrencyCode == null, "Ambiguous currency stays null");
Check(invoice.InvoiceNumber == "INV-1" && invoice.MerchantName == "Supplier", "Invoice fields mapping");
var missing = Parse("""
{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","stringIndexType":"utf16CodeUnit","pages":[],"content":"","documents":[{"docType":"receipt","confidence":0.99,"spans":[],"boundingRegions":[],"fields":{}}]}
""", "receipt");
Check(missing.Amount == null && missing.Date == null, "Missing fields must not default to zero/today");
Check(ReceiptService.SuggestCategory(receipt with { Text = "cement rental", ReceiptType = null }) == null, "Conflicting categories stay pending");
Check(ReceiptService.SuggestCategory(receipt with { ReceiptType = "Transportation.Parking" }) == "transport", "Azure receipt type mapping");
var unsafeName = ReceiptService.SafeName("../../bad\\image:*.png");
Check(!unsafeName.Contains("..") && !unsafeName.Contains('/') && !unsafeName.Contains('\\'), "ZIP path sanitization");
Console.WriteLine($"PASS: {checks} accounting checks (no Azure calls)");
if (args.Contains("--integration")) await IntegrationChecks.RunAsync();