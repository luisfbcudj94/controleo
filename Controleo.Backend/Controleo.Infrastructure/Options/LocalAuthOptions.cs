namespace Controleo.Infrastructure.Options;
public sealed class LocalAuthOptions
{
    public const string SectionName = "LocalAuth";
    public bool Enabled { get; set; } = true;
    public bool AllowAnonymousInDevelopment { get; set; }
    public string JwtIssuer { get; set; } = "controleo-api";
    public string JwtAudience { get; set; } = "controleo-clients";
    public string JwtSecret { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 43_200;
    public string PasswordPepper { get; set; } = "";
}
