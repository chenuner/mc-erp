using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MC.Erp.Common.Security;

/// <summary>
/// 登錄端點限流：同一個客戶端 IP 每分鐘最多 <see cref="PermitLimit"/> 次，超出直接 429。
/// <para>
/// 為什麼要限：每次登錄校驗都是 Argon2id（<c>MemoryCost=65536</c> 即 64MB、<c>TimeCost=3</c>、<c>Lanes=4</c>），
/// 單次約 150–300ms CPU；沒有任何上限時十幾個併發就能把 2 核小機器打到 100%。
/// 這個端點也是唯一不需要登錄狀態就能觸發這個計算的地方。
/// </para>
/// <para>
/// 為什麼按 IP 而不是按用戶名：用戶名在請求體裡，限流中介軟體在參數綁定之前就要做決定；
/// 而且「換用戶名繼續試」正是要擋的暴力破解方式，按用戶名分桶反而繞得過。
/// </para>
/// </summary>
public static class LoginRateLimit
{
    /// <summary>策略名稱，端點上用 <c>RequireRateLimiting</c> 引用。</summary>
    public const string Policy = "login";

    /// <summary>
    /// 同一個 IP 每個窗口允許的登錄次數。要容得下辦公室共用一個出口 IP 時早上陸續登錄，
    /// 同時把單一 IP 的 CPU 消耗壓在一個核的 5% 以內（20 × 250ms / 60s ≈ 8%）。
    /// </summary>
    public const int PermitLimit = 20;

    /// <summary>窗口長度。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 固定窗口計數：實現最簡單、內存開銷最小，代價只是窗口交界處可能有短時雙倍。
    /// 目的只是保護 CPU，不需要精確整形，所以不引入滑動窗口或令牌桶。
    /// </summary>
    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Policy, Partition);

            // 拒絕時回 ProblemDetails（前端 request.ts 會把 detail 直接彈出來），並帶 Retry-After
            options.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await TypedResults.Problem(
                        $"同一個網絡在 {Window.TotalMinutes:0} 分鐘內最多登錄 {PermitLimit} 次，請稍後再試。",
                        statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }

    /// <summary>
    /// 分桶規則：按客戶端 IP 各給一個固定窗口計數器。同時給 DI 註冊和 Identity.Checks 驗證使用。
    /// </summary>
    public static RateLimitPartition<string> Partition(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                // 不排隊：排隊只是把拒絕延後，還會一直佔著連線
                QueueLimit = 0,
                AutoReplenishment = true
            });

    /// <summary>
    /// 分桶鍵：真實客戶端 IP（由 <c>ClientIpForwarding</c> 從 X-Forwarded-For 還原）。
    /// 取不到來源地址時全部歸到同一個桶，寧可嚴格也不放行。
    /// </summary>
    private static string ClientKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
