using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Controleo.Api.Services.Auth;

public sealed class LocalUserAuthService : IUserAuthService
{
    private const int Pbkdf2Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly LocalAuthOptions _authOptions;
    private readonly Container _settingsContainer;

    public LocalUserAuthService(IOptions<CosmosStorageOptions> cosmosOptions, IOptions<LocalAuthOptions> authOptions)
    {
        var storage = cosmosOptions.Value;
        _authOptions = authOptions.Value;

        var endpoint = ResolveEndpoint(storage.Endpoint);
        var key = ResolveKey(storage.Key);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Cosmos DB no está configurado para autenticación local.");
        }

        var client = new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });

        var database = client.GetDatabase(storage.DatabaseName);
        _settingsContainer = database.GetContainer(storage.SettingsContainerName);
    }

    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(AuthRegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return (false, "El correo es requerido.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (false, "El nombre es requerido.", null);
        }

        if (!IsPasswordValid(request.Password))
        {
            return (false, "La contraseña debe tener al menos 8 caracteres.", null);
        }

        var userDocId = BuildUserDocId(normalizedEmail);
        var existing = await TryGetUserAsync(userDocId, cancellationToken);
        if (existing is not null)
        {
            return (false, "Este correo ya está registrado.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var passwordHash = HashPassword(request.Password, salt, _authOptions.PasswordPepper);
        var userId = Guid.NewGuid().ToString("N");

        var user = new UserCredentialDocument
        {
            Id = userDocId,
            UserId = userId,
            Type = "user-credential",
            Name = request.Name.Trim(),
            Email = normalizedEmail,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(passwordHash),
            HashVersion = "pbkdf2-sha256-v1",
            CreatedAt = now.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture),
            LastLoginAt = now.ToString("O", CultureInfo.InvariantCulture)
        };

        try
        {
            await _settingsContainer.CreateItemAsync(user, new PartitionKey(user.Id), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return (false, "Este correo ya está registrado.", null);
        }

        var session = CreateSession(user);
        return (true, string.Empty, session);
    }

    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(AuthLoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            return (false, "Credenciales inválidas.", null);
        }

        var user = await TryGetUserAsync(BuildUserDocId(normalizedEmail), cancellationToken);
        if (user is null)
        {
            return (false, "Credenciales inválidas.", null);
        }

        if (!VerifyPassword(request.Password, user.PasswordSalt, user.PasswordHash, _authOptions.PasswordPepper))
        {
            return (false, "Credenciales inválidas.", null);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        user.UpdatedAt = user.LastLoginAt;

        await _settingsContainer.UpsertItemAsync(user, new PartitionKey(user.Id), cancellationToken: cancellationToken);

        var session = CreateSession(user);
        return (true, string.Empty, session);
    }

    private AuthSessionResponse CreateSession(UserCredentialDocument user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(Math.Clamp(_authOptions.AccessTokenMinutes, 15, 30 * 24 * 60));

        var key = ResolveJwtSecret(_authOptions.JwtSecret);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.UniqueName, user.Email),
            new("name", user.Name)
        };

        var token = new JwtSecurityToken(
            issuer: _authOptions.JwtIssuer,
            audience: _authOptions.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthSessionResponse(
            tokenValue,
            expiresAt,
            new AuthUserProfile(user.UserId, user.Name, user.Email));
    }

    private async Task<UserCredentialDocument?> TryGetUserAsync(string userDocId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _settingsContainer.ReadItemAsync<UserCredentialDocument>(
                userDocId,
                new PartitionKey(userDocId),
                cancellationToken: cancellationToken);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool IsPasswordValid(string? password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Trim().Length >= 8;
    }

    private static byte[] HashPassword(string password, byte[] salt, string pepper)
    {
        var payload = string.Concat(password, pepper ?? string.Empty);
        return Rfc2898DeriveBytes.Pbkdf2(payload, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);
    }

    private static bool VerifyPassword(string password, string encodedSalt, string encodedHash, string pepper)
    {
        try
        {
            var salt = Convert.FromBase64String(encodedSalt);
            var expectedHash = Convert.FromBase64String(encodedHash);
            var computedHash = HashPassword(password, salt, pepper);
            return CryptographicOperations.FixedTimeEquals(expectedHash, computedHash);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildUserDocId(string normalizedEmail)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return $"user-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string ResolveEndpoint(string configuredEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(configuredEndpoint) && !configuredEndpoint.StartsWith("TU_", StringComparison.OrdinalIgnoreCase))
        {
            return configuredEndpoint.Trim();
        }

        return (Environment.GetEnvironmentVariable("COSMOS_DB_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
            ?? string.Empty).Trim();
    }

    private static string ResolveKey(string configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey) && !configuredKey.StartsWith("TU_", StringComparison.OrdinalIgnoreCase))
        {
            return configuredKey.Trim();
        }

        return (Environment.GetEnvironmentVariable("COSMOS_DB_KEY")
            ?? Environment.GetEnvironmentVariable("COSMOS_KEY")
            ?? string.Empty).Trim();
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

    private sealed class UserCredentialDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string HashVersion { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string LastLoginAt { get; set; } = string.Empty;
    }
}