using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Controleo.Api.Services.Auth;

public sealed class JwtAccessTokenValidator : IAccessTokenValidator
{
    private readonly LocalAuthOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtAccessTokenValidator(IOptions<LocalAuthOptions> options)
    {
        _options = options.Value;
    }

    public Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult((true, string.Empty, (ApiUserContext?)new ApiUserContext("anonymous-dev-user", null, "Dev User", "dev@controleo.local")));
        }

        if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult((false, "Missing bearer token.", (ApiUserContext?)null));
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult((false, "Bearer token is empty.", (ApiUserContext?)null));
        }

        try
        {
            var key = ResolveJwtSecret(_options.JwtSecret);

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

            var principal = _tokenHandler.ValidateToken(token, parameters, out _);
            var user = BuildUserContext(principal);
            if (string.IsNullOrWhiteSpace(user.UserId))
            {
                return Task.FromResult((false, "Token does not contain a stable user identifier.", (ApiUserContext?)null));
            }

            return Task.FromResult((true, string.Empty, (ApiUserContext?)user));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Invalid token: {ex.Message}", (ApiUserContext?)null));
        }
    }

    private static ApiUserContext BuildUserContext(ClaimsPrincipal principal)
    {
        var userId = GetClaimValue(principal, JwtRegisteredClaimNames.Sub)
                     ?? GetClaimValue(principal, ClaimTypes.NameIdentifier)
                     ?? string.Empty;

        var email = GetClaimValue(principal, JwtRegisteredClaimNames.Email)
                    ?? GetClaimValue(principal, JwtRegisteredClaimNames.UniqueName)
                    ?? GetClaimValue(principal, ClaimTypes.Email);

        var name = GetClaimValue(principal, "name")
                   ?? GetClaimValue(principal, ClaimTypes.Name);

        return new ApiUserContext(userId, userId, name, email);
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }

    private static string ResolveJwtSecret(string configuredSecret)
    {
        var secret = string.IsNullOrWhiteSpace(configuredSecret)
            ? (Environment.GetEnvironmentVariable("LOCAL_AUTH_JWT_SECRET") ?? string.Empty)
            : configuredSecret;

        secret = secret.Trim();
        if (secret.Length < 32)
        {
            throw new InvalidOperationException("Configura LocalAuth:JwtSecret (o LOCAL_AUTH_JWT_SECRET) con al menos 32 caracteres.");
        }

        return secret;
    }
}