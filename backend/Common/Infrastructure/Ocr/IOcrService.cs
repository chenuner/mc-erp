namespace MC.Erp.Common.Infrastructure.Ocr;

public interface IOcrService
{
    bool IsConfigured { get; }
    Task<OcrResult> RecognizeAsync(Stream content, string documentType);
}

public sealed record OcrResult(string? MerchantName, DateOnly? Date, decimal? Amount,
    string? CurrencyCode, string? InvoiceNumber, string? ReceiptType, string Text, string Json);

public sealed class OcrOptions
{
    public string Endpoint { get; set; } = "";
    public string Key { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}