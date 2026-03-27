using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Controleo.Api.Services.Auth;

public sealed class EntraAccessTokenValidator : IAccessTokenValidator
{
    private readonly EntraAuthOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;

    public EntraAccessTokenValidator(IOptions<EntraAuthOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.MetadataAddress))
        {
            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                _options.MetadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    public async Task<(bool IsValid, string ErrorMessage, ApiUserContext? User)> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return (true, string.Empty, new ApiUserContext("anonymous-dev-user", null, "Dev User", "dev@controleo.local"));
        }

        if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Missing bearer token.", null);
        }

        if (_configurationManager is null)
        {
            return (false, "Auth metadata is not configured.", null);
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Bearer token is empty.", null);
        }

        try
        {
            var oidcConfig = await _configurationManager.GetConfigurationAsync(cancellationToken);
            var handler = new JwtSecurityTokenHandler();

            var tokenValidationParameters = new TokenValidationParameters
            {
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuer = true,
                ValidIssuers = ResolveValidIssuers(oidcConfig),
                ValidateAudience = _options.ValidAudiences is { Length: > 0 },
                ValidAudiences = _options.ValidAudiences,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(3)
            };

            var principal = handler.ValidateToken(token, tokenValidationParameters, out _);
            if (!HasRequiredScope(principal))
            {
                return (false, "Token does not contain required scope.", null);
            }

            var user = BuildUserContext(principal);
            if (string.IsNullOrWhiteSpace(user.UserId))
            {
                return (false, "Token does not contain a stable user identifier.", null);
            }

            return (true, string.Empty, user);
        }
        catch (Exception ex)
        {
            return (false, $"Invalid token: {ex.Message}", null);
        }
    }

    private bool HasRequiredScope(ClaimsPrincipal principal)
    {
        if (string.IsNullOrWhiteSpace(_options.RequiredScope))
        {
            return true;
        }

        var rawScope = GetClaimValue(principal, "scp") ?? string.Empty;
        var scopes = rawScope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return scopes.Any(scope => string.Equals(scope, _options.RequiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> ResolveValidIssuers(OpenIdConnectConfiguration oidcConfig)
    {
        if (_options.ValidIssuers is { Length: > 0 })
        {
            return _options.ValidIssuers;
        }

        return string.IsNullOrWhiteSpace(oidcConfig.Issuer)
            ? []
            : [oidcConfig.Issuer];
    }

    private static ApiUserContext BuildUserContext(ClaimsPrincipal principal)
    {
        var externalId =
            GetClaimValue(principal, "oid")
            ?? GetClaimValue(principal, "sub")
            ?? string.Empty;

        var email =
            GetClaimValue(principal, "preferred_username")
            ?? GetClaimValue(principal, "emails")
            ?? GetClaimValue(principal, ClaimTypes.Email);

        var name =
            GetClaimValue(principal, "name")
            ?? GetClaimValue(principal, ClaimTypes.Name);

        var userId = externalId;
        return new ApiUserContext(userId, externalId, name, email);
    }

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
    {
        return principal.FindFirst(claimType)?.Value;
    }
}
