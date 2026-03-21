namespace Controleo.Api.Options;

public sealed class FirebaseStorageOptions
{
    public const string SectionName = "FirebaseStorage";

    public string ProjectId { get; set; } = string.Empty;
    public string CredentialsFilePath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "expenses";

    public string[] MovementTypes { get; set; } = [];

    public string[] PaymentMethods { get; set; } = [];
}
