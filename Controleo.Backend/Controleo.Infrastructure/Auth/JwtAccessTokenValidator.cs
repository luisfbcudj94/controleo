using System.IdentityModel.Tokens.Jwt; using System.Security.Claims; using System.Text; using Controleo.Domain.Common; using Controleo.Domain.Interfaces; using Controleo.Infrastructure.Options; using Microsoft.Extensions.Options; using Microsoft.IdentityModel.Tokens;
namespace Controleo.Infrastructure.Auth;
public sealed class JwtAccessTokenValidator(IOptions<LocalAuthOptions> options) : IAccessTokenValidator
{
    private readonly LocalAuthOptions _o = options.Value;
    private readonly JwtSecurityTokenHandler _h = new();
    public Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? auth, CancellationToken ct)
    {
        if (!_o.Enabled) return Task.FromResult((true, "", (ApiUserContext?)new ApiUserContext("anonymous-dev-user", null, "Dev User", "dev@controleo.local")));
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ")) return Task.FromResult((false, "Missing bearer token.", (ApiUserContext?)null));
        var token = auth["Bearer ".Length..].Trim(); if (string.IsNullOrWhiteSpace(token)) return Task.FromResult((false, "Empty token.", (ApiUserContext?)null));
        try { var key = ResolveSecret(_o.JwtSecret); var tvp = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateIssuer = true, ValidIssuer = _o.JwtIssuer, ValidateAudience = true, ValidAudience = _o.JwtAudience, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(2) }; var principal = _h.ValidateToken(token, tvp, out _); var uid = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ""; var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value; var name = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value; if (string.IsNullOrWhiteSpace(uid)) return Task.FromResult((false, "No user id in token.", (ApiUserContext?)null)); return Task.FromResult((true, "", (ApiUserContext?)new ApiUserContext(uid, uid, name, email))); }
        catch (Exception ex) { return Task.FromResult((false, $"Invalid token: {ex.Message}", (ApiUserContext?)null)); }
    }
    private static string ResolveSecret(string c) { var s = (string.IsNullOrWhiteSpace(c) ? Environment.GetEnvironmentVariable("LOCAL_AUTH_JWT_SECRET") ?? "" : c).Trim(); if (s.Length < 32) throw new InvalidOperationException("JWT secret must be >= 32 chars."); return s; }
}
