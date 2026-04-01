using Controleo.Mobile.Models;

namespace Controleo.Mobile.Interfaces;

public interface ICatalogColorService
{
    (string Hex, string Label)[] AvailableColors { get; }
    (string Emoji, string Label)[] AvailableIcons { get; }
    void SetConfigs(IReadOnlyList<MovementTypeConfig>? configs);
    Color ForMovementType(string? movementType);
    string HexForMovementType(string? movementType);
    string IconForMovementType(string? movementType);
    List<(string Hex, string Label)> GetAvailableColors(IReadOnlyList<MovementTypeConfig>? currentConfigs, string? excludeName = null);
}
