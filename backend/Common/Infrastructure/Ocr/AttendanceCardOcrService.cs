using Azure;
using Azure.AI.DocumentIntelligence;

namespace MC.Erp.Common.Infrastructure.Ocr;

/// <summary>考勤卡掃描：與票據識別共用同一個 client 和同一份憑據，只換模型。</summary>
public interface IAttendanceCardOcrService
{
    bool IsConfigured { get; }

    Task<AttendanceCard> ScanAsync(Stream content);

    Task<AttendanceCard> ScanAsync(byte[] content);

    /// <summary>由 Azure 直接拉取圖片（連結必須公網可訪問），本服務不下載。</summary>
    Task<AttendanceCard> ScanAsync(Uri url);
}

/// <summary>
/// 考勤卡識別：走 prebuilt-layout 取表格結構（不用 receipt / invoice 的欄位模型），
/// 再按行列還原逐日打卡。<see cref="DocumentIntelligenceClientProvider"/> 與發票共用同一個 client。
/// </summary>
public sealed class AzureAttendanceCardOcrService(DocumentIntelligenceClientProvider provider) : IAttendanceCardOcrService
{
    public const string Model = "prebuilt-layout";

    public bool IsConfigured => provider.IsConfigured;

    public async Task<AttendanceCard> ScanAsync(Stream content)
    {
        using var timeout = provider.TimeoutSource();
        var bytes = await BinaryData.FromStreamAsync(content, timeout.Token);
        return await AnalyzeAsync(new AnalyzeDocumentOptions(Model, bytes), timeout.Token);
    }

    public async Task<AttendanceCard> ScanAsync(byte[] content)
    {
        using var timeout = provider.TimeoutSource();
        return await AnalyzeAsync(new AnalyzeDocumentOptions(Model, BinaryData.FromBytes(content)), timeout.Token);
    }

    public async Task<AttendanceCard> ScanAsync(Uri url)
    {
        using var timeout = provider.TimeoutSource();
        return await AnalyzeAsync(new AnalyzeDocumentOptions(Model, url), timeout.Token);
    }

    /// <summary>
    /// 只認一張卡：多頁或多張時，表格會被混在一起，寧可報錯讓使用者重拍，
    /// 也不要把第二張卡的時間填到第一張的日期上。
    /// </summary>
    private async Task<AttendanceCard> AnalyzeAsync(AnalyzeDocumentOptions request, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Azure 識別服務尚未配置，請先配置 AZURE_DOC_ENDPOINT / AZURE_DOC_KEY");
        AnalyzeResult result;
        try
        {
            var operation = await provider.Client.AnalyzeDocumentAsync(WaitUntil.Completed, request, cancellationToken);
            result = operation.Value;
        }
        catch (RequestFailedException exception)
        {
            // 401 / 403 幾乎都是密鑰問題，直接講清楚，別讓使用者對著 500 猜
            throw new InvalidOperationException(exception.Status is 401 or 403
                ? "識別服務憑據無效或已過期，請檢查 AZURE_DOC_KEY"
                : "識別服務失敗，請稍後重試", exception);
        }
        if (result.Pages.Count > 1) throw new InvalidOperationException("請上傳只包含一張考勤卡的圖片");
        return AttendanceCardParser.Parse(result);
    }
}
