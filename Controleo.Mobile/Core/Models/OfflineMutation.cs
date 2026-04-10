namespace Controleo.Mobile.Core.Models;

public sealed record OfflineMutation(
    long Id,
    string MutationType,
    string PayloadJson,
    string ScopeKey,
    int RetryCount
);
