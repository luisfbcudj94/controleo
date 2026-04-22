using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Controleo.Domain.Interfaces;
using Controleo.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Controleo.Infrastructure.Auth;

public sealed class LocalUserAuthService : IUserAuthService
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int ImpersonationMaxMinutes = 120;

    private readonly LocalAuthOptions _authOptions;
    private readonly Container _settings;

    public LocalUserAuthService(IOptions<CosmosStorageOptions> cosmosOptions, IOptions<LocalAuthOptions> authOptions)
    {
        _authOptions = authOptions.Value;

        var storage = cosmosOptions.Value;
        var endpoint = Resolve(storage.Endpoint, "COSMOS_DB_ENDPOINT");
        var key = Resolve(storage.Key, "COSMOS_DB_KEY");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Cosmos not configured.");

        var client = new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });

        _settings = client.GetDatabase(storage.DatabaseName).GetContainer(storage.SettingsContainerName);
    }

    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(string name, string email, string password, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return (false, "El correo es requerido.", null);

        if (string.IsNullOrWhiteSpace(name))
            return (false, "El nombre es requerido.", null);

        if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 8)
            return (false, "La contraseña debe tener al menos 8 caracteres.", null);

        var docId = BuildDocId(normalizedEmail);
        if (await TryGetByDocumentIdAsync(docId, ct) is not null)
            return (false, "Este correo ya está registrado.", null);

        var isSuperAdmin = IsSuperAdminEmail(normalizedEmail);
        var now = DateTimeOffset.UtcNow;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        var user = new UserDoc
        {
            Id = docId,
            UserId = Guid.NewGuid().ToString("N"),
            Type = "user-credential",
            Name = name.Trim(),
            Email = normalizedEmail,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(Hash(password, salt, _authOptions.PasswordPepper)),
            HashVersion = "pbkdf2-sha256-v1",
            IsPremium = isSuperAdmin,
            IsAdmin = isSuperAdmin,
            IsDisabled = false,
            CreatedAt = now.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture),
            LastLoginAt = now.ToString("O", CultureInfo.InvariantCulture)
        };

        try
        {
            await _settings.CreateItemAsync(user, new PartitionKey(user.Id), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return (false, "Este correo ya está registrado.", null);
        }

        return (true, string.Empty, BuildSession(user));
    }

    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(string email, string password, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Credenciales inválidas.", null);

        var docId = BuildDocId(normalizedEmail);
        var user = await TryGetByDocumentIdAsync(docId, ct);
        if (user is null)
            return (false, "Credenciales inválidas.", null);

        if (!Verify(password, user.PasswordSalt, user.PasswordHash, _authOptions.PasswordPepper))
            return (false, "Credenciales inválidas.", null);

        var isSuperAdmin = IsSuperAdminEmail(user.Email);
        if (isSuperAdmin)
        {
            user.IsAdmin = true;
            user.IsPremium = true;
            user.IsDisabled = false;
        }
        else if (user.IsDisabled)
        {
            return (false, "Cuenta inhabilitada por el administrador.", null);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        user.UpdatedAt = user.LastLoginAt;
        await _settings.UpsertItemAsync(user, new PartitionKey(user.Id), cancellationToken: ct);

        return (true, string.Empty, BuildSession(user));
    }

    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> ImpersonateAsync(string actorUserId, string targetUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(targetUserId))
            return (false, "Los usuarios actor y objetivo son requeridos.", null);

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
            return (false, "No puedes suplantar tu propia cuenta.", null);

        var actor = await GetByUserIdAsync(actorUserId.Trim(), ct);
        if (actor is null)
            return (false, "Administrador no encontrado.", null);

        var actorIsSuperAdmin = IsSuperAdminEmail(actor.Email);
        var actorIsAdmin = actorIsSuperAdmin || actor.IsAdmin;
        if (!actorIsAdmin || (!actorIsSuperAdmin && actor.IsDisabled))
            return (false, "Solo administradores activos pueden suplantar usuarios.", null);

        var target = await GetByUserIdAsync(targetUserId.Trim(), ct);
        if (target is null)
            return (false, "Usuario objetivo no encontrado.", null);

        if (IsSuperAdminEmail(target.Email))
            return (false, "No se puede suplantar al super-admin.", null);

        if (target.IsDisabled)
            return (false, "No se puede suplantar un usuario inhabilitado.", null);

        var context = new ImpersonationContext(actor.UserId, actor.Name, actor.Email);
        return (true, string.Empty, BuildSession(target, context));
    }

    private AuthSessionResponse BuildSession(UserDoc user, ImpersonationContext? impersonation = null)
    {
        var now = DateTimeOffset.UtcNow;
        var configuredMinutes = Math.Clamp(_authOptions.AccessTokenMinutes, 15, 30 * 24 * 60);
        var effectiveMinutes = impersonation is null
            ? configuredMinutes
            : Math.Min(configuredMinutes, ImpersonationMaxMinutes);
        var expiresAt = now.AddMinutes(effectiveMinutes);

        var key = ResolveSecret(_authOptions.JwtSecret);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var isAdmin = user.IsAdmin || IsSuperAdminEmail(user.Email);
        var isPremium = user.IsPremium || isAdmin;
        var isImpersonating = impersonation is not null;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.UniqueName, user.Email),
            new("name", user.Name),
            new("is_premium", isPremium ? "true" : "false"),
            new("is_admin", isAdmin ? "true" : "false"),
            new("is_impersonating", isImpersonating ? "true" : "false")
        };

        if (impersonation is not null)
        {
            claims.Add(new Claim("actor_user_id", impersonation.ActorUserId));
            claims.Add(new Claim("actor_name", impersonation.ActorName));
            claims.Add(new Claim("actor_email", impersonation.ActorEmail));
        }

        var token = new JwtSecurityToken(
            issuer: _authOptions.JwtIssuer,
            audience: _authOptions.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthSessionResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            new AuthUserProfile(
                user.UserId,
                user.Name,
                user.Email,
                isPremium,
                isAdmin,
                isImpersonating,
                impersonation?.ActorUserId,
                impersonation?.ActorName,
                impersonation?.ActorEmail,
                user.MonthlyIncome));
    }

    private async Task<UserDoc?> TryGetByDocumentIdAsync(string id, CancellationToken ct)
    {
        try
        {
            return (await _settings.ReadItemAsync<UserDoc>(id, new PartitionKey(id), cancellationToken: ct)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<UserDoc?> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        var query = new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.type = @type AND c.userId = @userId")
            .WithParameter("@type", "user-credential")
            .WithParameter("@userId", userId);

        var iterator = _settings.GetItemQueryIterator<UserDoc>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            var item = response.FirstOrDefault();
            if (item is not null)
                return item;
        }

        return null;
    }

    private bool IsSuperAdminEmail(string? email)
        => string.Equals(NormalizeEmail(email), NormalizeEmail(_authOptions.SuperAdminEmail), StringComparison.Ordinal);

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string BuildDocId(string normalizedEmail)
        => $"user-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail))).ToLowerInvariant()}";

    private static byte[] Hash(string password, byte[] salt, string pepper)
        => Rfc2898DeriveBytes.Pbkdf2(string.Concat(password, pepper ?? string.Empty), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

    private static bool Verify(string password, string salt, string hash, string pepper)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(hash),
                Hash(password, Convert.FromBase64String(salt), pepper));
        }
        catch
        {
            return false;
        }
    }

    private static string Resolve(string configured, params string[] envVars)
    {
        if (!string.IsNullOrWhiteSpace(configured) && !configured.StartsWith("TU_", StringComparison.Ordinal))
            return configured.Trim();

        foreach (var envVar in envVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string ResolveSecret(string configured)
    {
        var secret = (string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable("LOCAL_AUTH_JWT_SECRET") ?? string.Empty
            : configured).Trim();

        if (secret.Length < 32)
            throw new InvalidOperationException("JWT secret >= 32 chars.");

        return secret;
    }

    private sealed class UserDoc
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string HashVersion { get; set; } = string.Empty;
        public bool IsPremium { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsDisabled { get; set; }
        public decimal? MonthlyIncome { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
        public string LastLoginAt { get; set; } = string.Empty;
    }

    private sealed record ImpersonationContext(string ActorUserId, string ActorName, string ActorEmail);
}
