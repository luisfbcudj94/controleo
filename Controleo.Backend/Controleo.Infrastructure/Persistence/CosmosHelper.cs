using System.Globalization;
using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
internal static class CosmosHelper
{
    public static async Task<List<T>> QueryAsync<T>(Container container, QueryDefinition query, CancellationToken ct)
    {
        var it = container.GetItemQueryIterator<T>(query); var r = new List<T>();
        while (it.HasMoreResults) { var resp = await it.ReadNextAsync(ct); r.AddRange(resp); }
        return r;
    }
    public static string[] NormalizeCatalog(IEnumerable<string>? s) => (s ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool IsAllowedValue(string v, IEnumerable<string> a) => a.Any(x => string.Equals(x.Trim(), v.Trim(), StringComparison.OrdinalIgnoreCase));
    public static DateTimeOffset ParseDateTimeOffset(string? raw, DateTimeOffset fb) => string.IsNullOrWhiteSpace(raw) ? fb : DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) ? p.ToUniversalTime() : fb;
    public static bool IsValidMonthKey(string? mk) => !string.IsNullOrWhiteSpace(mk) && DateOnly.TryParseExact($"{mk}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
