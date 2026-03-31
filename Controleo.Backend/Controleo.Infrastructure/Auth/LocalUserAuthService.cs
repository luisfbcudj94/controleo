using System.Globalization; using System.IdentityModel.Tokens.Jwt; using System.Net; using System.Security.Claims; using System.Security.Cryptography; using System.Text; using Controleo.Domain.Interfaces; using Controleo.Infrastructure.Options; using Microsoft.Azure.Cosmos; using Microsoft.Extensions.Options; using Microsoft.IdentityModel.Tokens;
namespace Controleo.Infrastructure.Auth;
public sealed class LocalUserAuthService : IUserAuthService
{
    private const int Iterations = 210_000; private const int SaltSz = 16; private const int HashSz = 32;
    private readonly LocalAuthOptions _ao; private readonly Container _settings;
    public LocalUserAuthService(IOptions<CosmosStorageOptions> co, IOptions<LocalAuthOptions> ao)
    {
        _ao = ao.Value; var s = co.Value; var ep = Res(s.Endpoint, "COSMOS_DB_ENDPOINT"); var key = Res(s.Key, "COSMOS_DB_KEY");
        if (string.IsNullOrWhiteSpace(ep) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Cosmos not configured.");
        var client = new CosmosClient(ep, key, new CosmosClientOptions { SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase } });
        _settings = client.GetDatabase(s.DatabaseName).GetContainer(s.SettingsContainerName);
    }
    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> RegisterAsync(string name, string email, string password, CancellationToken ct)
    {
        var ne = (email ?? "").Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(ne)) return (false, "El correo es requerido.", null); if (string.IsNullOrWhiteSpace(name)) return (false, "El nombre es requerido.", null); if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 8) return (false, "La contraseña debe tener al menos 8 caracteres.", null);
        var docId = $"user-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ne))).ToLowerInvariant()}";
        if (await TryGet(docId, ct) is not null) return (false, "Este correo ya está registrado.", null);
        var now = DateTimeOffset.UtcNow; var salt = RandomNumberGenerator.GetBytes(SaltSz);
        var user = new UserDoc { Id = docId, UserId = Guid.NewGuid().ToString("N"), Type = "user-credential", Name = name.Trim(), Email = ne, PasswordSalt = Convert.ToBase64String(salt), PasswordHash = Convert.ToBase64String(Hash(password, salt, _ao.PasswordPepper)), HashVersion = "pbkdf2-sha256-v1", CreatedAt = now.ToString("O", CultureInfo.InvariantCulture), UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture), LastLoginAt = now.ToString("O", CultureInfo.InvariantCulture) };
        try { await _settings.CreateItemAsync(user, new PartitionKey(user.Id), cancellationToken: ct); } catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return (false, "Este correo ya está registrado.", null); }
        return (true, "", Session(user));
    }
    public async Task<(bool IsSuccess, string ErrorMessage, AuthSessionResponse? Session)> LoginAsync(string email, string password, CancellationToken ct)
    {
        var ne = (email ?? "").Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(ne) || string.IsNullOrWhiteSpace(password)) return (false, "Credenciales inválidas.", null);
        var docId = $"user-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ne))).ToLowerInvariant()}";
        var user = await TryGet(docId, ct); if (user is null) return (false, "Credenciales inválidas.", null);
        if (!Verify(password, user.PasswordSalt, user.PasswordHash, _ao.PasswordPepper)) return (false, "Credenciales inválidas.", null);
        user.LastLoginAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture); user.UpdatedAt = user.LastLoginAt;
        await _settings.UpsertItemAsync(user, new PartitionKey(user.Id), cancellationToken: ct);
        return (true, "", Session(user));
    }
    private AuthSessionResponse Session(UserDoc u)
    {
        var now = DateTimeOffset.UtcNow; var exp = now.AddMinutes(Math.Clamp(_ao.AccessTokenMinutes, 15, 30 * 24 * 60));
        var key = Secret(_ao.JwtSecret); var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, u.UserId), new Claim(JwtRegisteredClaimNames.Email, u.Email), new Claim(JwtRegisteredClaimNames.UniqueName, u.Email), new Claim("name", u.Name) };
        var token = new JwtSecurityToken(issuer: _ao.JwtIssuer, audience: _ao.JwtAudience, claims: claims, notBefore: now.UtcDateTime, expires: exp.UtcDateTime, signingCredentials: creds);
        return new AuthSessionResponse(new JwtSecurityTokenHandler().WriteToken(token), exp, new AuthUserProfile(u.UserId, u.Name, u.Email));
    }
    private async Task<UserDoc?> TryGet(string id, CancellationToken ct) { try { return (await _settings.ReadItemAsync<UserDoc>(id, new PartitionKey(id), cancellationToken: ct)).Resource; } catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; } }
    private static byte[] Hash(string pw, byte[] salt, string pepper) => Rfc2898DeriveBytes.Pbkdf2(string.Concat(pw, pepper ?? ""), salt, Iterations, HashAlgorithmName.SHA256, HashSz);
    private static bool Verify(string pw, string s, string h, string pepper) { try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(h), Hash(pw, Convert.FromBase64String(s), pepper)); } catch { return false; } }
    private static string Res(string v, params string[] envs) { if (!string.IsNullOrWhiteSpace(v) && !v.StartsWith("TU_")) return v.Trim(); foreach (var n in envs) { var e = Environment.GetEnvironmentVariable(n); if (!string.IsNullOrWhiteSpace(e)) return e.Trim(); } return ""; }
    private static string Secret(string v) { var s = (string.IsNullOrWhiteSpace(v) ? Environment.GetEnvironmentVariable("LOCAL_AUTH_JWT_SECRET") ?? "" : v).Trim(); if (s.Length < 32) throw new InvalidOperationException("JWT secret >= 32 chars."); return s; }
    private sealed class UserDoc { public string Id { get; set; } = ""; public string Type { get; set; } = ""; public string UserId { get; set; } = ""; public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string PasswordSalt { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string HashVersion { get; set; } = ""; public string CreatedAt { get; set; } = ""; public string UpdatedAt { get; set; } = ""; public string LastLoginAt { get; set; } = ""; }
}
