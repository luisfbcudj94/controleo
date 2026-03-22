using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class SettingsPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly ObservableCollection<string> _movementTypes = [];
    private readonly ObservableCollection<string> _paymentMethods = [];
    private string? _selectedMovement;
    private string? _selectedPayment;
    private bool _loaded;

    public SettingsPage(ExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        MovementCollection.ItemsSource = _movementTypes;
        PaymentCollection.ItemsSource = _paymentMethods;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        await LoadDataAsync();
        _loaded = true;
    }

    private async Task LoadDataAsync()
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
        StatusLabel.Text = string.Empty;
    }

    private void OnAddMovementClicked(object? sender, EventArgs e)
    {
        AddItem(_movementTypes, NewMovementEntry, "sección");
    }

    private void OnAddPaymentClicked(object? sender, EventArgs e)
    {
        AddItem(_paymentMethods, NewPaymentEntry, "medio de pago");
    }

    private async void OnEditMovementClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMovement))
        {
            StatusLabel.Text = "Selecciona una sección para editar.";
            return;
        }

        var edited = await DisplayPromptAsync("Editar sección", "Nuevo nombre:", initialValue: _selectedMovement);
        if (string.IsNullOrWhiteSpace(edited))
        {
            return;
        }

        ReplaceItem(_movementTypes, _selectedMovement, edited.Trim());
        _selectedMovement = edited.Trim();
    }

    private async void OnEditPaymentClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedPayment))
        {
            StatusLabel.Text = "Selecciona un medio de pago para editar.";
            return;
        }

        var edited = await DisplayPromptAsync("Editar medio de pago", "Nuevo nombre:", initialValue: _selectedPayment);
        if (string.IsNullOrWhiteSpace(edited))
        {
            return;
        }

        ReplaceItem(_paymentMethods, _selectedPayment, edited.Trim());
        _selectedPayment = edited.Trim();
    }

    private void OnDeleteMovementClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMovement))
        {
            _movementTypes.Remove(_selectedMovement);
            _selectedMovement = null;
        }
    }

    private void OnDeletePaymentClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedPayment))
        {
            _paymentMethods.Remove(_selectedPayment);
            _selectedPayment = null;
        }
    }

    private async void OnSaveSettingsClicked(object? sender, EventArgs e)
    {
        if (_movementTypes.Count == 0 || _paymentMethods.Count == 0)
        {
            StatusLabel.Text = "Debe existir al menos una sección y un medio de pago.";
            return;
        }

        SaveSettingsButton.IsEnabled = false;

        var request = new UpdateCatalogsRequest(_movementTypes.ToList(), _paymentMethods.ToList());
        var result = await _apiClient.UpdateCatalogsAsync(request, CancellationToken.None);

        StatusLabel.Text = result.Message;
        SaveSettingsButton.IsEnabled = true;
    }

    private void OnMovementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedMovement = e.CurrentSelection.FirstOrDefault()?.ToString();
    }

    private void OnPaymentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedPayment = e.CurrentSelection.FirstOrDefault()?.ToString();
    }

    private void AddItem(ObservableCollection<string> source, Entry entry, string label)
    {
        var value = entry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusLabel.Text = $"Ingresa un nombre de {label}.";
            return;
        }

        if (source.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = $"Ese {label} ya existe.";
            return;
        }

        source.Add(value);
        entry.Text = string.Empty;
        StatusLabel.Text = string.Empty;
    }

    private static void ReplaceItem(ObservableCollection<string> source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue);
        if (index >= 0)
        {
            source[index] = newValue;
        }
    }
}
