namespace Controleo.Application.Options;

public sealed class LlmProviderOptions
{
    public const string SectionName = "LlmProvider";

    public bool Enabled { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-nano";
    public int MaxTokens { get; set; } = 260;
    public decimal Temperature { get; set; } = 0.2m;
    public int RequestTimeoutSeconds { get; set; } = 20;
}
