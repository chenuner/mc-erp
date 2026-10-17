using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MC.Erp.Common.Security;
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

Console.WriteLine($"PASS: {checks} identity checks");
