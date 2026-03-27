namespace Controleo.Api.Options;

public sealed class EntraAuthOptions
{
    public const string SectionName = "EntraAuth";

    public bool Enabled { get; set; } = true;
    public bool AllowAnonymousInDevelopment { get; set; }
    public string MetadataAddress { get; set; } = string.Empty;
    public string[] ValidAudiences { get; set; } = [];
    public string[] ValidIssuers { get; set; } = [];
    public string RequiredScope { get; set; } = string.Empty;
}
