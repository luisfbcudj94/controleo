using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Features.Auth;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Features.Settings;

public enum SettingsSectionMode
{
    All,
    MovementOnly,
    PaymentOnly
}

public partial class SettingsPage : ContentPage
{
    private const double DefaultDualSectionListHeight = 240;

    private readonly IExpenseApiClient _apiClient;
    private readonly IAuthService _authService;
    private readonly ICatalogColorService _colorService;
    private readonly IPaymentIconService _paymentIconService;
    private readonly SettingsSectionMode _sectionMode;
    private readonly bool _showSessionActions;
    private readonly ObservableCollection<string> _movementTypes = [];
    private readonly ObservableCollection<string> _paymentMethods = [];
    private readonly List<MovementTypeConfig> _configs = [];
    private readonly List<PaymentMethodConfig> _paymentConfigs = [];
    private bool _isEditingMovement;
    private bool _editingMovementWillPickIconColor;
    private string? _editingOriginalValue;
    private string _editSelectedIcon = "📋";
    private string? _editSelectedColor;
    private bool _isAddingMovement; // true = movement, false = payment
    private bool _isRefreshing;
    private string _editSelectedPaymentIcon = "💳";
    private string _addSelectedPaymentIcon = "💳";

    public SettingsPage(IExpenseApiClient apiClient, IAuthService authService, ICatalogColorService colorService, IPaymentIconService paymentIconService)
        : this(apiClient, authService, colorService, paymentIconService, SettingsSectionMode.All, showSessionActions: true)
    {
    }

    public SettingsPage(
        IExpenseApiClient apiClient,
        IAuthService authService,
        ICatalogColorService colorService,
        IPaymentIconService paymentIconService,
        SettingsSectionMode sectionMode,
        bool showSessionActions = false)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authService = authService;
        _colorService = colorService;
        _paymentIconService = paymentIconService;
        Resources.Add("MovementIconConverter", new MovementIconConverter(_colorService));
        Resources.Add("PaymentIconConverter", new PaymentIconConverter(_paymentIconService));
        _sectionMode = sectionMode;
        _showSessionActions = showSessionActions;
        MovementCollection.ItemsSource = _movementTypes;
        PaymentCollection.ItemsSource = _paymentMethods;
        ApplySectionMode();
    }

    public SettingsSectionMode SectionMode => _sectionMode;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyCollectionHeights();
        await LoadDataAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyCollectionHeights();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        SetLoading(true);
        try
        {
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            _configs.Clear();

            _colorService.SetConfigs(catalog.MovementTypeConfigs);
            foreach (var movementType in catalog.MovementTypes)
            {
                var existing = catalog.MovementTypeConfigs
                    ?.FirstOrDefault(cfg => string.Equals(cfg.Name, movementType, StringComparison.OrdinalIgnoreCase));

                var resolvedColor = !string.IsNullOrWhiteSpace(existing?.Color)
                    ? existing.Color.Trim()
                    : _colorService.HexForMovementType(movementType);

                _configs.Add(new MovementTypeConfig(
                    movementType,
                    _colorService.IconForMovementType(movementType),
                    resolvedColor));
            }

            _colorService.SetConfigs(_configs);

            _paymentConfigs.Clear();
            if (catalog.PaymentMethodConfigs is not null)
            {
                _paymentConfigs.AddRange(catalog.PaymentMethodConfigs);
            }

            _movementTypes.Clear();
            foreach (var item in catalog.MovementTypes)
                _movementTypes.Add(item);

            _paymentMethods.Clear();
            foreach (var item in catalog.PaymentMethods)
            {
                _paymentMethods.Add(item);

                var configuredIcon = _paymentConfigs
                    .FirstOrDefault(cfg => string.Equals(cfg.Name, item, StringComparison.OrdinalIgnoreCase))
                    ?.Icon;

                if (!string.IsNullOrWhiteSpace(configuredIcon))
                {
                    _paymentIconService.SetIconForPaymentMethod(item, configuredIcon);
                }

                UpsertPaymentConfig(item, _paymentIconService.IconForPaymentMethod(item));
            }

            _paymentConfigs.RemoveAll(cfg => !_paymentMethods.Any(item => string.Equals(item, cfg.Name, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _isRefreshing = false;
            SettingsRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private void ApplySectionMode()
    {
        switch (_sectionMode)
        {
            case SettingsSectionMode.MovementOnly:
                Title = "Tipos de gasto";
                PageTitleLabel.Text = "Tipos de gasto";
                PageHeaderRow.IsVisible = true;
                MovementSectionCard.IsVisible = true;
                PaymentSectionCard.IsVisible = false;
                MovementInlineAddButton.IsVisible = false;
                PaymentInlineAddButton.IsVisible = false;
                MovementAddCtaButton.IsVisible = true;
                PaymentAddCtaButton.IsVisible = false;
                break;

            case SettingsSectionMode.PaymentOnly:
                Title = "Medios de pago";
                PageTitleLabel.Text = "Medios de pago";
                PageHeaderRow.IsVisible = true;
                MovementSectionCard.IsVisible = false;
                PaymentSectionCard.IsVisible = true;
                MovementInlineAddButton.IsVisible = false;
                PaymentInlineAddButton.IsVisible = false;
                MovementAddCtaButton.IsVisible = false;
                PaymentAddCtaButton.IsVisible = true;
                break;

            default:
                Title = "Configuración";
                PageTitleLabel.Text = "Configuración";
                PageHeaderRow.IsVisible = false;
                MovementSectionCard.IsVisible = true;
                PaymentSectionCard.IsVisible = true;
                MovementInlineAddButton.IsVisible = true;
                PaymentInlineAddButton.IsVisible = true;
                MovementAddCtaButton.IsVisible = false;
                PaymentAddCtaButton.IsVisible = false;
                break;
        }

        SessionSectionCard.IsVisible = _showSessionActions;
        ApplyCollectionHeights();
    }

    private void ApplyCollectionHeights()
    {
        if (Height <= 0)
        {
            return;
        }

        switch (_sectionMode)
        {
            case SettingsSectionMode.MovementOnly:
                MovementCollection.HeightRequest = Math.Max(280, Height - 230);
                PaymentCollection.HeightRequest = -1;
                break;

            case SettingsSectionMode.PaymentOnly:
                PaymentCollection.HeightRequest = Math.Max(280, Height - 230);
                MovementCollection.HeightRequest = -1;
                break;

            default:
                MovementCollection.HeightRequest = DefaultDualSectionListHeight;
                PaymentCollection.HeightRequest = DefaultDualSectionListHeight;
                break;
        }
    }

    // ── Add flows via modal ──

    private string _addSelectedIcon = "📋";
    private string? _addSelectedColor;

    private void OnAddMovementTapped(object? sender, EventArgs e)
    {
        _isAddingMovement = true;
        _addSelectedIcon = "📋";
        _addSelectedColor = null;
        AddModalTitleLabel.Text = "Nueva sección";
        AddModalEntry.Text = string.Empty;
        AddModalEntry.Placeholder = "Nombre de la sección";
        AddIconSection.IsVisible = true;
        AddColorLabel.IsVisible = true;
        AddColorScroll.IsVisible = true;
        PopulateAddIconGrid("📋");
        PopulateColorRow(AddColorRow, _colorService.GetAvailableColors(_configs), null,
            hex => { _addSelectedColor = hex; RebuildAddColorRow(); });
        AddOverlay.IsVisible = true;
        AddModalEntry.Focus();
    }

    private void OnAddPaymentTapped(object? sender, EventArgs e)
    {
        _isAddingMovement = false;
        _addSelectedPaymentIcon = "💳";
        AddModalTitleLabel.Text = "Nuevo medio de pago";
        AddModalEntry.Text = string.Empty;
        AddModalEntry.Placeholder = "Nombre del medio";
        AddIconSection.IsVisible = true;
        AddColorLabel.IsVisible = false;
        AddColorScroll.IsVisible = false;
        PopulateAddPaymentIconGrid(_addSelectedPaymentIcon);
        AddOverlay.IsVisible = true;
        AddModalEntry.Focus();
    }

    private void OnCancelAddModal(object? sender, EventArgs e)
    {
        AddOverlay.IsVisible = false;
    }

    private async void OnConfirmAddModal(object? sender, EventArgs e)
    {
        var name = AddModalEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Dato inválido", "Ingresa un nombre.");
            return;
        }

        AddOverlay.IsVisible = false;

        if (_isAddingMovement)
        {
            if (_movementTypes.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Esa sección ya existe.");
                return;
            }

            var availableColors = _colorService.GetAvailableColors(_configs);
            if (availableColors.Count == 0)
            {
                await StyledResultModalPage.ShowAsync(this, false, "Sin colores", "No hay colores disponibles.");
                return;
            }

            var selectedIcon = _addSelectedIcon;
            var selectedColor = _addSelectedColor ?? availableColors[0].Hex;
            AddOverlay.IsVisible = false;

            _movementTypes.Add(name);
            _configs.Add(new MovementTypeConfig(name, selectedIcon, selectedColor));
            await SaveCatalogsAsync();
        }
        else
        {
            if (_paymentMethods.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Ese medio ya existe.");
                return;
            }

            _paymentMethods.Add(name);
            _paymentIconService.SetIconForPaymentMethod(name, _addSelectedPaymentIcon);
            UpsertPaymentConfig(name, _addSelectedPaymentIcon);
            await SaveCatalogsAsync();
        }
    }

    // ── Edit inline per item ──

    private void OnEditMovementItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string value) return;
        _isEditingMovement = true;
        _editingMovementWillPickIconColor = true;
        _editingOriginalValue = value;
        EditModalTitleLabel.Text = "Editar sección";
        EditModalEntry.Text = value;

        // Resolve icon/color through the shared service to avoid stale invalid icons.
        var cfg = _configs.FirstOrDefault(c => string.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase));
        _editSelectedIcon = _colorService.IconForMovementType(value);
        _editSelectedColor = cfg?.Color;
        EditIconSection.IsVisible = true;
        EditColorLabel.IsVisible = true;
        EditColorScroll.IsVisible = true;
        PopulateEditIconGrid(_editSelectedIcon);
        PopulateColorRow(EditColorRow, _colorService.GetAvailableColors(_configs, value), _editSelectedColor,
            hex => { _editSelectedColor = hex; RebuildEditColorRow(); });

        EditOverlay.IsVisible = true;
    }

    private void OnEditPaymentItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string value) return;
        _isEditingMovement = false;
        _editingMovementWillPickIconColor = false;

        var configuredIcon = _paymentConfigs
            .FirstOrDefault(cfg => string.Equals(cfg.Name, value, StringComparison.OrdinalIgnoreCase))
            ?.Icon;

        if (!string.IsNullOrWhiteSpace(configuredIcon))
        {
            _paymentIconService.SetIconForPaymentMethod(value, configuredIcon);
        }

        _editSelectedPaymentIcon = _paymentIconService.IconForPaymentMethod(value);
        _editingOriginalValue = value;
        EditModalTitleLabel.Text = "Editar medio de pago";
        EditModalEntry.Text = value;
        EditIconSection.IsVisible = true;
        EditColorLabel.IsVisible = false;
        EditColorScroll.IsVisible = false;
        PopulateEditPaymentIconGrid(_editSelectedPaymentIcon);
        EditOverlay.IsVisible = true;
    }

    private async void OnDeleteMovementItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string value) return;

        // Check how many expenses exist for this movement type
        int count;
        SetLoading(true);
        try
        {
            count = await _apiClient.CountExpensesByMovementTypeAsync(value, CancellationToken.None);
        }
        finally
        {
            SetLoading(false);
        }

        string confirmMessage;
        if (count > 0)
        {
            confirmMessage = $"'{value}' tiene {count} gasto(s) registrado(s).\n\n" +
                             "Si eliminas esta sección, se borrarán TODOS los gastos, presupuestos y recurrentes asociados.\n\n" +
                             "¿Deseas continuar?";
        }
        else
        {
            confirmMessage = $"¿Eliminar la sección '{value}'?";
        }

        var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar sección", confirmMessage);
        if (!confirmed) return;

        // Cascade delete all data for this movement type on the server
        if (count > 0)
        {
            SetLoading(true);
            OperationResult deleteResult;
            try
            {
                deleteResult = await _apiClient.DeleteAllByMovementTypeAsync(value, CancellationToken.None);
            }
            finally
            {
                SetLoading(false);
            }

            if (!deleteResult.IsSuccess)
            {
                await StyledResultModalPage.ShowAsync(this, false, "Error", deleteResult.Message);
                return;
            }
        }

        // Remove from local lists and save catalogs
        _movementTypes.Remove(value);
        _configs.RemoveAll(c => string.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase));
        await SaveCatalogsAsync();
        await StyledResultModalPage.ShowAsync(this, true, "Eliminado",
            count > 0 ? $"Se eliminó '{value}' y {count} gasto(s) asociados." : $"Sección '{value}' eliminada.");
    }

    private async void OnDeletePaymentItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string value) return;
        var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar medio", $"¿Eliminar '{value}'?");
        if (!confirmed) return;
        _paymentMethods.Remove(value);
        _paymentIconService.RemovePaymentMethod(value);
        _paymentConfigs.RemoveAll(cfg => string.Equals(cfg.Name, value, StringComparison.OrdinalIgnoreCase));
        await SaveCatalogsAsync();
    }

    // ── Edit modal confirm ──

    private void OnCancelEditModalClicked(object? sender, EventArgs e)
    {
        EditOverlay.IsVisible = false;
        _editingOriginalValue = null;
    }

    private async void OnConfirmEditModalClicked(object? sender, EventArgs e)
    {
        var edited = EditModalEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(edited) || string.IsNullOrWhiteSpace(_editingOriginalValue))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Dato inválido", "Ingresa un valor válido.");
            return;
        }

        if (_isEditingMovement)
        {
            if (_movementTypes.Any(item =>
                    !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Esa sección ya existe.");
                return;
            }

            ReplaceItem(_movementTypes, _editingOriginalValue, edited);
            var cfgIdx = _configs.FindIndex(c =>
                string.Equals(c.Name, _editingOriginalValue, StringComparison.OrdinalIgnoreCase));

            if (_editingMovementWillPickIconColor)
            {
                var oldCfg = cfgIdx >= 0 ? _configs[cfgIdx] : null;
                var selectedIcon = _editSelectedIcon;
                var selectedColor = _editSelectedColor ?? oldCfg?.Color ?? "#B6E6BD";

                EditOverlay.IsVisible = false;

                if (cfgIdx >= 0)
                    _configs[cfgIdx] = new MovementTypeConfig(edited, selectedIcon, selectedColor);
                else
                    _configs.Add(new MovementTypeConfig(edited, selectedIcon, selectedColor));
            }
            else if (cfgIdx >= 0)
            {
                var old = _configs[cfgIdx];
                _configs[cfgIdx] = new MovementTypeConfig(edited, old.Icon, old.Color);
            }

            _editingMovementWillPickIconColor = false;
        }
        else
        {
            var originalPaymentName = _editingOriginalValue;
            if (_paymentMethods.Any(item =>
                    !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Ese medio ya existe.");
                return;
            }

            ReplaceItem(_paymentMethods, _editingOriginalValue, edited);

            if (!string.IsNullOrWhiteSpace(originalPaymentName))
            {
                _paymentIconService.RenamePaymentMethod(originalPaymentName, edited);
                _paymentConfigs.RemoveAll(cfg => string.Equals(cfg.Name, originalPaymentName, StringComparison.OrdinalIgnoreCase));
            }

            _paymentIconService.SetIconForPaymentMethod(edited, _editSelectedPaymentIcon);
            UpsertPaymentConfig(edited, _editSelectedPaymentIcon);
        }

        EditOverlay.IsVisible = false;
        _editingOriginalValue = null;
        await SaveCatalogsAsync();
    }

    // ── Save & helpers ──

    private void PopulateEditIconGrid(string? selectedIcon)
    {
        PopulateIconGrid(EditIconGrid, _colorService.AvailableIcons, selectedIcon,
            emoji => { _editSelectedIcon = emoji; PopulateEditIconGrid(emoji); });
    }

    private void PopulateAddIconGrid(string? selectedIcon)
    {
        PopulateIconGrid(AddIconGrid, _colorService.AvailableIcons, selectedIcon,
            emoji => { _addSelectedIcon = emoji; PopulateAddIconGrid(emoji); });
    }

    private void PopulateEditPaymentIconGrid(string? selectedIcon)
    {
        PopulateIconGrid(EditIconGrid, _paymentIconService.AvailableIcons, selectedIcon,
            emoji => { _editSelectedPaymentIcon = emoji; PopulateEditPaymentIconGrid(emoji); });
    }

    private void PopulateAddPaymentIconGrid(string? selectedIcon)
    {
        PopulateIconGrid(AddIconGrid, _paymentIconService.AvailableIcons, selectedIcon,
            emoji => { _addSelectedPaymentIcon = emoji; PopulateAddPaymentIconGrid(emoji); });
    }

    private void RebuildEditColorRow()
    {
        PopulateColorRow(EditColorRow, _colorService.GetAvailableColors(_configs, _editingOriginalValue), _editSelectedColor,
            hex => { _editSelectedColor = hex; RebuildEditColorRow(); });
    }

    private void RebuildAddColorRow()
    {
        PopulateColorRow(AddColorRow, _colorService.GetAvailableColors(_configs), _addSelectedColor,
            hex => { _addSelectedColor = hex; RebuildAddColorRow(); });
    }

    private static void PopulateIconGrid(
        Grid grid,
        IReadOnlyList<(string Emoji, string Label)> iconSource,
        string? selectedIcon,
        Action<string> onSelected)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        var icons = iconSource;
        var cols = 6;
        var rows = (int)Math.Ceiling(icons.Count / (double)cols);

        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < icons.Count; i++)
        {
            var (emoji, _) = icons[i];
            var isSelected = string.Equals(emoji, selectedIcon);

            var border = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                BackgroundColor = isSelected ? Color.FromArgb("#D8F3DC") : Color.FromArgb("#F2F5F3"),
                Stroke = isSelected ? Color.FromArgb("#40916C") : Colors.Transparent,
                StrokeThickness = isSelected ? 2 : 0,
                HeightRequest = 44,
                Margin = new Thickness(2),
                Padding = 0,
            };

            border.Content = new Label
            {
                Text = emoji,
                FontSize = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };

            var capturedEmoji = emoji;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onSelected(capturedEmoji);
            border.GestureRecognizers.Add(tap);

            Grid.SetColumn(border, i % cols);
            Grid.SetRow(border, i / cols);
            grid.Children.Add(border);
        }
    }

    private static void PopulateColorRow(
        HorizontalStackLayout row,
        IReadOnlyList<(string Hex, string Label)> colors,
        string? selectedHex,
        Action<string>? onSelected)
    {
        row.Children.Clear();
        foreach (var (hex, _) in colors)
        {
            var isSelected = string.Equals(hex, selectedHex, StringComparison.OrdinalIgnoreCase);
            var circle = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) },
                BackgroundColor = Color.FromArgb(hex),
                WidthRequest = 40,
                HeightRequest = 40,
                Stroke = isSelected ? Color.FromArgb("#1B4332") : Colors.Transparent,
                StrokeThickness = isSelected ? 3 : 0,
            };

            if (isSelected)
            {
                circle.Content = new Label
                {
                    Text = "✓",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1B4332"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                };
            }

            if (onSelected is not null)
            {
                var capturedHex = hex;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => onSelected(capturedHex);
                circle.GestureRecognizers.Add(tap);
            }

            row.Children.Add(circle);
        }
    }

    private async Task SaveCatalogsAsync()
    {
        if (_movementTypes.Count == 0 || _paymentMethods.Count == 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar",
                "Debe existir al menos una sección y un medio de pago.");
            return;
        }

        SetLoading(true);
        try
        {
            _paymentConfigs.RemoveAll(cfg => !_paymentMethods.Any(item => string.Equals(item, cfg.Name, StringComparison.OrdinalIgnoreCase)));
            foreach (var paymentMethod in _paymentMethods)
            {
                if (_paymentConfigs.Any(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                UpsertPaymentConfig(paymentMethod, _paymentIconService.IconForPaymentMethod(paymentMethod));
            }

            var request = new UpdateCatalogsRequest(_movementTypes.ToList(), _paymentMethods.ToList(), _configs.ToList(), _paymentConfigs.ToList());
            var result = await _apiClient.UpdateCatalogsAsync(request, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(this, result.IsSuccess,
                result.IsSuccess ? "Guardado" : "Error", result.IsSuccess ? "Catálogos actualizados." : result.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private static void ReplaceItem(ObservableCollection<string> source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue);
        if (index >= 0) source[index] = newValue;
    }

    private void UpsertPaymentConfig(string paymentMethod, string icon)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod) || string.IsNullOrWhiteSpace(icon))
        {
            return;
        }

        var existing = _paymentConfigs.FirstOrDefault(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase));
        _paymentConfigs.RemoveAll(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase));
        _paymentConfigs.Add(existing is null
            ? new PaymentMethodConfig(paymentMethod.Trim(), icon.Trim())
            : existing with { Name = paymentMethod.Trim(), Icon = icon.Trim() });
    }

    private async void OnRefreshing(object? sender, EventArgs e) => await LoadDataAsync();

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        SetLoading(true);
        try
        {
            await _authService.SignOutAsync();
            if (Application.Current?.Windows.FirstOrDefault() is { } window)
            {
                var loginPage = new LoginPage(_authService);
                NavigationPage.SetHasNavigationBar(loginPage, false);
                window.Page = new NavigationPage(loginPage);
            }
        }
        finally
        {
            SetLoading(false);
        }
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

    private void SetLoading(bool isLoading) => LoadingOverlay.IsVisible = isLoading;

    private sealed class MovementIconConverter : IValueConverter
    {
        private readonly ICatalogColorService _colorService;

        public MovementIconConverter(ICatalogColorService colorService)
        {
            _colorService = colorService;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrWhiteSpace(name))
                return _colorService.IconForMovementType(name) + " " + name;
            return value?.ToString() ?? "";
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class PaymentIconConverter : IValueConverter
    {
        private readonly IPaymentIconService _paymentIconService;

        public PaymentIconConverter(IPaymentIconService paymentIconService)
        {
            _paymentIconService = paymentIconService;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string name && !string.IsNullOrWhiteSpace(name))
            {
                var emoji = _paymentIconService.IconForPaymentMethod(name);
                return emoji + " " + name;
            }
            return value?.ToString() ?? "";
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}