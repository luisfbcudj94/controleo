using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Microsoft.Maui.Controls.Shapes;

namespace Controleo.Mobile.Features.Obligations;

internal enum ObligationDayActionKind
{
    Edit,
    Delete
}

internal sealed record ObligationDayActionResult(ObligationDayActionKind Action, ObligationItem Item);

internal sealed class ObligationDayDetailsModalPage : ContentPage
{
    private static readonly CultureInfo EsCulture = new("es-CO");

    private readonly TaskCompletionSource<ObligationDayActionResult?> _completionSource = new();

    private ObligationDayDetailsModalPage(
        DateOnly date,
        IReadOnlyList<ObligationItem> obligations,
        IPaymentIconService paymentIconService,
        ICatalogColorService colorService)
    {
        BackgroundColor = Color.FromArgb("#44000000");
        Shell.SetNavBarIsVisible(this, false);

        var titleText = EsCulture.TextInfo.ToTitleCase(date.ToString("dddd d 'de' MMMM", EsCulture));
        var subtitleText = obligations.Count == 1
            ? "1 obligación programada"
            : $"{obligations.Count} obligaciones programadas";

        var itemsContainer = new VerticalStackLayout
        {
            Spacing = 10
        };

        foreach (var obligation in obligations
                     .OrderBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.MonthlyPayment))
        {
            itemsContainer.Children.Add(CreateObligationCard(obligation, paymentIconService, colorService));
        }

        var closeButton = new Button
        {
            Text = "Listo",
            HeightRequest = 42,
            CornerRadius = 12,
            BackgroundColor = Color.FromArgb("#EFF3F1"),
            TextColor = Color.FromArgb("#1A2E23")
        };
        closeButton.Clicked += async (_, _) => await CloseWithResultAsync(null);

        var content = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Stroke = new SolidColorBrush(Color.FromArgb("#DCE5DF")),
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            Padding = new Thickness(16),
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                },
                RowSpacing = 10,
                Children =
                {
                    new Grid
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
                                Text = titleText,
                                FontSize = 18,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#1A2E23")
                            },
                            new Button
                            {
                                Text = "✕",
                                WidthRequest = 34,
                                HeightRequest = 34,
                                CornerRadius = 17,
                                FontSize = 14,
                                BackgroundColor = Color.FromArgb("#E4EAE6"),
                                TextColor = Color.FromArgb("#1A2E23"),
                                Command = new Command(async () => await CloseWithResultAsync(null))
                            }.WithGridColumn(1)
                        }
                    },
                    new Label
                    {
                        Text = subtitleText,
                        FontSize = 12,
                        TextColor = Color.FromArgb("#7A9183")
                    }.WithGridRow(1),
                    new ScrollView
                    {
                        Content = itemsContainer
                    }.WithGridRow(2),
                    closeButton.WithGridRow(3)
                }
            }
        };

        Content = new Grid
        {
            Padding = new Thickness(20),
            Children =
            {
                content
            }
        };
    }

    public Task<ObligationDayActionResult?> Result => _completionSource.Task;

    public static async Task<ObligationDayActionResult?> ShowAsync(
        Page host,
        DateOnly date,
        IReadOnlyList<ObligationItem> obligations,
        IPaymentIconService paymentIconService,
        ICatalogColorService colorService)
    {
        var modal = new ObligationDayDetailsModalPage(date, obligations, paymentIconService, colorService);
        await host.Navigation.PushModalAsync(modal);
        return await modal.Result;
    }

    private Border CreateObligationCard(
        ObligationItem obligation,
        IPaymentIconService paymentIconService,
        ICatalogColorService colorService)
    {
        var editButton = new Button
        {
            Text = "Editar",
            BackgroundColor = Color.FromArgb("#D8F3DC"),
            TextColor = Color.FromArgb("#1B4332"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 36,
            Padding = new Thickness(10, 0)
        };
        editButton.Clicked += async (_, _) => await CloseWithResultAsync(new ObligationDayActionResult(ObligationDayActionKind.Edit, obligation));

        var deleteButton = new Button
        {
            Text = "Eliminar",
            BackgroundColor = Color.FromArgb("#FDEBE9"),
            TextColor = Color.FromArgb("#8E1D13"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 36,
            Padding = new Thickness(10, 0)
        };
        deleteButton.Clicked += async (_, _) => await CloseWithResultAsync(new ObligationDayActionResult(ObligationDayActionKind.Delete, obligation));

        var iconColor = colorService.ForMovementType(obligation.MovementType);
        var paymentIcon = paymentIconService.IconForPaymentMethod(obligation.PaymentMethod);

        return new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Stroke = new SolidColorBrush(Color.FromArgb("#DCE5DF")),
            BackgroundColor = Color.FromArgb("#F8FAF8"),
            Padding = new Thickness(12),
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                RowSpacing = 6,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Auto),
                            new ColumnDefinition(GridLength.Star)
                        },
                        ColumnSpacing = 8,
                        Children =
                        {
                            new Border
                            {
                                WidthRequest = 34,
                                HeightRequest = 34,
                                StrokeThickness = 0,
                                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                                BackgroundColor = iconColor,
                                Content = new Label
                                {
                                    Text = "🔔",
                                    FontSize = 16,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    VerticalTextAlignment = TextAlignment.Center,
                                    HorizontalOptions = LayoutOptions.Center,
                                    VerticalOptions = LayoutOptions.Center
                                }
                            },
                            new VerticalStackLayout
                            {
                                Spacing = 1,
                                Children =
                                {
                                    new Label
                                    {
                                        Text = obligation.Description,
                                        FontSize = 14,
                                        FontAttributes = FontAttributes.Bold,
                                        TextColor = Color.FromArgb("#1A2E23")
                                    },
                                    new Label
                                    {
                                        Text = $"{paymentIcon} {obligation.PaymentMethod}",
                                        FontSize = 11,
                                        TextColor = Color.FromArgb("#7A9183")
                                    }
                                }
                            }.WithGridColumn(1)
                        }
                    },
                    new Label
                    {
                        Text = $"Cuota mensual: {obligation.MonthlyPayment:C0}",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#1B4332")
                    }.WithGridRow(1),
                    new Label
                    {
                        Text = $"Recordatorio: {obligation.ReminderDaysBefore} día(s) antes",
                        FontSize = 11,
                        TextColor = Color.FromArgb("#7A9183")
                    }.WithGridRow(2),
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        HorizontalOptions = LayoutOptions.End,
                        Children =
                        {
                            editButton,
                            deleteButton
                        }
                    }.WithGridRow(3)
                }
            }
        };
    }

    private async Task CloseWithResultAsync(ObligationDayActionResult? result)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(result);
        }

        await Navigation.PopModalAsync();
    }
}

internal static class LayoutExtensions
{
    public static View WithGridColumn(this View view, int column)
    {
        Grid.SetColumn(view, column);
        return view;
    }

    public static View WithGridRow(this View view, int row)
    {
        Grid.SetRow(view, row);
        return view;
    }
}