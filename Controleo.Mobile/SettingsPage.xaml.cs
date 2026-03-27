using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class SettingsPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly AuthService _authService;
    private readonly ObservableCollection<string> _movementTypes = [];
    private readonly ObservableCollection<string> _paymentMethods = [];
    private string? _selectedMovement;
    private string? _selectedPayment;
    private bool _isEditingMovement;
    private string? _editingOriginalValue;
    private bool _isRefreshing;

    public SettingsPage(ExpenseApiClient apiClient, AuthService authService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authService = authService;
        MovementCollection.ItemsSource = _movementTypes;
        PaymentCollection.ItemsSource = _paymentMethods;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        SetLoading(true);
        try
        {
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);

            _movementTypes.Clear();
            foreach (var item in catalog.MovementTypes)
            {
                _movementTypes.Add(item);
            }

            _paymentMethods.Clear();
            foreach (var item in catalog.PaymentMethods)
            {
                _paymentMethods.Add(item);
            }

            _selectedMovement = null;
            _selectedPayment = null;
            SelectedMovementLabel.Text = "Sin sección seleccionada";
            SelectedPaymentLabel.Text = "Sin medio seleccionado";
        }
        finally
        {
            _isRefreshing = false;
            SettingsRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private async void OnAddMovementClicked(object? sender, EventArgs e)
    {
        if (AddItem(_movementTypes, NewMovementEntry, "sección"))
        {
            await SaveCatalogsAsync();
        }
    }

    private async void OnAddPaymentClicked(object? sender, EventArgs e)
    {
        if (AddItem(_paymentMethods, NewPaymentEntry, "medio de pago"))
        {
            await SaveCatalogsAsync();
        }
    }

    private void OnEditMovementClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMovement))
        {
            ResetSettingsInputs();
            _ = StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Selecciona una sección para editar.");
            return;
        }

        _isEditingMovement = true;
        _editingOriginalValue = _selectedMovement;
        EditModalTitleLabel.Text = "Editar sección";
        EditModalEntry.Text = _selectedMovement;
        EditOverlay.IsVisible = true;
    }

    private void OnEditPaymentClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedPayment))
        {
            ResetSettingsInputs();
            _ = StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Selecciona un medio de pago para editar.");
            return;
        }

        _isEditingMovement = false;
        _editingOriginalValue = _selectedPayment;
        EditModalTitleLabel.Text = "Editar medio de pago";
        EditModalEntry.Text = _selectedPayment;
        EditOverlay.IsVisible = true;
    }

    private async void OnDeleteMovementClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMovement))
        {
            ResetSettingsInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo eliminar", "Selecciona una sección para eliminar.");
            return;
        }

        var confirmed = await StyledConfirmModalPage.ConfirmAsync(
            this,
            "Eliminar sección",
            $"¿Deseas eliminar '{_selectedMovement}'?");
        if (!confirmed)
        {
            return;
        }

        _movementTypes.Remove(_selectedMovement);
        _selectedMovement = null;
        MovementCollection.SelectedItem = null;
        SelectedMovementLabel.Text = "Sin sección seleccionada";
        await SaveCatalogsAsync();
    }

    private async void OnDeletePaymentClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedPayment))
        {
            ResetSettingsInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo eliminar", "Selecciona un medio de pago para eliminar.");
            return;
        }

        var confirmed = await StyledConfirmModalPage.ConfirmAsync(
            this,
            "Eliminar medio de pago",
            $"¿Deseas eliminar '{_selectedPayment}'?");
        if (!confirmed)
        {
            return;
        }

        _paymentMethods.Remove(_selectedPayment);
        _selectedPayment = null;
        PaymentCollection.SelectedItem = null;
        SelectedPaymentLabel.Text = "Sin medio seleccionado";
        await SaveCatalogsAsync();
    }

    private async Task SaveCatalogsAsync()
    {
        if (_movementTypes.Count == 0 || _paymentMethods.Count == 0)
        {
            ResetSettingsInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "Debe existir al menos una sección y un medio de pago.");
            return;
        }

        SetLoading(true);

        var request = new UpdateCatalogsRequest(_movementTypes.ToList(), _paymentMethods.ToList());
        var result = await _apiClient.UpdateCatalogsAsync(request, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Cambios guardados" : "No se pudo guardar",
            result.IsSuccess ? "Los catálogos se actualizaron correctamente." : result.Message);

        if (!result.IsSuccess)
        {
            ResetSettingsInputs();
        }
        SetLoading(false);
    }

    private void OnMovementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedMovement = e.CurrentSelection.FirstOrDefault()?.ToString();
        SelectedMovementLabel.Text = string.IsNullOrWhiteSpace(_selectedMovement)
            ? "Sin sección seleccionada"
            : $"Seleccionada: {_selectedMovement}";
    }

    private void OnPaymentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedPayment = e.CurrentSelection.FirstOrDefault()?.ToString();
        SelectedPaymentLabel.Text = string.IsNullOrWhiteSpace(_selectedPayment)
            ? "Sin medio seleccionado"
            : $"Seleccionado: {_selectedPayment}";
    }

    private bool AddItem(ObservableCollection<string> source, Entry entry, string label)
    {
        var value = entry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            entry.Text = "0";
            _ = StyledResultModalPage.ShowAsync(this, false, "Dato inválido", $"Ingresa un nombre de {label}.");
            return false;
        }

        if (source.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            entry.Text = "0";
            _ = StyledResultModalPage.ShowAsync(this, false, "Dato duplicado", $"Ese {label} ya existe.");
            return false;
        }

        source.Add(value);
        entry.Text = string.Empty;
        return true;
    }

    private static void ReplaceItem(ObservableCollection<string> source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue);
        if (index >= 0)
        {
            source[index] = newValue;
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }


    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        await _authService.SignOutAsync();

        if (Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            var loginPage = new LoginPage(_authService);
            NavigationPage.SetHasNavigationBar(loginPage, false);
            window.Page = new NavigationPage(loginPage);
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

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
            EditModalEntry.Text = "0";
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "Debes ingresar un valor válido.");
            return;
        }

        if (_isEditingMovement)
        {
            if (_movementTypes.Any(item => !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) && string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                EditModalEntry.Text = "0";
                await StyledResultModalPage.ShowAsync(this, false, "Dato duplicado", "Esa sección ya existe.");
                return;
            }

            ReplaceItem(_movementTypes, _editingOriginalValue, edited);
            _selectedMovement = edited;
        }
        else
        {
            if (_paymentMethods.Any(item => !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) && string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                EditModalEntry.Text = "0";
                await StyledResultModalPage.ShowAsync(this, false, "Dato duplicado", "Ese medio de pago ya existe.");
                return;
            }

            ReplaceItem(_paymentMethods, _editingOriginalValue, edited);
            _selectedPayment = edited;
        }

        EditOverlay.IsVisible = false;
        _editingOriginalValue = null;
        await SaveCatalogsAsync();
    }

    private void ResetSettingsInputs()
    {
        NewMovementEntry.Text = "0";
        NewPaymentEntry.Text = "0";
        if (EditOverlay.IsVisible)
        {
            EditModalEntry.Text = "0";
        }
    }
}
