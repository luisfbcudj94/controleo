using Controleo.Mobile.Models;
using Controleo.Mobile.Services;
using System.Globalization;

namespace Controleo.Mobile;

public partial class RecurringPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private string? _editingId;
    private DateOnly _selectedStartDate = DateOnly.FromDateTime(DateTime.Today);

    public RecurringPage(ExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        UpdateStartDateSelectorLabel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCatalogsAsync();
        await LoadRecurringAsync();
    }

    private async Task LoadCatalogsAsync()
    {
        var catalogs = await _apiClient.GetCatalogsAsync(CancellationToken.None);
        MovementTypePicker.ItemsSource = catalogs.MovementTypes.ToList();
        PaymentMethodPicker.ItemsSource = catalogs.PaymentMethods.ToList();

        if (MovementTypePicker.SelectedIndex < 0 && MovementTypePicker.ItemsSource.Count > 0)
        {
            MovementTypePicker.SelectedIndex = 0;
        }

        if (PaymentMethodPicker.SelectedIndex < 0 && PaymentMethodPicker.ItemsSource.Count > 0)
        {
            PaymentMethodPicker.SelectedIndex = 0;
        }
    }

    private async Task LoadRecurringAsync()
    {
        var items = await _apiClient.GetRecurringExpensesAsync(CancellationToken.None);
        RecurringCollection.ItemsSource = items;
        StatusLabel.Text = string.Empty;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (!TryBuildRequest(out var request, out var errorMessage))
        {
            ResetRecurringInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", errorMessage);
            return;
        }

        SaveButton.IsEnabled = false;
        var result = await _apiClient.SaveRecurringExpenseAsync(_editingId, request!, CancellationToken.None);
        SaveButton.IsEnabled = true;
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Recurrente guardado" : "No se pudo guardar",
            result.IsSuccess ? "El gasto recurrente se guardó correctamente." : result.Message);

        if (!result.IsSuccess)
        {
            ResetRecurringInputs();
            return;
        }

        ClearForm();
        await LoadRecurringAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: RecurringExpenseItem item })
        {
            return;
        }

        var confirm = await DisplayAlert("Eliminar", $"¿Eliminar '{item.Description}'?", "Sí", "No");
        if (!confirm)
        {
            return;
        }

        var result = await _apiClient.DeleteRecurringExpenseAsync(item.Id, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Recurrente eliminado" : "No se pudo eliminar",
            result.IsSuccess ? "El gasto recurrente se eliminó correctamente." : result.Message);
        await LoadRecurringAsync();
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: RecurringExpenseItem item })
        {
            return;
        }

        _editingId = item.Id;
        DescriptionEntry.Text = item.Description;
        AmountEntry.Text = item.Amount.ToString("0.##");
        MovementTypePicker.SelectedItem = item.MovementType;
        PaymentMethodPicker.SelectedItem = item.PaymentMethod;
        DayEntry.Text = item.DayOfMonth.ToString();
        _selectedStartDate = item.StartDate;
        UpdateStartDateSelectorLabel();
        IsActiveCheck.IsChecked = item.IsActive;
        StatusLabel.Text = string.Empty;
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        ClearForm();
    }

    private void ClearForm()
    {
        _editingId = null;
        DescriptionEntry.Text = string.Empty;
        AmountEntry.Text = string.Empty;
        DayEntry.Text = "1";
        _selectedStartDate = DateOnly.FromDateTime(DateTime.Today);
        UpdateStartDateSelectorLabel();
        IsActiveCheck.IsChecked = true;
        StatusLabel.Text = string.Empty;
    }

    private void ResetRecurringInputs()
    {
        DescriptionEntry.Text = "0";
        AmountEntry.Text = "0";
        DayEntry.Text = "0";
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

        if (!decimal.TryParse(AmountEntry.Text, out var amount) || amount <= 0)
        {
            errorMessage = "El monto debe ser mayor a cero.";
            return false;
        }

        if (!int.TryParse(DayEntry.Text, out var day) || day < 1 || day > 31)
        {
            errorMessage = "El día debe estar entre 1 y 31.";
            return false;
        }

        if (MovementTypePicker.SelectedItem is null || PaymentMethodPicker.SelectedItem is null)
        {
            errorMessage = "Debes elegir tipo y medio de pago.";
            return false;
        }

        request = new RecurringExpenseUpsertRequest(
            description,
            amount,
            MovementTypePicker.SelectedItem.ToString()!,
            PaymentMethodPicker.SelectedItem.ToString()!,
            day,
            _selectedStartDate,
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
}
