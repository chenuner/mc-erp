using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;

namespace MC.Erp.Common.Infrastructure.Ocr;

/// <summary>
/// 全站共用的 Document Intelligence 客戶端：票據與考勤卡共用同一個 client、同一份憑據與超時策略。
/// 憑據只從環境變數 AZURE_DOC_ENDPOINT / AZURE_DOC_KEY 讀取，代碼與倉庫都不出現真實密鑰。
/// </summary>
/// <remarks>
/// client 用 Lazy 建立：未配置憑據時應用仍可正常啟動，只有真正識別時才報錯。
/// </remarks>
public sealed class DocumentIntelligenceClientProvider(IOptions<OcrOptions> options, IHttpContextAccessor context)
{
    private readonly Lazy<DocumentIntelligenceClient> client = new(() =>
    {
        var clientOptions = new DocumentIntelligenceClientOptions { Retry = { MaxRetries = 2 } };
        return new DocumentIntelligenceClient(new Uri(options.Value.Endpoint),
            new AzureKeyCredential(options.Value.Key), clientOptions);
    });

    public DocumentIntelligenceClient Client => client.Value;

    public bool IsConfigured => Uri.TryCreate(options.Value.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == "https" && !string.IsNullOrWhiteSpace(options.Value.Key);

    /// <summary>CancellationToken 在基礎設施層處理：鏈接請求取消，再按超時兜底。</summary>
    public CancellationTokenSource TimeoutSource()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext?.RequestAborted ?? default);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 10, 120)));
        return timeout;
    }
}
