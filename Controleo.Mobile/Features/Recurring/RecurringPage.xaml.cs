using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using System.Globalization;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Features.Recurring;

public partial class RecurringPage : ContentPage
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20];

    private readonly IExpenseApiClient _apiClient;
    private readonly ICatalogColorService _colorService;
    private readonly IPaymentIconService _paymentIconService;
    private string? _editingId;
    private DateOnly _selectedStartDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? _selectedMovementType;
    private string? _selectedPaymentMethod;
    private bool _isPageSizeSyncing;
    private int _pageNumber = 1;
    private int _pageSize = 5;

    public RecurringPage(IExpenseApiClient apiClient, ICatalogColorService colorService, IPaymentIconService paymentIconService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _colorService = colorService;
        _paymentIconService = paymentIconService;
        UpdateStartDateSelectorLabel();
        PageSizePicker.ItemsSource = AllowedPageSizes.Select(item => item.ToString()).ToList();
        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = _pageSize.ToString();
        _isPageSizeSyncing = false;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
        PaginationStatusLabel.Text = "Página 1/1 · 0 registros";
        PrevPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;
        MoneyFormatHelper.Attach(AmountEntry);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        SetLoading(true);
        try
        {
            await LoadCatalogsAsync();
            await LoadRecurringPageAsync();
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task LoadCatalogsAsync()
    {
        var catalogs = await _apiClient.GetCatalogsAsync(CancellationToken.None);
        _colorService.SetConfigs(catalogs.MovementTypeConfigs);

        foreach (var paymentMethod in catalogs.PaymentMethods)
        {
            var configuredIcon = (catalogs.PaymentMethodConfigs ?? [])
                .FirstOrDefault(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase))
                ?.Icon;

            if (!string.IsNullOrWhiteSpace(configuredIcon))
            {
                _paymentIconService.SetIconForPaymentMethod(paymentMethod, configuredIcon);
                continue;
            }

            _paymentIconService.IconForPaymentMethod(paymentMethod);
        }

        BuildTypePickerGrid(catalogs.MovementTypes.ToList());
        BuildPayPickerGrid(catalogs.PaymentMethods.ToList());
    }

    private void BuildTypePickerGrid(List<string> types)
    {
        TypePickerFlexLayout.Children.Clear();
        var tileWidth = (DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density - 48 - 36) / 3;
        foreach (var t in types)
        {
            var bgColor = _colorService.ForMovementType(t);
            var icon = _colorService.IconForMovementType(t);
            var tile = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                StrokeThickness = 0,
                BackgroundColor = bgColor,
                Padding = new Thickness(8, 14),
                WidthRequest = tileWidth,
                Margin = new Thickness(0, 0, 6, 6),
                Content = new VerticalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = icon, FontSize = 28, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = t, FontSize = 11, FontAttributes = FontAttributes.Bold,
                                    HorizontalOptions = LayoutOptions.Center,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    TextColor = Color.FromArgb("#1A2E23") }
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            var captured = t;
            var capturedIcon = icon;
            tap.Tapped += (s, e) =>
            {
                _selectedMovementType = captured;
                SelectedTypeLabel.Text = capturedIcon + " " + captured;
                TypePickerOverlay.IsVisible = false;
            };
            tile.GestureRecognizers.Add(tap);
            TypePickerFlexLayout.Children.Add(tile);
        }
    }

    private void BuildPayPickerGrid(List<string> methods)
    {
        PayPickerFlexLayout.Children.Clear();
        var tileWidth = (DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density - 48 - 36) / 3;
        var payColors = new Dictionary<string, (string bg, string fg)>(StringComparer.OrdinalIgnoreCase)
        {
            ["TC Black"]       = ("#1A2E23", "#FFFFFF"),
            ["TC Rappi"]       = ("#FF6B35", "#FFFFFF"),
            ["TD Bancolombia"] = ("#FFD700", "#1A2E23"),
            ["Bancolombia"]    = ("#FFD700", "#1A2E23"),
            ["TC Nu"]          = ("#7B2D8E", "#FFFFFF"),
            ["Efectivo"]       = ("#B6E6BD", "#1A2E23"),
            ["Transferencia"]  = ("#A8D8F0", "#1A2E23"),
            ["Nequi"]          = ("#00C389", "#FFFFFF"),
        };
        foreach (var m in methods)
        {
            var (bg, fg) = payColors.TryGetValue(m, out var c) ? c : ("#A8D8F0", "#1A2E23");
            var emoji = _paymentIconService.IconForPaymentMethod(m);
            var tile = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb(bg),
                Padding = new Thickness(8, 14),
                WidthRequest = tileWidth,
                Margin = new Thickness(0, 0, 6, 6),
                Content = new VerticalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = emoji, FontSize = 28, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = m, FontSize = 11, FontAttributes = FontAttributes.Bold,
                                    HorizontalOptions = LayoutOptions.Center,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    TextColor = Color.FromArgb(fg) }
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            var captured = m;
            var capturedEmoji = emoji;
            tap.Tapped += (s, e) =>
            {
                _selectedPaymentMethod = captured;
                SelectedPayLabel.Text = capturedEmoji + " " + captured;
                PayPickerOverlay.IsVisible = false;
            };
            tile.GestureRecognizers.Add(tap);
            PayPickerFlexLayout.Children.Add(tile);
        }
    }

    private void OnTypePickerTapped(object? sender, TappedEventArgs e) => TypePickerOverlay.IsVisible = true;
    private void OnPayPickerTapped(object? sender, TappedEventArgs e) => PayPickerOverlay.IsVisible = true;
    private void OnCloseTypePicker(object? sender, EventArgs e) => TypePickerOverlay.IsVisible = false;
    private void OnClosePayPicker(object? sender, EventArgs e) => PayPickerOverlay.IsVisible = false;

    private async Task LoadRecurringPageAsync()
    {
        var page = await _apiClient.GetRecurringExpensesPageAsync(_pageNumber, _pageSize, CancellationToken.None);
        _pageNumber = page.PageNumber;

        var viewItems = page.Items.Select(item => new RecurringViewItem(
            item,
            _colorService.ForMovementType(item.MovementType),
            _colorService.IconForMovementType(item.MovementType)
        )).ToList();

        RecurringCollection.ItemsSource = viewItems;
        PaginationStatusLabel.Text = $"Página {page.PageNumber}/{page.TotalPages} · {page.TotalCount} registros";
        PrevPageButton.IsEnabled = page.HasPreviousPage;
        NextPageButton.IsEnabled = page.HasNextPage;
        StatusLabel.Text = string.Empty;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!TryBuildRequest(out var request, out var errorMessage))
        {
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", errorMessage);
            return;
        }

        SaveButton.IsEnabled = false;
        SetLoading(true);
        try
        {
            var result = await _apiClient.SaveRecurringExpenseAsync(_editingId, request!, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Recurrente guardado" : "No se pudo guardar",
                result.IsSuccess ? "El gasto recurrente se guardó correctamente." : result.Message);

            if (!result.IsSuccess)
            {
                return;
            }

            FormOverlay.IsVisible = false;
            ClearForm();
            _pageNumber = 1;
            await LoadRecurringPageAsync();
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SetLoading(false);
        }
    }

    private void OnAddNewClicked(object? sender, EventArgs e)
    {
        ClearForm();
        FormTitle.Text = "Nuevo gasto recurrente";
        FormOverlay.IsVisible = true;
    }

    private void OnCancelForm(object? sender, EventArgs e)
    {
        FormOverlay.IsVisible = false;
        ClearForm();
    }

    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        RecurringExpenseItem item;
        if (e.Parameter is RecurringViewItem view)
            item = view.Item;
        else if (e.Parameter is RecurringExpenseItem direct)
            item = direct;
        else
            return;

        var confirm = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar recurrente", $"¿Eliminar '{item.Description}'?");
        if (!confirm)
        {
            return;
        }

        SetLoading(true);
        try
        {
            var result = await _apiClient.DeleteRecurringExpenseAsync(item.Id, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Recurrente eliminado" : "No se pudo eliminar",
                result.IsSuccess ? "El gasto recurrente se eliminó correctamente." : result.Message);

            if (result.IsSuccess)
            {
                await LoadRecurringPageAsync();
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnEditTapped(object? sender, TappedEventArgs e)
    {
        RecurringExpenseItem item;
        if (e.Parameter is RecurringViewItem view)
            item = view.Item;
        else if (e.Parameter is RecurringExpenseItem direct)
            item = direct;
        else
            return;

        _editingId = item.Id;
        FormTitle.Text = "Editar recurrente";
        DescriptionEntry.Text = item.Description;
        AmountEntry.Text = item.Amount.ToString("0.##");
        _selectedMovementType = item.MovementType;
        SelectedTypeLabel.Text = _colorService.IconForMovementType(item.MovementType) + " " + item.MovementType;
        _selectedPaymentMethod = item.PaymentMethod;
        SelectedPayLabel.Text = _paymentIconService.IconForPaymentMethod(item.PaymentMethod) + " " + item.PaymentMethod;
        DayEntry.Text = item.DayOfMonth.ToString();
        _selectedStartDate = item.StartDate;
        UpdateStartDateSelectorLabel();
        IsActiveCheck.IsChecked = item.IsActive;
        StatusLabel.Text = string.Empty;
        FormOverlay.IsVisible = true;
    }

    private void ClearForm()
    {
        _editingId = null;
        DescriptionEntry.Text = string.Empty;
        AmountEntry.Text = string.Empty;
        DayEntry.Text = "1";
        _selectedMovementType = null;
        _selectedPaymentMethod = null;
        SelectedTypeLabel.Text = "Seleccionar";
        SelectedPayLabel.Text = "Seleccionar";
        _selectedStartDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        UpdateStartDateSelectorLabel();
        IsActiveCheck.IsChecked = true;
        StatusLabel.Text = string.Empty;
    }

    private bool TryBuildRequest(out RecurringExpenseUpsertRequest? request, out string errorMessage)
    {
        request = null;
        errorMessage = string.Empty;

        var description = DescriptionEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            errorMessage = "La descripción es requerida.";
            return false;
        }

        if (!MoneyFormatHelper.TryParse(AmountEntry.Text, out var amount) || amount <= 0)
        {
            errorMessage = "El monto debe ser mayor a cero.";
            return false;
        }

        if (!int.TryParse(DayEntry.Text, out var day) || day < 1 || day > 31)
        {
            errorMessage = "El día debe estar entre 1 y 31.";
            return false;
        }

        if (_selectedStartDate == default)
        {
            _selectedStartDate = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        var maxDay = DateTime.DaysInMonth(_selectedStartDate.Year, _selectedStartDate.Month);
        var normalizedDay = Math.Min(day, maxDay);
        var normalizedStartDate = new DateOnly(_selectedStartDate.Year, _selectedStartDate.Month, normalizedDay);
        _selectedStartDate = normalizedStartDate;
        UpdateStartDateSelectorLabel();

        if (string.IsNullOrEmpty(_selectedMovementType) || string.IsNullOrEmpty(_selectedPaymentMethod))
        {
            errorMessage = "Debes elegir tipo y medio de pago.";
            return false;
        }

        request = new RecurringExpenseUpsertRequest(
            description,
            amount,
            _selectedMovementType,
            _selectedPaymentMethod,
            normalizedDay,
            normalizedStartDate,
            IsActiveCheck.IsChecked);

        return true;
    }

    private async void OnStartDateSelectorTapped(object? sender, TappedEventArgs e)
    {
        var selected = await CalendarDateModalPage.PickAsync(
            this,
            "Inicio recurrente",
            _selectedStartDate,
            DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            DateOnly.FromDateTime(DateTime.Today.AddYears(10)));

        if (selected is null)
        {
            return;
        }

        _selectedStartDate = selected.Value;
        UpdateStartDateSelectorLabel();
    }
    private void UpdateStartDateSelectorLabel()
    {
        StartDateSelectorLabel.Text = _selectedStartDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (_isPageSizeSyncing || PageSizePicker.SelectedItem is not string pageSizeText)
        {
            return;
        }

        if (!int.TryParse(pageSizeText, out var selectedPageSize))
        {
            return;
        }

        _pageSize = AllowedPageSizes.Contains(selectedPageSize) ? selectedPageSize : 5;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
        _pageNumber = 1;
        await LoadRecurringPageAsync();
    }

    private async void OnPageSizeSelectorTapped(object? sender, TappedEventArgs e)
    {
        var options = AllowedPageSizes.Select(size => size.ToString()).ToList();
        var selected = await StyledSelectorModalPage.PickAsync(this, "Mostrar elementos", options, PageSizeSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        if (PageSizePicker.SelectedItem?.ToString() == selected)
        {
            return;
        }

        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = selected;
        _isPageSizeSyncing = false;

        if (int.TryParse(selected, out var selectedPageSize) && AllowedPageSizes.Contains(selectedPageSize))
        {
            _pageSize = selectedPageSize;
            PageSizeSelectorLabel.Text = _pageSize.ToString();
            _pageNumber = 1;
            await LoadRecurringPageAsync();
        }
    }

    private async void OnPrevPageClicked(object? sender, EventArgs e)
    {
        if (_pageNumber <= 1)
        {
            return;
        }

        _pageNumber--;
        await LoadRecurringPageAsync();
    }

    private async void OnNextPageClicked(object? sender, EventArgs e)
    {
        _pageNumber++;
        await LoadRecurringPageAsync();
    }

    private async void OnClosePageClicked(object? sender, EventArgs e)
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync();
            return;
        }

        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    private sealed record RecurringViewItem(RecurringExpenseItem Item, Color IconColor, string Icon)
    {
        public string Id => Item.Id;
        public string Description => Item.Description;
        public decimal Amount => Item.Amount;
        public string MovementType => Item.MovementType;
        public string PaymentMethod => Item.PaymentMethod;
        public int DayOfMonth => Item.DayOfMonth;
        public DateOnly StartDate => Item.StartDate;
        public bool IsActive => Item.IsActive;
    }
}