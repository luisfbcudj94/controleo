using Controleo.Mobile.Models;
using Controleo.Mobile.Services;
using System.Globalization;

namespace Controleo.Mobile;

public partial class MainPage : ContentPage
{
	private readonly ExpenseApiClient _apiClient;
	private bool _isLoaded;

	public MainPage(ExpenseApiClient apiClient)
	{
		InitializeComponent();
		_apiClient = apiClient;
		DatePickerField.Date = DateTime.Today;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (_isLoaded)
		{
			return;
		}

		StatusLabel.Text = "Cargando catálogos...";
		var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);

		MovementTypePicker.ItemsSource = catalog.MovementTypes.ToList();
		PaymentMethodPicker.ItemsSource = catalog.PaymentMethods.ToList();

		if (MovementTypePicker.ItemsSource.Count > 0)
		{
			MovementTypePicker.SelectedIndex = 0;
		}

		if (PaymentMethodPicker.ItemsSource.Count > 0)
		{
			PaymentMethodPicker.SelectedIndex = 0;
		}

		StatusLabel.Text = "Listo para registrar.";
		_isLoaded = true;
	}

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		if (!ValidateForm(out var amount, out var errorMessage))
		{
			StatusLabel.Text = errorMessage;
			return;
		}

		SaveButton.IsEnabled = false;
		StatusLabel.Text = "Guardando...";

		var request = new ExpenseEntryRequest(
			DateOnly.FromDateTime(DatePickerField.Date),
			DescriptionField.Text!.Trim(),
			amount,
			MovementTypePicker.SelectedItem!.ToString()!,
			PaymentMethodPicker.SelectedItem!.ToString()!);

		var result = await _apiClient.SaveExpenseAsync(request, CancellationToken.None);
		StatusLabel.Text = result.Message;

		if (result.IsSuccess)
		{
			DescriptionField.Text = string.Empty;
			AmountField.Text = string.Empty;
			DatePickerField.Date = DateTime.Today;
		}

		SaveButton.IsEnabled = true;
	}

	private bool ValidateForm(out decimal amount, out string errorMessage)
	{
		amount = 0;
		errorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(DescriptionField.Text))
		{
			errorMessage = "La descripción es requerida.";
			return false;
		}

		if (!decimal.TryParse(AmountField.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
		    !decimal.TryParse(AmountField.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount))
		{
			errorMessage = "El valor debe ser numérico.";
			return false;
		}

		if (amount <= 0)
		{
			errorMessage = "El valor debe ser mayor a cero.";
			return false;
		}

		if (MovementTypePicker.SelectedItem is null)
		{
			errorMessage = "Selecciona un tipo de movimiento.";
			return false;
		}

		if (PaymentMethodPicker.SelectedItem is null)
		{
			errorMessage = "Selecciona un medio de pago.";
			return false;
		}

		return true;
	}

	private async void OnGoToExpensesClicked(object? sender, EventArgs e)
	{
		var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (rootPage is TabbedPage tabbedPage && tabbedPage.Children.Count > 1)
		{
			tabbedPage.CurrentPage = tabbedPage.Children[1];
			return;
		}

		await DisplayAlert("Navegación", "No fue posible abrir la lista de gastos.", "OK");
	}
}
