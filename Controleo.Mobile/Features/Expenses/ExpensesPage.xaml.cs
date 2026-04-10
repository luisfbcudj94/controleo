using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Features.Expenses;

public partial class ExpensesPage : ContentPage
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20];

    private static readonly Dictionary<string, (string bg, string fg)> PaymentTileColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TC Black"] = ("#1A2E23", "#FFFFFF"),
        ["TC Rappi"] = ("#FF6B35", "#FFFFFF"),
        ["TD Bancolombia"] = ("#FFD700", "#1A2E23"),
        ["Bancolombia"] = ("#FFD700", "#1A2E23"),
        ["TC Nu"] = ("#7B2D8E", "#FFFFFF"),
        ["Efectivo"] = ("#B6E6BD", "#1A2E23"),
        ["Transferencia"] = ("#A8D8F0", "#1A2E23"),
        ["Nequi"] = ("#00C389", "#FFFFFF"),
    };

    private readonly IExpenseApiClient _apiClient;
    private readonly IMonthContextService _monthContext;
    private readonly IAuthService _authService;
    private readonly IConnectivityService _connectivityService;
    private readonly ICatalogColorService _colorService;
    private readonly IPaymentIconService _paymentIconService;
    private readonly ObservableCollection<ExpenseViewItem> _expenses = [];
    private ExpenseCatalog _catalog = new([], []);
    private ExpenseItem? _selectedExpense;
    private bool _isRefreshing;
    private bool _isMonthPickerSyncing;
    private bool _isPageSizeSyncing;
    private bool _isActive;
    private int _pageNumber = 1;
    private int _pageSize = 5;
    private string _searchTerm = string.Empty;
    private string? _selectedMovementTypeFilter;
    private string? _selectedPaymentMethodFilter;
    private DateOnly _editSelectedDate = DateOnly.FromDateTime(DateTime.Today);
    private string? _editSelectedMovementType;
    private string? _editSelectedPaymentMethod;
    private bool _offlineStatusToastDismissed;
    private bool _lastConnectivityOnlineState;

    public ExpensesPage(
        IExpenseApiClient apiClient,
        IMonthContextService monthContext,
        IAuthService authService,
        IConnectivityService connectivityService,
        ICatalogColorService colorService,
        IPaymentIconService paymentIconService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        _authService = authService;
        _connectivityService = connectivityService;
        _lastConnectivityOnlineState = _connectivityService.IsOnline;
        _colorService = colorService;
        _paymentIconService = paymentIconService;
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;
        SyncMonthSelection();
        ExpensesCollection.ItemsSource = _expenses;
        PageSizePicker.ItemsSource = AllowedPageSizes.Select(item => item.ToString()).ToList();
        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = _pageSize.ToString();
        _isPageSizeSyncing = false;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
        MoneyFormatHelper.Attach(EditAmountEntry);
        UpdateEditSelectorUi();
        UpdateFilterUi();
        UpdateOfflineStatusToast();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isActive = true;
        UpdateOfflineStatusToast();
        await RefreshMonthOptionsAsync();
        await LoadDataAsync();
    }

    private async Task RefreshMonthOptionsAsync()
    {
        var monthKeys = await _apiClient.GetAvailableMonthsAsync(CancellationToken.None);
        _monthContext.SetAvailableMonths(monthKeys);
        var currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        _monthContext.SetMonth(currentMonth);
        RefreshMonthPickerItems();
        SyncMonthSelection();
    }

    protected override void OnDisappearing()
    {
        _isActive = false;
        base.OnDisappearing();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        SetLoading(true);
        StatusLabel.Text = "Cargando información...";
        try
        {
            _catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            _colorService.SetConfigs(_catalog.MovementTypeConfigs);
            SyncPaymentIcons(_catalog);

            var page = await _apiClient.GetExpensesPageAsync(
                _monthContext.SelectedMonthKey,
                _pageNumber,
                _pageSize,
                movementType: _selectedMovementTypeFilter,
                paymentMethod: _selectedPaymentMethodFilter,
                searchTerm: _searchTerm,
                CancellationToken.None);

            _pageNumber = page.PageNumber;
            _expenses.Clear();
            foreach (var item in page.Items)
            {
                _expenses.Add(new ExpenseViewItem(item, _colorService.ForMovementType(item.MovementType), _colorService.IconForMovementType(item.MovementType)));
            }

            PaginationStatusLabel.Text = $"Página {page.PageNumber}/{page.TotalPages} · {page.TotalCount} registros";
            PrevPageButton.IsEnabled = page.HasPreviousPage;
            NextPageButton.IsEnabled = page.HasNextPage;

            StatusLabel.Text = string.Empty;
        }
        finally
        {
            _isRefreshing = false;
            ExpensesRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private async void OnDeleteExpenseTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExpenseViewItem view)
        {
            return;
        }

        var expense = view.Item;

        var confirm = await StyledConfirmModalPage.ConfirmAsync(
            this,
            "Eliminar gasto",
            $"¿Deseas eliminar '{expense.Description}'?");
        if (!confirm)
        {
            return;
        }

        SetLoading(true);
        try
        {
            var result = await _apiClient.DeleteExpenseAsync(expense.Id, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Gasto eliminado" : "No se pudo eliminar",
                result.IsSuccess ? "El gasto se eliminó correctamente." : result.Message);

            if (result.IsSuccess)
            {
                EditPanel.IsVisible = false;
                _selectedExpense = null;
                await LoadDataAsync();
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnEditExpenseTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExpenseViewItem view)
        {
            return;
        }

        var expense = view.Item;

        _selectedExpense = expense;
        _editSelectedDate = expense.Date;
        UpdateEditDateSelectorLabel();
        EditDescriptionEntry.Text = expense.Description;
        EditAmountEntry.Text = MoneyFormatHelper.FormatWithDots(((long)expense.Amount).ToString());
        _editSelectedMovementType = ResolveCatalogOption(expense.MovementType, _catalog.MovementTypes);
        _editSelectedPaymentMethod = ResolveCatalogOption(expense.PaymentMethod, _catalog.PaymentMethods);
        UpdateEditSelectorUi();
        EditPanel.IsVisible = true;
        StatusLabel.Text = string.Empty;
    }

    private async void OnSaveEditClicked(object? sender, EventArgs e)
    {
        if (_selectedExpense is null)
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Selecciona un gasto para editar.");
            return;
        }

        if (!MoneyFormatHelper.TryParse(EditAmountEntry.Text, out var amount))
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "El valor debe ser numérico.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditDescriptionEntry.Text) || amount == 0 ||
            string.IsNullOrWhiteSpace(_editSelectedMovementType) || string.IsNullOrWhiteSpace(_editSelectedPaymentMethod))
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Completa todos los campos y usa un valor diferente de cero.");
            return;
        }

        var request = new ExpenseEntryRequest(
            _editSelectedDate,
            EditDescriptionEntry.Text.Trim(),
            amount,
            _editSelectedMovementType!,
            _editSelectedPaymentMethod!);

        SetLoading(true);
        try
        {
            var result = await _apiClient.UpdateExpenseAsync(_selectedExpense.Id, request, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Gasto actualizado" : "No se pudo actualizar",
                result.IsSuccess ? "Los cambios se guardaron correctamente." : result.Message);

            if (result.IsSuccess)
            {
                await LoadDataAsync();
                EditPanel.IsVisible = false;
                _selectedExpense = null;
            }
            else
            {
                ResetEditInputs();
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        EditPanel.IsVisible = false;
        EditMovementPickerOverlay.IsVisible = false;
        EditPaymentPickerOverlay.IsVisible = false;
        _selectedExpense = null;
        StatusLabel.Text = string.Empty;
    }

    private void ResetEditInputs()
    {
        EditDescriptionEntry.Text = string.Empty;
        EditAmountEntry.Text = string.Empty;
        _editSelectedMovementType = null;
        _editSelectedPaymentMethod = null;
        EditMovementPickerOverlay.IsVisible = false;
        EditPaymentPickerOverlay.IsVisible = false;
        UpdateEditSelectorUi();
    }

    private void OnGoToRegisterClicked(object? sender, EventArgs e)
    {
        var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (rootPage is TabbedPage tabbedPage && tabbedPage.Children.Count > 0)
        {
            tabbedPage.CurrentPage = tabbedPage.Children[0];
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private void OnPrevMonthClicked(object? sender, EventArgs e)
    {
        _monthContext.MoveMonths(-1);
    }

    private void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _monthContext.MoveMonths(1);
    }

    private void OnMonthPickerChanged(object? sender, EventArgs e)
    {
        if (_isMonthPickerSyncing || MonthPicker.SelectedItem is not MonthContextService.MonthOption option)
        {
            return;
        }

        _monthContext.SetMonth(option.Value);
    }

    private void OnMonthChanged(object? sender, DateOnly month)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            SyncMonthSelection();
            _pageNumber = 1;
            if (!_isActive)
            {
                return;
            }

            await LoadDataAsync();
        });
    }

    private void OnMonthOptionsChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshMonthPickerItems();
            SyncMonthSelection();
        });
    }

    private void RefreshMonthPickerItems()
    {
        MonthPicker.ItemsSource = _monthContext.MonthOptions.ToList();
    }

    private void SyncMonthSelection()
    {
        var selected = _monthContext.MonthOptions.FirstOrDefault(item => item.Value == _monthContext.SelectedMonth);
        if (selected is null)
        {
            return;
        }

        _isMonthPickerSyncing = true;
        MonthPicker.SelectedItem = selected;
        _isMonthPickerSyncing = false;
        MonthSelectorLabel.Text = selected.Label;
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (isOnline != _lastConnectivityOnlineState)
        {
            _offlineStatusToastDismissed = false;
            _lastConnectivityOnlineState = isOnline;
        }

        MainThread.BeginInvokeOnMainThread(UpdateOfflineStatusToast);
    }

    private void UpdateOfflineStatusToast()
    {
        if (_connectivityService.IsOnline || _offlineStatusToastDismissed)
        {
            OfflineStatusToast.IsVisible = false;
            OfflineStatusBackdrop.IsVisible = false;
            return;
        }

        OfflineStatusToast.IsVisible = true;
        OfflineStatusBackdrop.IsVisible = true;
        OfflineStatusToast.StrokeThickness = 1;
        if (_authService.IsCurrentUserPremium)
        {
            OfflineStatusBackdropShade.Color = Color.FromArgb("#55000000");
            OfflineStatusToast.BackgroundColor = Color.FromArgb("#E7F6EE");
            OfflineStatusToast.Stroke = new SolidColorBrush(Color.FromArgb("#2D6A4F"));
            OfflineStatusTitleLabel.TextColor = Color.FromArgb("#1F5B3E");
            OfflineStatusTitleLabel.Text = "Modo offline premium activo";
            OfflineStatusLabel.Text = "Sin internet, pero puedes seguir usando Gastos sin limites.";
            OfflineStatusBenefitsLabel.Text = "Beneficios premium: registrar, editar y sincronizar automaticamente al reconectar.";
            OfflineStatusCtaButton.Text = "Continuar usando la app";
            OfflineStatusCtaButton.IsVisible = true;
            return;
        }

        OfflineStatusBackdropShade.Color = Color.FromArgb("#55000000");
        OfflineStatusToast.BackgroundColor = Color.FromArgb("#FDEBE9");
        OfflineStatusToast.Stroke = new SolidColorBrush(Color.FromArgb("#C13A2E"));
        OfflineStatusTitleLabel.TextColor = Color.FromArgb("#8E1D13");
        OfflineStatusTitleLabel.Text = "Activa premium y usa offline completo";
        OfflineStatusLabel.Text = "Sin internet. Tu plan actual no permite registrar ni editar gastos offline.";
        OfflineStatusBenefitsLabel.Text = "Suscribete por solo $0.99 al mes para registro offline, edicion offline y sincronizacion automatica.";
        OfflineStatusCtaButton.Text = "Ir a suscribirse";
        OfflineStatusCtaButton.IsVisible = true;
    }

    private async void OnOfflineStatusCtaClicked(object? sender, EventArgs e)
    {
        if (_authService.IsCurrentUserPremium)
        {
            _offlineStatusToastDismissed = true;
            OfflineStatusToast.IsVisible = false;
            OfflineStatusBackdrop.IsVisible = false;
            return;
        }

        await StyledResultModalPage.ShowAsync(
            this,
            false,
            "Suscripcion premium",
            "Oferta especial: suscribete por $0.99 y disfruta modo offline completo, edicion y sincronizacion automatica.");
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
        await LoadDataAsync();
    }

    private async void OnMonthSelectorTapped(object? sender, EventArgs e)
    {
        var monthOptions = _monthContext.MonthOptions.ToList();
        if (monthOptions.Count == 0)
        {
            return;
        }

        var options = monthOptions.Select(option => option.Label).ToList();
        var selected = await StyledSelectorModalPage.PickAsync(this, "Seleccionar mes", options, MonthSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        var selectedMonth = monthOptions.FirstOrDefault(option => option.Label == selected);
        if (selectedMonth is not null)
        {
            _monthContext.SetMonth(selectedMonth.Value);
        }
    }

    private async void OnPageSizeSelectorTapped(object? sender, EventArgs e)
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
            await LoadDataAsync();
        }
    }

    private async void OnPrevPageClicked(object? sender, EventArgs e)
    {
        if (_pageNumber <= 1)
        {
            return;
        }

        _pageNumber--;
        await LoadDataAsync();
    }

    private async void OnNextPageClicked(object? sender, EventArgs e)
    {
        _pageNumber++;
        await LoadDataAsync();
    }

    private async void OnOpenFiltersClicked(object? sender, EventArgs e)
    {
        if (_catalog.MovementTypes.Count == 0 && _catalog.PaymentMethods.Count == 0)
        {
            _catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
        }

        var selection = await ExpensesFiltersModalPage.PickAsync(
            this,
            _catalog.MovementTypes,
            _catalog.PaymentMethods,
            _selectedMovementTypeFilter,
            _selectedPaymentMethodFilter);

        if (selection is null)
        {
            return;
        }

        var newMovementType = NormalizeFilterValue(selection.MovementType);
        var newPaymentMethod = NormalizeFilterValue(selection.PaymentMethod);

        var changed = !string.Equals(_selectedMovementTypeFilter, newMovementType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_selectedPaymentMethodFilter, newPaymentMethod, StringComparison.OrdinalIgnoreCase);

        _selectedMovementTypeFilter = newMovementType;
        _selectedPaymentMethodFilter = newPaymentMethod;
        UpdateFilterUi();

        if (!changed)
        {
            return;
        }

        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchTerm = e.NewTextValue?.Trim() ?? string.Empty;
        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void OnEditDateSelectorTapped(object? sender, TappedEventArgs e)
    {
        var selected = await CalendarDateModalPage.PickAsync(
            this,
            "Fecha del gasto",
            _editSelectedDate,
            DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            DateOnly.FromDateTime(DateTime.Today.AddYears(10)));

        if (selected is null)
        {
            return;
        }

        _editSelectedDate = selected.Value;
        UpdateEditDateSelectorLabel();
    }
    private void UpdateEditDateSelectorLabel()
    {
        EditDateSelectorLabel.Text = _editSelectedDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    private void OnEditMovementSelectorTapped(object? sender, TappedEventArgs e)
    {
        var options = _catalog.MovementTypes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        EditMovementPickerOverlay.IsVisible = true;
        MainThread.BeginInvokeOnMainThread(() => BuildEditMovementPickerGrid(options));
    }

    private void OnEditPaymentSelectorTapped(object? sender, TappedEventArgs e)
    {
        var options = _catalog.PaymentMethods
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            return;
        }

        EditPaymentPickerOverlay.IsVisible = true;
        MainThread.BeginInvokeOnMainThread(() => BuildEditPaymentPickerGrid(options));
    }

    private void OnCloseEditMovementPicker(object? sender, EventArgs e)
    {
        EditMovementPickerOverlay.IsVisible = false;
    }

    private void OnCloseEditPaymentPicker(object? sender, EventArgs e)
    {
        EditPaymentPickerOverlay.IsVisible = false;
    }

    private void BuildEditMovementPickerGrid(List<string> options)
    {
        EditMovementPickerFlexLayout.Children.Clear();
        var tileWidth = CalculatePickerTileWidth(EditMovementPickerFlexLayout);

        foreach (var option in options)
        {
            var isSelected = string.Equals(option, _editSelectedMovementType, StringComparison.OrdinalIgnoreCase);
            var tile = BuildPickerTile(
                tileWidth,
                _colorService.ForMovementType(option),
                _colorService.IconForMovementType(option),
                option,
                Color.FromArgb("#1A2E23"),
                isSelected);

            var captured = option;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _editSelectedMovementType = captured;
                UpdateEditSelectorUi();
                EditMovementPickerOverlay.IsVisible = false;
            };
            tile.GestureRecognizers.Add(tap);

            EditMovementPickerFlexLayout.Children.Add(tile);
        }
    }

    private void BuildEditPaymentPickerGrid(List<string> options)
    {
        EditPaymentPickerFlexLayout.Children.Clear();
        var tileWidth = CalculatePickerTileWidth(EditPaymentPickerFlexLayout);

        foreach (var option in options)
        {
            var (bgHex, fgHex) = PaymentTileColors.TryGetValue(option, out var colors)
                ? colors
                : ("#A8D8F0", "#1A2E23");

            var isSelected = string.Equals(option, _editSelectedPaymentMethod, StringComparison.OrdinalIgnoreCase);
            var tile = BuildPickerTile(
                tileWidth,
                Color.FromArgb(bgHex),
                _paymentIconService.IconForPaymentMethod(option),
                option,
                Color.FromArgb(fgHex),
                isSelected);

            var captured = option;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _editSelectedPaymentMethod = captured;
                UpdateEditSelectorUi();
                EditPaymentPickerOverlay.IsVisible = false;
            };
            tile.GestureRecognizers.Add(tap);

            EditPaymentPickerFlexLayout.Children.Add(tile);
        }
    }

    private static Border BuildPickerTile(double width, Color backgroundColor, string icon, string text, Color foregroundColor, bool isSelected)
    {
        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            StrokeThickness = isSelected ? 2 : 0,
            Stroke = isSelected ? Color.FromArgb("#2D6A4F") : Colors.Transparent,
            BackgroundColor = backgroundColor,
            Padding = new Thickness(6),
            WidthRequest = width,
            HeightRequest = width,
            Margin = new Thickness(0, 0, 6, 6),
            Opacity = isSelected ? 1 : 0.85,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = icon,
                        FontSize = 22,
                        HorizontalOptions = LayoutOptions.Center,
                        TextColor = foregroundColor,
                    },
                    new Label
                    {
                        Text = text,
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        TextColor = foregroundColor,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        MaxLines = 2,
                    }
                }
            }
        };

        return border;
    }

    private double CalculatePickerTileWidth(FlexLayout targetLayout)
    {
        var layoutWidth = targetLayout.Width;
        if (layoutWidth <= 0)
        {
            var pageWidth = Width > 0 ? Width : DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            layoutWidth = Math.Max(220, pageWidth - 32 - 28);
        }

        const double marginRightPerTile = 6;
        const double columns = 3;
        var spacingReserve = marginRightPerTile * columns;
        var tileWidth = Math.Floor((layoutWidth - spacingReserve) / columns);
        return Math.Clamp(tileWidth, 76d, 120d);
    }

    private void UpdateEditSelectorUi()
    {
        var hasMovement = !string.IsNullOrWhiteSpace(_editSelectedMovementType);
        var hasPayment = !string.IsNullOrWhiteSpace(_editSelectedPaymentMethod);

        EditMovementSelectorLabel.Text = hasMovement
            ? $"{_colorService.IconForMovementType(_editSelectedMovementType!)} {_editSelectedMovementType}"
            : "Seleccionar";
        EditPaymentSelectorLabel.Text = hasPayment
            ? $"{_paymentIconService.IconForPaymentMethod(_editSelectedPaymentMethod!)} {_editSelectedPaymentMethod}"
            : "Seleccionar";

        EditMovementSelectorLabel.TextColor = hasMovement ? Color.FromArgb("#1A2E23") : Color.FromArgb("#708070");
        EditPaymentSelectorLabel.TextColor = hasPayment ? Color.FromArgb("#1A2E23") : Color.FromArgb("#708070");

        EditMovementSelectorBorder.Stroke = hasMovement ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#DCE5DF");
        EditPaymentSelectorBorder.Stroke = hasPayment ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#DCE5DF");
    }

    private static string? ResolveCatalogOption(string? value, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = options.FirstOrDefault(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Trim() ?? value.Trim();
    }

    private void UpdateFilterUi()
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(_selectedMovementTypeFilter))
        {
            filters.Add($"Seccion: {_selectedMovementTypeFilter}");
        }

        if (!string.IsNullOrWhiteSpace(_selectedPaymentMethodFilter))
        {
            filters.Add($"Medio: {_selectedPaymentMethodFilter}");
        }

        FilterSummaryBorder.IsVisible = filters.Count > 0;
        FilterSummaryLabel.Text = filters.Count == 0 ? string.Empty : string.Join(" | ", filters);
        FilterButton.Text = filters.Count == 0 ? "Filtrar" : $"Filtrar ({filters.Count})";
    }

    private void SyncPaymentIcons(ExpenseCatalog catalog)
    {
        foreach (var paymentMethod in catalog.PaymentMethods)
        {
            var configuredIcon = (catalog.PaymentMethodConfigs ?? [])
                .FirstOrDefault(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase))
                ?.Icon;

            if (!string.IsNullOrWhiteSpace(configuredIcon))
            {
                _paymentIconService.SetIconForPaymentMethod(paymentMethod, configuredIcon);
                continue;
            }

            _paymentIconService.IconForPaymentMethod(paymentMethod);
        }
    }

    private static string? NormalizeFilterValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal sealed record ExpenseViewItem(ExpenseItem Item, Color CardColor, string Icon)
    {
        public string Id => Item.Id;
        public DateOnly Date => Item.Date;
        public string Description => Item.Description;
        public decimal Amount => Item.Amount;
        public string MovementType => Item.MovementType;
        public string PaymentMethod => Item.PaymentMethod;
    }
}