namespace MC.Erp.Common.Infrastructure.Ocr;

public interface IOcrService
{
    bool IsConfigured { get; }

    /// <summary>識別圖片；documentType 為票據類型字典值（receipt / invoice），決定用哪個預建模型。</summary>
    Task<OcrResult> RecognizeAsync(Stream content, string documentType);

    Task<OcrResult> RecognizeAsync(byte[] content, string documentType);
}

/// <summary>單一欄位：識別值 + Azure 回報的置信度（0–1）。未識別到時兩者皆為 null。</summary>
public sealed record OcrField<T>(T Value, float? Confidence);

/// <summary>
/// 只映射票據表實際會用到的欄位，不搬 Azure 的完整 schema（原始結果含 pages / words / polygon，不入庫也不回前端）。
/// ReviewFields 是置信度低於閾值、或識別到欄位卻讀不出值的欄位名（camelCase，與前端欄位同名）。
/// </summary>
public sealed record OcrResult(
    OcrField<string?> MerchantName,
    OcrField<DateOnly?> TransactionDate,
    OcrField<decimal?> Total,
    OcrField<string?> CurrencyCode,
    OcrField<string?> InvoiceNumber,
    OcrField<string?> ReceiptType,
    string Text,
    IReadOnlyList<string> ReviewFields)
{
    public bool NeedsReview => ReviewFields.Count > 0;
}

/// <summary>
/// 憑據只從環境變數 AZURE_DOC_ENDPOINT / AZURE_DOC_KEY 讀取（本機用 User Secrets 存同名鍵），
/// 代碼與倉庫都不出現真實密鑰。
/// </summary>
public sealed class OcrOptions
{
    public string Endpoint { get; set; } = "";
    public string Key { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}
