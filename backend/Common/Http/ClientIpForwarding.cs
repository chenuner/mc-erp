using Microsoft.AspNetCore.HttpOverrides;

namespace MC.Erp.Common.Http;

/// <summary>
/// 把 <c>HttpContext.Connection.RemoteIpAddress</c> 還原成真實客戶端 IP，而不是上一跳代理的地址。
/// <para>
/// 生產鏈路是「公網 → 宿主 nginx → web 容器 nginx → backend」兩跳，兩層 nginx 都用
/// <c>proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for</c>（追加，不是覆寫），
/// 所以 backend 收到的 X-Forwarded-For 最後兩項固定是「真實客戶端, 宿主機 127.0.0.1」。
/// </para>
/// <para>
/// 可以放心信任這個頭：backend 的端口不對外發布（docker-compose.prod.yml 只有 web 映射到
/// 127.0.0.1:8080），客戶端能碰到的只有宿主 nginx，而它把客戶端自己塞的 X-Forwarded-For
/// 追加在左邊；只取最後兩項永遠取不到偽造值。
/// </para>
/// <para>
/// 登錄限流按 IP 分桶依賴它：不做這一步，所有請求看起來都來自 127.0.0.1，
/// 限流會退化成「全站共用一個桶」——一個人打錯 20 次，全公司陪著等一分鐘。
/// </para>
/// </summary>
public static class ClientIpForwarding
{
    /// <summary>兩層 nginx，各追加一項。</summary>
    private const int ProxyHops = 2;

    public static IApplicationBuilder UseForwardedClientIp(this IApplicationBuilder app)
    {
        var options = new ForwardedHeadersOptions { ForwardLimit = ProxyHops };
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        // 默認只信 loopback，但容器網絡裡上一跳是 web 容器的 172.x 地址。
        // 放開已知代理限制後的可信度由「backend 不發布端口 + 兩層 nginx 都只追加」保證，見類別說明。
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        return app.UseForwardedHeaders(options);
    }
}
