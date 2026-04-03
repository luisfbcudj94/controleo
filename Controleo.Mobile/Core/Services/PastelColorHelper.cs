using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using System.Globalization;
using System.Text;

namespace Controleo.Mobile.Core.Services;

/// <summary>
/// Maps movement types to colors/icons from user config or defaults.
/// Provides selectable palettes for the Settings UI.
/// </summary>
public sealed class PastelColorHelper : ICatalogColorService
{
    /// <summary>20 distinct pastel colors for movement types.</summary>
    public static readonly (string Hex, string Label)[] StaticAvailableColors =
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

    /// <summary>20 emoji icons the user can pick from.</summary>
    public static readonly (string Emoji, string Label)[] StaticAvailableIcons =
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
        ("🏥", "Salud"),
        ("🎓", "Educacion"),
        ("🎬", "Entretenimiento"),
        ("🐾", "Mascotas"),
        ("👕", "Ropa"),
        ("🛠️", "Mantenimiento"),
        ("🎁", "Regalos"),
        ("📚", "Libros"),
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
        ["✈"] = "✈️",
        ["🛠"] = "🛠️",
        ["🎟"] = "🎟️",
        ["🛍"] = "🛍️",
    };

    private static readonly HashSet<string> AllowedIcons = StaticAvailableIcons
        .Select(item => item.Emoji)
        .ToHashSet(StringComparer.Ordinal);

    (string Hex, string Label)[] ICatalogColorService.AvailableColors => StaticAvailableColors;
    (string Emoji, string Label)[] ICatalogColorService.AvailableIcons => StaticAvailableIcons;

    private IReadOnlyList<MovementTypeConfig> _cachedConfigs = [];

    public PastelColorHelper(IAuthService authService)
    {
        authService.SessionCleared += HandleSessionCleared;
    }

    /// <summary>Call after loading catalogs to update the in-memory config.</summary>
    public void SetConfigs(IReadOnlyList<MovementTypeConfig>? configs)
    {
        _cachedConfigs = configs ?? [];
    }

    /// <summary>Returns user-defined or fallback Color for a movement type.</summary>
    public Color ForMovementType(string? movementType)
    {
        var hex = HexForMovementType(movementType);
        return Color.FromArgb(hex);
    }

    /// <summary>Returns user-defined or fallback hex color.</summary>
    public string HexForMovementType(string? movementType)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return StaticAvailableColors[0].Hex;
        }

        var cfg = FindConfig(movementType);
        if (cfg is not null && !string.IsNullOrWhiteSpace(cfg.Color))
        {
            return cfg.Color;
        }

        return StaticAvailableColors[StableCatalogIndex(movementType, StaticAvailableColors.Length)].Hex;
    }

    /// <summary>Returns user-defined or fallback icon.</summary>
    public string IconForMovementType(string? movementType)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return "📋";
        }

        var cfg = FindConfig(movementType);
        var configuredIcon = SanitizeIcon(cfg?.Icon);
        if (configuredIcon is not null)
        {
            return configuredIcon;
        }

        var normalized = NormalizeKey(movementType);
        if (normalized.Contains("basico", StringComparison.Ordinal)) return "🛒";
        if (normalized.Contains("hogar", StringComparison.Ordinal)) return "🏠";
        if (normalized.Contains("salida", StringComparison.Ordinal)) return "🎉";
        if (normalized.Contains("imprevisto", StringComparison.Ordinal)) return "⚡";
        if (normalized.Contains("suscrip", StringComparison.Ordinal)) return "📱";
        if (normalized.Contains("deuda", StringComparison.Ordinal)) return "💳";
        if (normalized.Contains("prestamo", StringComparison.Ordinal) || normalized.Contains("banco", StringComparison.Ordinal)) return "🏦";
        if (normalized.Contains("bienestar", StringComparison.Ordinal)) return "🧘";
        if (normalized.Contains("viaje", StringComparison.Ordinal)) return "✈️";
        if (normalized.Contains("obra", StringComparison.Ordinal)) return "🔨";
        if (normalized.Contains("comida", StringComparison.Ordinal)) return "🍕";

        return StaticAvailableIcons[StableCatalogIndex(movementType, StaticAvailableIcons.Length)].Emoji;
    }

    /// <summary>Returns colors not yet used by any movement type in the current config.</summary>
    public List<(string Hex, string Label)> GetAvailableColors(IReadOnlyList<MovementTypeConfig>? currentConfigs, string? excludeName = null)
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

        return StaticAvailableColors.Where(ac => !usedColors.Contains(ac.Hex)).ToList();
    }

    private MovementTypeConfig? FindConfig(string movementType)
    {
        return _cachedConfigs.FirstOrDefault(c =>
            string.Equals(c.Name, movementType.Trim(), StringComparison.OrdinalIgnoreCase));
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

    private void HandleSessionCleared()
    {
        _cachedConfigs = [];
    }
}
