using System.Text.Json;
using Controleo.Mobile.Interfaces;
using Microsoft.Maui.Storage;

namespace Controleo.Mobile.Services;

public sealed class PaymentIconService : IPaymentIconService
{
    private const string PreferencesKey = "controleo_payment_icons";

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

    (string Emoji, string Label)[] IPaymentIconService.AvailableIcons => StaticAvailableIcons;

    private static readonly object SyncRoot = new();
    private static Dictionary<string, string>? _cache;

    public string IconForPaymentMethod(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return "💳";
        }

        var name = paymentMethod.Trim();
        var map = GetMap();
        if (map.TryGetValue(name, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
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

        var map = GetMap();
        map[paymentMethod.Trim()] = icon.Trim();
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

        if (map.TryGetValue(oldKey, out var icon) && !string.IsNullOrWhiteSpace(icon))
        {
            map.Remove(oldKey);
            map[newKey] = icon;
            SaveMap(map);
            return;
        }

        // Ensure renamed item still gets deterministic default.
        map[newKey] = DefaultIconForPaymentMethod(newKey);
        SaveMap(map);
    }

    private static Dictionary<string, string> GetMap()
    {
        lock (SyncRoot)
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var raw = Preferences.Default.Get(PreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return _cache;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                _cache = parsed is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return _cache;
        }
    }

    private static void SaveMap(Dictionary<string, string> map)
    {
        lock (SyncRoot)
        {
            _cache = map;
            var raw = JsonSerializer.Serialize(map);
            Preferences.Default.Set(PreferencesKey, raw);
        }
    }

    private static string DefaultIconForPaymentMethod(string paymentMethod)
    {
        var normalized = paymentMethod.Trim().ToLowerInvariant();
        return normalized switch
        {
            "efectivo" => "💵",
            "transferencia" => "🔁",
            "nequi" => "📲",
            "tc black" => "🖤",
            "tc rappi" => "🧡",
            "tc nu" => "💜",
            "td bancolombia" => "🏦",
            "bancolombia" => "🏦",
            _ => StaticAvailableIcons[(int)((uint)normalized.GetHashCode() % (uint)StaticAvailableIcons.Length)].Emoji,
        };
    }
}
