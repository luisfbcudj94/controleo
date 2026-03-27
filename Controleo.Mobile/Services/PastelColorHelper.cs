using Controleo.Mobile.Models;

namespace Controleo.Mobile.Services;

/// <summary>
/// Maps movement types to colors/icons from user config or defaults.
/// Provides selectable palettes for the Settings UI.
/// </summary>
public static class PastelColorHelper
{
    /// <summary>20 distinct pastel colors for movement types.</summary>
    public static readonly (string Hex, string Label)[] AvailableColors =
    [
        ("#B6E6BD", "Verde menta"),
        ("#A8D8F0", "Azul cielo"),
        ("#F8C4B4", "Naranja suave"),
        ("#C5B3E6", "Púrpura"),
        ("#F4A9A8", "Rojo coral"),
        ("#F9E07F", "Amarillo sol"),
        ("#7ECEC1", "Turquesa"),
        ("#F0B5D0", "Rosa chicle"),
        ("#A0C4A8", "Verde salvia"),
        ("#B0D4F1", "Azul pastel"),
        ("#E6C88C", "Dorado suave"),
        ("#D4A5C9", "Magenta claro"),
        ("#8CC5B2", "Jade"),
        ("#F5D08A", "Ámbar"),
        ("#9DB8D4", "Azul acero"),
        ("#E8A89C", "Terracota"),
        ("#B3D9A3", "Lima suave"),
        ("#C9A0D4", "Lila"),
        ("#A8D4C8", "Menta fresca"),
        ("#F2C6A0", "Melocotón"),
    ];

    /// <summary>10 emoji icons the user can pick from.</summary>
    public static readonly (string Emoji, string Label)[] AvailableIcons =
    [
        ("🛒", "Compras"),
        ("🏠", "Hogar"),
        ("🎉", "Salidas"),
        ("⚡", "Imprevistos"),
        ("📱", "Suscripciones"),
        ("💳", "Deudas"),
        ("🏦", "Banco"),
        ("🧘", "Bienestar"),
        ("✈️", "Viajes"),
        ("🔨", "Obra"),
        ("🍕", "Comida"),
        ("🚗", "Transporte"),
    ];

    private static IReadOnlyList<MovementTypeConfig> _cachedConfigs = [];

    /// <summary>Call after loading catalogs to update the in-memory config.</summary>
    public static void SetConfigs(IReadOnlyList<MovementTypeConfig>? configs)
    {
        _cachedConfigs = configs ?? [];
    }

    /// <summary>Returns user-defined or fallback Color for a movement type.</summary>
    public static Color ForMovementType(string? movementType)
    {
        var hex = HexForMovementType(movementType);
        return Color.FromArgb(hex);
    }

    /// <summary>Returns user-defined or fallback hex color.</summary>
    public static string HexForMovementType(string? movementType)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return AvailableColors[0].Hex;
        }

        var cfg = FindConfig(movementType);
        if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.Color))
        {
            return cfg.Color;
        }

        // Deterministic fallback
        var hash = (uint)movementType.Trim().ToLowerInvariant().GetHashCode();
        return AvailableColors[hash % (uint)AvailableColors.Length].Hex;
    }

    /// <summary>Returns user-defined or fallback icon.</summary>
    public static string IconForMovementType(string? movementType)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return "📋";
        }

        var cfg = FindConfig(movementType);
        if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.Icon))
        {
            return cfg.Icon;
        }

        // Deterministic fallback from name
        var lower = movementType.Trim().ToLowerInvariant();
        return lower switch
        {
            "basicos para vivir" => "🛒",
            "hogar" => "🏠",
            "salidas" => "🎉",
            "imprevistos" => "⚡",
            "suscripciones" => "📱",
            "deudas" => "💳",
            "prestamo" => "🏦",
            "bienestar" => "🧘",
            "viajes" => "✈️",
            "sogamoso obra" => "🔨",
            _ => "📋"
        };
    }

    /// <summary>Returns colors not yet used by any movement type in the current config.</summary>
    public static List<(string Hex, string Label)> GetAvailableColors(IReadOnlyList<MovementTypeConfig>? currentConfigs, string? excludeName = null)
    {
        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in currentConfigs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(c.Color) &&
                (excludeName is null || !string.Equals(c.Name, excludeName, StringComparison.OrdinalIgnoreCase)))
            {
                usedColors.Add(c.Color.Trim());
            }
        }

        return AvailableColors.Where(ac => !usedColors.Contains(ac.Hex)).ToList();
    }

    private static MovementTypeConfig? FindConfig(string movementType)
    {
        return _cachedConfigs.FirstOrDefault(c =>
            string.Equals(c.Name, movementType.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
