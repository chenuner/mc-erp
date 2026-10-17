using System.Text.Json;
using System.ClientModel.Primitives;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;

namespace MC.Erp.Common.Infrastructure.Ocr;

public sealed class AzureDocumentOcrService(IOptions<OcrOptions> options, IHttpContextAccessor context) : IOcrService
{
    private readonly OcrOptions settings = options.Value;
    private readonly Lazy<DocumentIntelligenceClient> client = new(() =>
    {
        var clientOptions = new DocumentIntelligenceClientOptions();
        clientOptions.Retry.MaxRetries = 2;
        return new DocumentIntelligenceClient(new Uri(options.Value.Endpoint),
            new AzureKeyCredential(options.Value.Key), clientOptions);
    });

    public bool IsConfigured => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == "https" && !string.IsNullOrWhiteSpace(settings.Key);

    public async Task<OcrResult> RecognizeAsync(Stream content, string documentType)
    {
        if (!IsConfigured) throw new InvalidOperationException("Azure 識別服務尚未配置，請手動填寫或配置後重試");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext?.RequestAborted ?? default);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 120)));
        var bytes = await BinaryData.FromStreamAsync(content, timeout.Token);
        var request = new AnalyzeDocumentOptions(documentType == "invoice" ? "prebuilt-invoice" : "prebuilt-receipt", bytes);
        var operation = await client.Value.AnalyzeDocumentAsync(WaitUntil.Completed, request, timeout.Token);
        var result = operation.Value;
        if (result.Documents.Count != 1) throw new InvalidOperationException("請上傳只包含一張票據的圖片");
        var json = ModelReaderWriter.Write(result).ToString();
        using var parsed = JsonDocument.Parse(json);
        return ParseResult(parsed.RootElement, documentType);
    }

    public static OcrResult ParseResult(JsonElement result, string documentType)
    {
        var document = result.GetProperty("documents")[0];
        var fields = document.GetProperty("fields");
        JsonElement Field(string name) => fields.TryGetProperty(name, out var field) ? field : default;
        string? TextValue(JsonElement field, string key) => field.ValueKind == JsonValueKind.Object
            && field.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var amountField = Field(documentType == "invoice" ? "InvoiceTotal" : "Total");
        decimal? amount = null;
        string? currency = null;
        if (amountField.ValueKind == JsonValueKind.Object)
        {
            if (amountField.TryGetProperty("valueCurrency", out var money))
            {
                if (money.TryGetProperty("amount", out var number) && number.TryGetDecimal(out var value)) amount = value;
                currency = TextValue(money, "currencyCode");
            }
            else if (amountField.TryGetProperty("valueNumber", out var number) && number.TryGetDecimal(out var value)) amount = value;
        }
        var dateValue = TextValue(Field(documentType == "invoice" ? "InvoiceDate" : "TransactionDate"), "valueDate");
        return new OcrResult(TextValue(Field(documentType == "invoice" ? "VendorName" : "MerchantName"), "valueString"),
            DateOnly.TryParse(dateValue, System.Globalization.CultureInfo.InvariantCulture, out var date) ? date : null,
            amount is >= 0 and <= 9999999999999999.99m ? decimal.Round(amount.Value, 2) : null,
            currency, TextValue(Field("InvoiceId"), "valueString"), TextValue(Field("ReceiptType"), "valueString"),
            TextValue(result, "content") ?? "", result.GetRawText());
    }
}