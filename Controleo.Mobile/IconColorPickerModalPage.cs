using Controleo.Mobile.Models;
using Controleo.Mobile.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Controleo.Mobile;

/// <summary>
/// Two-step grid picker: first icons, then colors.
/// Returns (icon, colorHex) or null if cancelled.
/// </summary>
public sealed class IconColorPickerModalPage : ContentPage
{
    private readonly TaskCompletionSource<(string Icon, string Color)?> _tcs = new();
    private readonly IReadOnlyList<(string Hex, string Label)> _availableColors;
    private readonly string? _preselectedIcon;
    private readonly string? _preselectedColor;
    private readonly bool _skipIconStep;

    private string? _pickedIcon;

    private readonly Label _titleLabel;
    private readonly Grid _gridContainer;
    private readonly Button _closeButton;

    private IconColorPickerModalPage(
        IReadOnlyList<(string Hex, string Label)> availableColors,
        string? preselectedIcon = null,
        string? preselectedColor = null,
        bool skipIconStep = false)
    {
        _availableColors = availableColors;
        _preselectedIcon = preselectedIcon;
        _preselectedColor = preselectedColor;
        _skipIconStep = skipIconStep;

        BackgroundColor = Color.FromArgb("#44000000");
        Shell.SetNavBarIsVisible(this, false);

        _titleLabel = new Label
        {
            Text = "Escoge un icono",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = GetColor("AppText", "#1A2E23"),
            VerticalOptions = LayoutOptions.Center
        };

        _closeButton = new Button
        {
            Text = "✕",
            WidthRequest = 36,
            HeightRequest = 36,
            CornerRadius = 18,
            FontSize = 14,
            Padding = 0,
            BackgroundColor = GetColor("Gray200", "#E4EAE6"),
            TextColor = GetColor("AppText", "#1A2E23")
        };
        _closeButton.Clicked += async (_, _) => await CloseAsync(null);

        _gridContainer = new Grid();

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { _titleLabel, _closeButton }
        };
        Grid.SetColumn(_closeButton, 1);

        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            BackgroundColor = GetColor("AppBg", "#F4F7F5"),
            Stroke = GetColor("AppBorder", "#DCE5DF"),
            Padding = 18,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { headerGrid, _gridContainer }
            }
        };

        Content = new Grid
        {
            Padding = 24,
            Children =
            {
                new Grid
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children = { card }
                }
            }
        };

        ShowIconGrid();
    }

    public static async Task<(string Icon, string Color)?> PickAsync(
        Page host,
        IReadOnlyList<(string Hex, string Label)> availableColors,
        string? preselectedIcon = null,
        string? preselectedColor = null)
    {
        var modal = new IconColorPickerModalPage(availableColors, preselectedIcon, preselectedColor);
        await host.Navigation.PushModalAsync(modal);
        return await modal._tcs.Task;
    }

    /// <summary>Skip icon step — go straight to color picker with a pre-set icon.</summary>
    public static async Task<string?> PickColorAsync(
        Page host,
        IReadOnlyList<(string Hex, string Label)> availableColors,
        string icon,
        string? preselectedColor = null)
    {
        var modal = new IconColorPickerModalPage(availableColors, icon, preselectedColor, skipIconStep: true);
        await host.Navigation.PushModalAsync(modal);
        var result = await modal._tcs.Task;
        return result?.Color;
    }

    private void ShowIconGrid()
    {
        // If skipping icon step, go directly to color
        if (_skipIconStep && !string.IsNullOrWhiteSpace(_preselectedIcon))
        {
            _pickedIcon = _preselectedIcon;
            ShowColorGrid();
            return;
        }

        _titleLabel.Text = "Escoge un icono";
        _gridContainer.Children.Clear();
        _gridContainer.RowDefinitions.Clear();
        _gridContainer.ColumnDefinitions.Clear();

        var icons = PastelColorHelper.AvailableIcons;
        var cols = 4;
        var rows = (int)Math.Ceiling(icons.Length / (double)cols);

        for (var c = 0; c < cols; c++)
            _gridContainer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < rows; r++)
            _gridContainer.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < icons.Length; i++)
        {
            var (emoji, _) = icons[i];
            var isSelected = string.Equals(emoji, _preselectedIcon);

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                BackgroundColor = isSelected
                    ? GetColor("AccentLight", "#D8F3DC")
                    : GetColor("AppSurface", "#FFFFFF"),
                Stroke = isSelected
                    ? GetColor("Tertiary", "#40916C")
                    : GetColor("AppBorder", "#DCE5DF"),
                StrokeThickness = isSelected ? 2 : 1,
                HeightRequest = 56,
                Margin = new Thickness(3),
                Padding = 0
            };

            var label = new Label
            {
                Text = emoji,
                FontSize = 28,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };

            border.Content = label;

            var capturedEmoji = emoji;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnIconPicked(capturedEmoji);
            border.GestureRecognizers.Add(tap);

            Grid.SetColumn(border, i % cols);
            Grid.SetRow(border, i / cols);
            _gridContainer.Children.Add(border);
        }
    }

    private void OnIconPicked(string emoji)
    {
        _pickedIcon = emoji;
        ShowColorGrid();
    }

    private void ShowColorGrid()
    {
        _titleLabel.Text = "Escoge un color";
        _gridContainer.Children.Clear();
        _gridContainer.RowDefinitions.Clear();
        _gridContainer.ColumnDefinitions.Clear();

        var colors = _availableColors;
        if (colors.Count == 0)
        {
            _gridContainer.Children.Add(new Label
            {
                Text = "No hay colores disponibles",
                TextColor = GetColor("AppHint", "#7A9183"),
                HorizontalOptions = LayoutOptions.Center
            });
            return;
        }

        var cols = 5;
        var rows = (int)Math.Ceiling(colors.Count / (double)cols);

        for (var c = 0; c < cols; c++)
            _gridContainer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < rows; r++)
            _gridContainer.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < colors.Count; i++)
        {
            var (hex, _) = colors[i];
            var isSelected = string.Equals(hex, _preselectedColor, StringComparison.OrdinalIgnoreCase);

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                BackgroundColor = Color.FromArgb(hex),
                Stroke = isSelected
                    ? GetColor("PrimaryDark", "#1B4332")
                    : Colors.Transparent,
                StrokeThickness = isSelected ? 3 : 0,
                HeightRequest = 56,
                WidthRequest = 56,
                Margin = new Thickness(3),
                HorizontalOptions = LayoutOptions.Center
            };

            // Show the picked icon inside the color swatch
            if (!string.IsNullOrWhiteSpace(_pickedIcon))
            {
                border.Content = new Label
                {
                    Text = _pickedIcon,
                    FontSize = 22,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                };
            }

            var capturedHex = hex;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await CloseAsync((_pickedIcon!, capturedHex));
            border.GestureRecognizers.Add(tap);

            Grid.SetColumn(border, i % cols);
            Grid.SetRow(border, i / cols);
            _gridContainer.Children.Add(border);
        }
    }

    private async Task CloseAsync((string Icon, string Color)? result)
    {
        if (!_tcs.Task.IsCompleted)
        {
            _tcs.TrySetResult(result);
        }

        await Navigation.PopModalAsync();
    }

    private static Color GetColor(string resourceKey, string fallbackHex)
    {
        if (Application.Current?.Resources?.TryGetValue(resourceKey, out var resource) == true && resource is Color color)
        {
            return color;
        }

        return Color.FromArgb(fallbackHex);
    }
}
