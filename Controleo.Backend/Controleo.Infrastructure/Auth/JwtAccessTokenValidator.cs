using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Controleo.Domain.Common;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Controleo.Infrastructure.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Controleo.Infrastructure.Auth;

public sealed class JwtAccessTokenValidator(IOptions<LocalAuthOptions> options, CosmosContainerProvider containerProvider) : IAccessTokenValidator
{
    private readonly LocalAuthOptions _options = options.Value;
    private readonly Container _settings = containerProvider.Settings;
    private readonly JwtSecurityTokenHandler _handler = new();

    public async Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? auth, CancellationToken ct)
    {
        if (!_options.Enabled)
            return (true, "", new ApiUserContext("anonymous-dev-user", null, "Dev User", "dev@controleo.local", false, true));

        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.Ordinal))
            return (false, "Missing bearer token.", null);

        var token = auth["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Empty token.", null);

        try
        {
            var key = ResolveSecret(_options.JwtSecret);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true,
                ValidIssuer = _options.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = _options.JwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _handler.ValidateToken(token, parameters, out _);

            var uid = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(uid))
                return (false, "No user id in token.", null);

            var tokenEmail = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                ?? principal.FindFirst(ClaimTypes.Email)?.Value;
            var tokenName = principal.FindFirst("name")?.Value
                ?? principal.FindFirst(ClaimTypes.Name)?.Value;

            var isImpersonatingRaw = principal.FindFirst("is_impersonating")?.Value;
            var isImpersonating = bool.TryParse(isImpersonatingRaw, out var parsedImpersonating) && parsedImpersonating;
            var actorUserId = principal.FindFirst("actor_user_id")?.Value;
            var actorName = principal.FindFirst("actor_name")?.Value;
            var actorEmail = principal.FindFirst("actor_email")?.Value;

            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                isImpersonating = false;
                actorName = null;
                actorEmail = null;
            }

            var userDoc = await LoadUserCredentialAsync(uid, ct);
            if (userDoc is null)
                return (false, "Cuenta no encontrada.", null);

            var isSuperAdmin = IsSuperAdminEmail(userDoc.Email);
            if (!isSuperAdmin && userDoc.IsDisabled)
                return (false, "Cuenta inhabilitada por el administrador.", null);

            var resolvedName = string.IsNullOrWhiteSpace(userDoc.Name) ? tokenName : userDoc.Name;
            var resolvedEmail = string.IsNullOrWhiteSpace(userDoc.Email) ? tokenEmail : userDoc.Email;
            var isAdmin = isSuperAdmin || userDoc.IsAdmin;
            var isPremium = isAdmin || userDoc.IsPremium;

            return (true, "", new ApiUserContext(uid, uid, resolvedName, resolvedEmail, isPremium, isAdmin, isImpersonating, actorUserId, actorName, actorEmail));
        }
        catch (Exception ex)
        {
            return (false, $"Invalid token: {ex.Message}", null);
        }
    }

    private async Task<UserCredentialProjection?> LoadUserCredentialAsync(string userId, CancellationToken ct)
    {
        var rows = await CosmosHelper.QueryAsync<UserCredentialProjection>(
            _settings,
            new QueryDefinition("SELECT TOP 1 c.name, c.email, c.isPremium, c.isAdmin, c.isDisabled FROM c WHERE c.type = @type AND c.userId = @userId")
                .WithParameter("@type", "user-credential")
                .WithParameter("@userId", userId),
            ct);

        return rows.FirstOrDefault();
    }

    private bool IsSuperAdminEmail(string? email)
        => string.Equals(NormalizeEmail(email), NormalizeEmail(_options.SuperAdminEmail), StringComparison.Ordinal);

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string ResolveSecret(string configured)
    {
        var secret = (string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable("LOCAL_AUTH_JWT_SECRET") ?? string.Empty
            : configured).Trim();

        if (secret.Length < 32)
            throw new InvalidOperationException("JWT secret must be >= 32 chars.");

        return secret;
    }

    private sealed class UserCredentialProjection
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public bool IsPremium { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsDisabled { get; set; }
    }
}
