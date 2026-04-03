using System.Text.Json;
using System.Globalization;
using System.Text;
using Controleo.Mobile.Core.Interfaces;
using Microsoft.Maui.Storage;

namespace Controleo.Mobile.Core.Services;

public sealed class PaymentIconService : IPaymentIconService
{
    private const string PreferencesKeyPrefix = "controleo_payment_icons";

    public static readonly (string Emoji, string Label)[] StaticAvailableIcons =
    [
        ("💵", "Efectivo"),
        ("💳", "Tarjeta"),
        ("🖤", "Black card"),
        ("🧡", "Tarjeta naranja"),
        ("💜", "Tarjeta morada"),
        ("💚", "Tarjeta verde"),
        ("🏦", "Banco"),
        ("📲", "Billetera digital"),
        ("🔁", "Transferencia"),
        ("🪙", "Monedas"),
        ("🏧", "Cajero"),
        ("📟", "Dataphone"),
        ("🧾", "Codigo QR"),
        ("💻", "Pago web"),
        ("⌚", "NFC reloj"),
        ("📱", "NFC movil"),
        ("🎟️", "Voucher"),
        ("🛍️", "Credito tienda"),
        ("🏢", "Nomina"),
        ("🤝", "Prestado"),
    ];

    private static readonly HashSet<string> InvalidIcons = new(StringComparer.Ordinal)
    {
        string.Empty,
        "?",
        "??",
        "�",
    };

    private static readonly Dictionary<string, string> IconAliases = new(StringComparer.Ordinal)
    {
        ["🎟"] = "🎟️",
        ["🛍"] = "🛍️",
    };

    private static readonly HashSet<string> AllowedIcons = StaticAvailableIcons
        .Select(item => item.Emoji)
        .ToHashSet(StringComparer.Ordinal);

    (string Emoji, string Label)[] IPaymentIconService.AvailableIcons => StaticAvailableIcons;

    private readonly object _syncRoot = new();
    private readonly IAuthService _authService;
    private Dictionary<string, string>? _cache;
    private string? _cacheStorageKey;

    public PaymentIconService(IAuthService authService)
    {
        _authService = authService;
        _authService.SessionCleared += HandleSessionCleared;
    }

    public string IconForPaymentMethod(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return "💳";
        }

        var name = paymentMethod.Trim();
        var map = GetMap();
        if (map.TryGetValue(name, out var configured))
        {
            var sanitized = SanitizeIcon(configured);
            if (sanitized is not null)
            {
                return sanitized;
            }
        }

        var fallback = DefaultIconForPaymentMethod(name);
        map[name] = fallback;
        SaveMap(map);
        return fallback;
    }

    public void SetIconForPaymentMethod(string paymentMethod, string icon)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod) || string.IsNullOrWhiteSpace(icon))
        {
            return;
        }

        var name = paymentMethod.Trim();
        var resolvedIcon = SanitizeIcon(icon) ?? DefaultIconForPaymentMethod(name);
        var map = GetMap();
        map[name] = resolvedIcon;
        SaveMap(map);
    }

    public void RemovePaymentMethod(string paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return;
        }

        var map = GetMap();
        map.Remove(paymentMethod.Trim());
        SaveMap(map);
    }

    public void RenamePaymentMethod(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var map = GetMap();
        var oldKey = oldName.Trim();
        var newKey = newName.Trim();

        if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (map.TryGetValue(oldKey, out var icon))
        {
            map.Remove(oldKey);
            map[newKey] = SanitizeIcon(icon) ?? DefaultIconForPaymentMethod(newKey);
            SaveMap(map);
            return;
        }

        // Ensure renamed item still gets deterministic default.
        map[newKey] = DefaultIconForPaymentMethod(newKey);
        SaveMap(map);
    }

    private Dictionary<string, string> GetMap()
    {
        lock (_syncRoot)
        {
            var storageKey = ResolveStorageKey();
            if (_cache is not null && string.Equals(_cacheStorageKey, storageKey, StringComparison.Ordinal))
            {
                return _cache;
            }

            var raw = Preferences.Default.Get(storageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _cacheStorageKey = storageKey;
                return _cache;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                _cache = parsed is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                NormalizeMapIcons(_cache);
            }
            catch
            {
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            _cacheStorageKey = storageKey;

            return _cache;
        }
    }

    private void SaveMap(Dictionary<string, string> map)
    {
        lock (_syncRoot)
        {
            var storageKey = ResolveStorageKey();
            _cache = map;
            _cacheStorageKey = storageKey;
            var raw = JsonSerializer.Serialize(map);
            Preferences.Default.Set(storageKey, raw);
        }
    }

    private string ResolveStorageKey()
    {
        var userScope = NormalizeUserScope(_authService.CurrentUserId);
        return $"{PreferencesKeyPrefix}_{userScope}";
    }

    private static string NormalizeUserScope(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "anonymous";
        }

        var normalized = userId.Trim().ToLowerInvariant().Replace("/", "_");
        return string.IsNullOrWhiteSpace(normalized) ? "anonymous" : normalized;
    }

    private void HandleSessionCleared()
    {
        lock (_syncRoot)
        {
            _cache = null;
            _cacheStorageKey = null;
        }
    }

    private static string DefaultIconForPaymentMethod(string paymentMethod)
    {
        var normalized = NormalizeKey(paymentMethod);
        if (normalized == "efectivo") return "💵";
        if (normalized == "transferencia") return "🔁";
        if (normalized == "nequi") return "📲";
        if (normalized == "tc black") return "🖤";
        if (normalized == "tc rappi") return "🧡";
        if (normalized == "tc nu") return "💜";
        if (normalized == "td bancolombia" || normalized == "bancolombia") return "🏦";

        return StaticAvailableIcons[StableCatalogIndex(normalized, StaticAvailableIcons.Length)].Emoji;
    }

    private static int StableCatalogIndex(string value, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        uint hash = 2166136261;
        foreach (var ch in NormalizeKey(value))
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return (int)(hash % (uint)length);
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                sb.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            sb.Append(ch);
            previousWasWhitespace = false;
        }

        return sb.ToString().Trim();
    }

    private static void NormalizeMapIcons(Dictionary<string, string> map)
    {
        foreach (var key in map.Keys.ToList())
        {
            map[key] = SanitizeIcon(map[key]) ?? DefaultIconForPaymentMethod(key);
        }
    }

    private static string? SanitizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        var trimmed = icon.Trim();
        if (InvalidIcons.Contains(trimmed))
        {
            return null;
        }

        if (IconAliases.TryGetValue(trimmed, out var canonical))
        {
            trimmed = canonical;
        }

        return AllowedIcons.Contains(trimmed) ? trimmed : null;
    }
}
