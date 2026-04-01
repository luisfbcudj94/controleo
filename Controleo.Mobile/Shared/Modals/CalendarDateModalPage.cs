using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Shared.Modals;

public sealed class CalendarDateModalPage : ContentPage
{
    private readonly TaskCompletionSource<DateOnly?> _completionSource = new();
    private readonly DateOnly _minDate;
    private readonly DateOnly _maxDate;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("es-CO");

    private readonly Label _monthLabel;
    private readonly Grid _daysGrid;
    private readonly Button _prevMonthButton;
    private readonly Button _nextMonthButton;

    private DateOnly _selectedDate;
    private DateOnly _displayMonth;

    private CalendarDateModalPage(string title, DateOnly current, DateOnly? minDate, DateOnly? maxDate)
    {
        _minDate = minDate ?? DateOnly.MinValue;
        _maxDate = maxDate ?? DateOnly.MaxValue;

        if (_minDate > _maxDate)
        {
            (_minDate, _maxDate) = (_maxDate, _minDate);
        }

        _selectedDate = Clamp(current);
        _displayMonth = new DateOnly(_selectedDate.Year, _selectedDate.Month, 1);

        BackgroundColor = Color.FromArgb("#44000000");
        Shell.SetNavBarIsVisible(this, false);

        _monthLabel = new Label
        {
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = GetColor("AppText", "#1A2E23")
        };

        _prevMonthButton = new Button
        {
            Text = "◀",
            WidthRequest = 42,
            HeightRequest = 42,
            CornerRadius = 21,
            BackgroundColor = GetColor("AppSurface2", "#EFF3F1"),
            TextColor = GetColor("AppText", "#1A2E23")
        };
        _prevMonthButton.Clicked += (_, _) =>
        {
            _displayMonth = _displayMonth.AddMonths(-1);
            RenderCalendar();
        };

        _nextMonthButton = new Button
        {
            Text = "▶",
            WidthRequest = 42,
            HeightRequest = 42,
            CornerRadius = 21,
            BackgroundColor = GetColor("AppSurface2", "#F5F5F5"),
            TextColor = GetColor("AppText", "#1A1A1A")
        };
        _nextMonthButton.Clicked += (_, _) =>
        {
            _displayMonth = _displayMonth.AddMonths(1);
            RenderCalendar();
        };

        var closeButton = new Button
        {
            Text = "✕",
            WidthRequest = 40,
            HeightRequest = 40,
            CornerRadius = 20,
            BackgroundColor = GetColor("Gray200", "#E4EAE6"),
            TextColor = GetColor("AppText", "#1A2E23")
        };
        closeButton.Clicked += async (_, _) => await CloseAsync(null);

        _daysGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = GetColor("AppText", "#1A2E23"),
                    VerticalOptions = LayoutOptions.Center
                },
                closeButton
            }
        };
        Grid.SetColumn(closeButton, 1);

        var monthNavGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _prevMonthButton,
                _monthLabel,
                _nextMonthButton
            }
        };
        Grid.SetColumn(_monthLabel, 1);
        Grid.SetColumn(_nextMonthButton, 2);

        var contentGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 10,
            Children =
            {
                headerGrid,
                monthNavGrid,
                _daysGrid
            }
        };
        Grid.SetRow(monthNavGrid, 1);
        Grid.SetRow(_daysGrid, 2);

        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            BackgroundColor = GetColor("AppSurface", "#FFFFFF"),
            Stroke = GetColor("AppBorder", "#DCE5DF"),
            Padding = 14,
            Content = contentGrid
        };

        Content = new Grid
        {
            Padding = 20,
            Children =
            {
                new Grid
                {
                    VerticalOptions = LayoutOptions.Center,
                    Children = { card }
                }
            }
        };

        RenderCalendar();
    }

    public static async Task<DateOnly?> PickAsync(Page host, string title, DateOnly current, DateOnly? minDate = null, DateOnly? maxDate = null)
    {
        var modal = new CalendarDateModalPage(title, current, minDate, maxDate);
        await host.Navigation.PushModalAsync(modal);
        return await modal._completionSource.Task;
    }

    private void RenderCalendar()
    {
        var monthStart = new DateOnly(_displayMonth.Year, _displayMonth.Month, 1);
        var minMonth = new DateOnly(_minDate.Year, _minDate.Month, 1);
        var maxMonth = new DateOnly(_maxDate.Year, _maxDate.Month, 1);

        _prevMonthButton.IsEnabled = monthStart.AddMonths(-1) >= minMonth;
        _nextMonthButton.IsEnabled = monthStart.AddMonths(1) <= maxMonth;
        _monthLabel.Text = _displayMonth.ToString("MMMM yyyy", _culture);

        _daysGrid.Children.Clear();
        _daysGrid.RowDefinitions.Clear();
        _daysGrid.ColumnDefinitions.Clear();

        for (var col = 0; col < 7; col++)
        {
            _daysGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        _daysGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var row = 0; row < 6; row++)
        {
            _daysGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var dayHeaders = new[] { "L", "M", "X", "J", "V", "S", "D" };
        for (var col = 0; col < dayHeaders.Length; col++)
        {
            var header = new Label
            {
                Text = dayHeaders[col],
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = GetColor("AppHint", "#7A9183")
            };
            Grid.SetColumn(header, col);
            Grid.SetRow(header, 0);
            _daysGrid.Children.Add(header);
        }

        var firstDay = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        var firstColumn = ((int)firstDay.DayOfWeek + 6) % 7;
        var visibleStart = DateOnly.FromDateTime(firstDay.AddDays(-firstColumn));

        for (var index = 0; index < 42; index++)
        {
            var date = visibleStart.AddDays(index);
            var isCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year;
            var isEnabled = date >= _minDate && date <= _maxDate && isCurrentMonth;
            var isSelected = date == _selectedDate;

            var dayButton = new Button
            {
                Text = date.Day.ToString("00", CultureInfo.InvariantCulture),
                FontSize = 13,
                Padding = new Thickness(0),
                HeightRequest = 38,
                CornerRadius = 10,
                BackgroundColor = isSelected
                    ? GetColor("PrimaryDark", "#1B4332")
                    : Colors.Transparent,
                TextColor = isSelected
                    ? GetColor("PrimaryDarkText", "#FFFFFF")
                    : isCurrentMonth
                        ? GetColor("AppText", "#1A2E23")
                        : GetColor("AppHint", "#7A9183"),
                Opacity = isEnabled ? 1 : 0.38,
                IsEnabled = isEnabled
            };

            if (isEnabled)
            {
                dayButton.Clicked += async (_, _) => await CloseAsync(date);
            }

            Grid.SetColumn(dayButton, index % 7);
            Grid.SetRow(dayButton, 1 + (index / 7));
            _daysGrid.Children.Add(dayButton);
        }
    }

    private DateOnly Clamp(DateOnly value)
    {
        if (value < _minDate)
        {
            return _minDate;
        }

        if (value > _maxDate)
        {
            return _maxDate;
        }

        return value;
    }

    private async Task CloseAsync(DateOnly? value)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(value);
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