using System.Globalization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;

namespace MC.Erp.Common.Infrastructure.Ocr;

/// <summary>
/// Azure Document Intelligence 包裝：收據 / 現沽單走 prebuilt-receipt，發票走 prebuilt-invoice。
/// 只取票據表需要的欄位，並保留 Azure 回報的置信度；低於閾值的欄位標記為需人工複核。
/// </summary>
public sealed class AzureDocumentOcrService(IOptions<OcrOptions> options, IHttpContextAccessor context) : IOcrService
{
    /// <summary>
    /// 關鍵欄位低於此置信度即標記需人工複核。手寫金額、褶皺反光的照片尤其容易偏低，
    /// 預建模型對手寫體識別率有限，金額不能只看「有值」就當正確。
    /// </summary>
    public const double ConfidenceThreshold = 0.80;

    private readonly OcrOptions settings = options.Value;
    private readonly Lazy<DocumentIntelligenceClient> client = new(() =>
    {
        var clientOptions = new DocumentIntelligenceClientOptions { Retry = { MaxRetries = 2 } };
        return new DocumentIntelligenceClient(new Uri(options.Value.Endpoint),
            new AzureKeyCredential(options.Value.Key), clientOptions);
    });

    public bool IsConfigured => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == "https" && !string.IsNullOrWhiteSpace(settings.Key);

    public async Task<OcrResult> RecognizeAsync(Stream content, string documentType)
    {
        using var timeout = TimeoutSource();
        var bytes = await BinaryData.FromStreamAsync(content, timeout.Token);
        return await AnalyzeAsync(bytes, documentType, timeout.Token);
    }

    public async Task<OcrResult> RecognizeAsync(byte[] content, string documentType)
    {
        using var timeout = TimeoutSource();
        return await AnalyzeAsync(BinaryData.FromBytes(content), documentType, timeout.Token);
    }

    /// <summary>CancellationToken 在基礎設施層處理：鏈接請求取消，再按超時兜底。</summary>
    private CancellationTokenSource TimeoutSource()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext?.RequestAborted ?? default);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 120)));
        return timeout;
    }

    private async Task<OcrResult> AnalyzeAsync(BinaryData bytes, string documentType, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Azure 識別服務尚未配置，請手動填寫或配置後重試");
        // 一次只分析一張單據，避免多頁 / 多圖時把第一張的欄位當成全部
        var request = new AnalyzeDocumentOptions(ModelFor(documentType), bytes);
        var operation = await client.Value.AnalyzeDocumentAsync(WaitUntil.Completed, request, cancellationToken);
        return ParseResult(operation.Value, documentType);
    }

    private static string ModelFor(string documentType) => documentType switch
    {
        "invoice" => "prebuilt-invoice",
        "receipt" => "prebuilt-receipt",
        _ => throw new InvalidOperationException("票據類型只支援 receipt / invoice")
    };

    public static OcrResult ParseResult(AnalyzeResult result, string documentType, double threshold = ConfidenceThreshold)
    {
        var document = result.Documents.Count == 1
            ? result.Documents[0]
            : throw new InvalidOperationException("請上傳只包含一張票據的圖片");
        var fields = document.Fields;
        var invoice = documentType == "invoice";
        var merchant = TextField(fields, invoice ? "VendorName" : "MerchantName");
        var date = DateField(fields, invoice ? "InvoiceDate" : "TransactionDate");
        var (total, currency) = MoneyField(fields, invoice ? "InvoiceTotal" : "Total");
        // 預建模型常把「合計 879」當普通文字，Total 只認到貨幣符號；金額缺失時用文字兜底
        if (total.Value is null)
        {
            var textTotal = TotalFromText(result.Content ?? "");
            if (textTotal.Value is not null) total = textTotal;
        }
        var invoiceNumber = TextField(fields, "InvoiceId");
        var review = new List<string>();
        // 金額與幣種同屬一個欄位，只標記金額，避免同一段低置信度重複提示
        Flag(review, "amount", total, threshold);
        Flag(review, "merchantName", merchant, threshold);
        Flag(review, "receiptDate", date, threshold);
        Flag(review, "invoiceNumber", invoiceNumber, threshold);
        return new OcrResult(merchant, date, total, currency, invoiceNumber,
            TextField(fields, "ReceiptType"), result.Content ?? "", review);
    }

    private static void Flag<T>(List<string> review, string name, OcrField<T> field, double threshold)
    {
        // 置信度為 null 代表 Azure 根本沒回這個欄位；有置信度但取不到值，代表識別到了卻讀不出內容
        if (field.Confidence is not { } confidence) return;
        if (field.Value is null || confidence < threshold) review.Add(name);
    }

    private static OcrField<string?> TextField(IReadOnlyDictionary<string, DocumentField> fields, string name)
    {
        if (!fields.TryGetValue(name, out var field) || field.FieldType != DocumentFieldType.String)
            return new OcrField<string?>(null, null);
        return new OcrField<string?>(field.ValueString, field.Confidence);
    }

    private static OcrField<DateOnly?> DateField(IReadOnlyDictionary<string, DocumentField> fields, string name)
    {
        if (!fields.TryGetValue(name, out var field) || field.FieldType != DocumentFieldType.Date)
            return new OcrField<DateOnly?>(null, null);
        return new OcrField<DateOnly?>(field.ValueDate is { } value ? DateOnly.FromDateTime(value.DateTime) : null,
            field.Confidence);
    }

    /// <summary>金額與幣種取自同一個欄位（收據 Total / 發票 InvoiceTotal），置信度共用。</summary>
    private static (OcrField<decimal?> Amount, OcrField<string?> Currency) MoneyField(
        IReadOnlyDictionary<string, DocumentField> fields, string name)
    {
        if (!fields.TryGetValue(name, out var field)) return (new OcrField<decimal?>(null, null), new OcrField<string?>(null, null));
        if (field.FieldType == DocumentFieldType.Currency)
        {
            // 常見情況：只識別到貨幣符號（content = "$"）卻沒有金額，值為 null 但置信度要保留，才能標記人工複核
            if (field.ValueCurrency is not { } currency)
                return (new OcrField<decimal?>(null, field.Confidence), new OcrField<string?>(null, field.Confidence));
            var amount = currency.Amount is >= 0 and <= 9999999999999999.99 ? decimal.Round((decimal)currency.Amount, 2) : (decimal?)null;
            var code = currency.CurrencyCode is { Length: 3 } text ? text.ToUpperInvariant() : null;
            return (new OcrField<decimal?>(amount, field.Confidence), new OcrField<string?>(code, field.Confidence));
        }
        if (field.FieldType == DocumentFieldType.Double)
        {
            var amount = field.ValueDouble is { } number && number is >= 0 and <= 9999999999999999.99
                ? decimal.Round((decimal)number, 2) : (decimal?)null;
            return (new OcrField<decimal?>(amount, field.Confidence), new OcrField<string?>(null, null));
        }
        return (new OcrField<decimal?>(null, null), new OcrField<string?>(null, null));
    }

    /// <summary>文字兜底的置信度：不是 Azure 的判斷，固定給低分，確保一定被標記人工複核。</summary>
    public const float TextFallbackConfidence = 0.5f;

    /// <summary>金額標籤；只認明確的總額字眼，避免把「金額」欄頭或單價當總額。</summary>
    private static readonly string[] TotalLabels =
        ["合計", "合 計", "總計", "總 計", "总计", "总 计", "TOTAL", "應付", "應 付", "应付", "應繳", "實收", "实收"];

    private static readonly Regex InlineAmount = new(
        @"[$¥€£]?\s*(\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)", RegexOptions.CultureInvariant);

    /// <summary>獨立成行的金額；要求整行只有數字（可帶貨幣符號、千分位），避免誤抓日期、電話、地址。</summary>
    private static readonly Regex AmountLine = new(
        @"^[$¥€£]?\s*(\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)$", RegexOptions.CultureInvariant);

    /// <summary>在識別文字裡找「合計 / TOTAL」後面的金額：同一行優先，其次下一行的純數字行。</summary>
    public static OcrField<decimal?> TotalFromText(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var label in TotalLabels)
            {
                var at = lines[index].IndexOf(label, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                if (Amount(InlineAmount.Match(lines[index][(at + label.Length)..])) is { } inline)
                    return new OcrField<decimal?>(inline, TextFallbackConfidence);
                if (index + 1 < lines.Length && Amount(AmountLine.Match(lines[index + 1])) is { } next)
                    return new OcrField<decimal?>(next, TextFallbackConfidence);
            }
        }
        return new OcrField<decimal?>(null, null);
    }

    private static decimal? Amount(Match match)
    {
        if (!match.Success) return null;
        var raw = match.Groups[1].Value.Replace(",", "");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            && amount is > 0 and <= 9999999999999999.99m ? amount : null;
    }
}
