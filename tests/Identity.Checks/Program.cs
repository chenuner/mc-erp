using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using MC.Erp.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var generator = new JwtTokenGenerator(Options.Create(new JwtOptions
{
    SecretKey = new string('x', 64),
    Issuer = "identity-checks",
    Audience = "identity-checks",
    ExpiryMinutes = 5
}));
var userId = Guid.NewGuid();
var handler = new JwtSecurityTokenHandler();
var checks = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var token = handler.ReadJwtToken(generator.Generate(
    userId, "admin", ["Admin"], ["identity.view"], "\u9673\u5927\u6587"));
Check(token.Subject == userId.ToString(), "Subject must remain the user ID");
Check(token.Claims.Single(c => c.Type == ClaimTypes.Name).Value == "admin",
    "Name claim must remain the login username");
Check(token.Claims.Single(c => c.Type == "display_name").Value == "\u9673\u5927\u6587",
    "Display name must be preserved as UTF-8");
Check(token.Claims.Single(c => c.Type == ClaimTypes.Role).Value == "Admin",
    "Role claims must be unchanged");
Check(token.Claims.Single(c => c.Type == "permission").Value == "identity.view",
    "Permission claims must be unchanged");

foreach (var displayName in new string?[] { null, "", " " })
{
    var withoutName = handler.ReadJwtToken(generator.Generate(userId, "admin", [], [], displayName));
    Check(!withoutName.Claims.Any(c => c.Type == "display_name"),
        "Missing display names must not produce an empty claim");
}

// 登錄限流：同一個 IP 一分鐘 20 次，第 21 次拒絕；不同 IP 各有各的桶
// （不分桶的話一個人打錯 20 次，全公司都登不進來）
var services = new ServiceCollection();
services.AddOptions();
services.AddLoginRateLimit();
using var provider = services.BuildServiceProvider();
var rateLimiterOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
Check(rateLimiterOptions.RejectionStatusCode == StatusCodes.Status429TooManyRequests,
    "限流拒絕時狀態碼應為 429");
Check(rateLimiterOptions.OnRejected is not null, "拒絕時應回 ProblemDetails");

using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(LoginRateLimit.Partition);

static HttpContext FromIp(string ip)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
    return context;
}

var client = FromIp("203.0.113.7");
var accepted = new List<RateLimitLease>();
for (var i = 0; i < LoginRateLimit.PermitLimit; i++)
{
    var lease = await limiter.AcquireAsync(client);
    Check(lease.IsAcquired, $"第 {i + 1} 次登錄應放行");
    accepted.Add(lease);
}

using (var blocked = await limiter.AcquireAsync(client))
{
    Check(!blocked.IsAcquired, "超過 PermitLimit 後應拒絕");
    Check(blocked.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero,
        "拒絕時應帶 Retry-After");
}

using (var otherClient = await limiter.AcquireAsync(FromIp("198.51.100.9")))
{
    Check(otherClient.IsAcquired, "不同 IP 不應共用同一個桶");
}

foreach (var lease in accepted) lease.Dispose();

Console.WriteLine($"PASS: {checks} identity checks");
