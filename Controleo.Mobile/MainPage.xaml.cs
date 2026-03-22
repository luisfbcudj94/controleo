using Controleo.Mobile.Models;
using Controleo.Mobile.Services;
using System.Globalization;

namespace Controleo.Mobile;

public partial class MainPage : ContentPage
{
	private readonly ExpenseApiClient _apiClient;
	private readonly MonthContextService _monthContext;
	private bool _isRefreshing;
	private bool _isMonthPickerSyncing;

	public MainPage(ExpenseApiClient apiClient, MonthContextService monthContext)
	{
		InitializeComponent();
		_apiClient = apiClient;
		_monthContext = monthContext;
		RefreshMonthPickerItems();
		_monthContext.MonthChanged += OnMonthChanged;
		_monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
		SyncMonthSelection();
		ApplyDateBoundsForSelectedMonth();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshMonthOptionsAsync();
		await LoadCatalogsAsync();
	}

	private async Task RefreshMonthOptionsAsync()
	{
		var monthKeys = await _apiClient.GetAvailableMonthsAsync(CancellationToken.None);
		_monthContext.SetAvailableMonths(monthKeys);
		RefreshMonthPickerItems();
		SyncMonthSelection();
	}

	private async Task LoadCatalogsAsync()
	{
		if (_isRefreshing)
		{
			return;
		}

		try
		{
			_isRefreshing = true;
			SetLoading(true);
			StatusLabel.Text = "Cargando catálogos...";
			var selectedMovement = MovementTypePicker.SelectedItem?.ToString();
			var selectedPayment = PaymentMethodPicker.SelectedItem?.ToString();

			var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);

			var movements = catalog.MovementTypes.ToList();
			var payments = catalog.PaymentMethods.ToList();

			MovementTypePicker.ItemsSource = movements;
			PaymentMethodPicker.ItemsSource = payments;

			if (!string.IsNullOrWhiteSpace(selectedMovement) && movements.Contains(selectedMovement))
			{
				MovementTypePicker.SelectedItem = selectedMovement;
			}
			else if (movements.Count > 0)
			{
				MovementTypePicker.SelectedIndex = 0;
			}

			if (!string.IsNullOrWhiteSpace(selectedPayment) && payments.Contains(selectedPayment))
			{
				PaymentMethodPicker.SelectedItem = selectedPayment;
			}
			else if (payments.Count > 0)
			{
				PaymentMethodPicker.SelectedIndex = 0;
			}

			StatusLabel.Text = "Listo para registrar.";
		}
		finally
		{
			_isRefreshing = false;
			MainRefreshView.IsRefreshing = false;
			SetLoading(false);
		}
	}

	private async void OnRefreshing(object? sender, EventArgs e)
	{
		await LoadCatalogsAsync();
	}

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		if (!ValidateForm(out var amount, out var errorMessage))
		{
			StatusLabel.Text = errorMessage;
			return;
		}

		SaveButton.IsEnabled = false;
		SetLoading(true);
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
			ApplyDateBoundsForSelectedMonth();
			await RefreshMonthOptionsAsync();
		}

		SaveButton.IsEnabled = true;
		SetLoading(false);
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

		if (amount == 0)
		{
			errorMessage = "El valor debe ser diferente de cero.";
			return false;
		}

		var selectedDate = DateOnly.FromDateTime(DatePickerField.Date);
		if (selectedDate.Year != _monthContext.SelectedMonth.Year || selectedDate.Month != _monthContext.SelectedMonth.Month)
		{
			errorMessage = "La fecha debe pertenecer al mes seleccionado.";
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
		MainThread.BeginInvokeOnMainThread(() =>
		{
			SyncMonthSelection();
			HeaderMonthBadgeLabel.Text = month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void OnMonthOptionsChanged(object? sender, EventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			RefreshMonthPickerItems();
			SyncMonthSelection();
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void RefreshMonthPickerItems()
	{
		MonthPicker.ItemsSource = _monthContext.MonthOptions.ToList();
		MonthSelectorLabel.Text = _monthContext.MonthOptions.FirstOrDefault(item => item.Value == _monthContext.SelectedMonth)?.Label
			?? _monthContext.SelectedMonth.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-CO"));
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
		HeaderMonthBadgeLabel.Text = _monthContext.SelectedMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
		MonthSelectorLabel.Text = selected.Label;

		if (MovementTypePicker.SelectedItem is string movement)
		{
			MovementTypeSelectorLabel.Text = movement;
		}

		if (PaymentMethodPicker.SelectedItem is string payment)
		{
			PaymentMethodSelectorLabel.Text = payment;
		}
	}

	private void SetLoading(bool isLoading)
	{
		LoadingOverlay.IsVisible = isLoading;
	}

	private void ApplyDateBoundsForSelectedMonth()
	{
		var month = _monthContext.SelectedMonth;
		var minDate = new DateTime(month.Year, month.Month, 1);
		var maxDate = new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

		DatePickerField.MinimumDate = minDate;
		DatePickerField.MaximumDate = maxDate;

		var current = DatePickerField.Date;
		if (current < minDate || current > maxDate)
		{
			DatePickerField.Date = minDate;
		}
	}

	private async void OnMonthSelectorTapped(object? sender, TappedEventArgs e)
	{
		var options = _monthContext.MonthOptions.ToList();
		var labels = options.Select(item => item.Label).ToList();
		var current = options.FirstOrDefault(item => item.Value == _monthContext.SelectedMonth)?.Label;
		var selectedLabel = await StyledSelectorModalPage.PickAsync(this, "Selecciona mes", labels, current);
		if (string.IsNullOrWhiteSpace(selectedLabel))
		{
			return;
		}

		var selectedOption = options.FirstOrDefault(item => string.Equals(item.Label, selectedLabel, StringComparison.Ordinal));
		if (selectedOption is not null)
		{
			_monthContext.SetMonth(selectedOption.Value);
		}
	}

	private async void OnMovementTypeSelectorTapped(object? sender, TappedEventArgs e)
	{
		var options = MovementTypePicker.ItemsSource?.Cast<string>().ToList() ?? [];
		if (options.Count == 0)
		{
			return;
		}

		var current = MovementTypePicker.SelectedItem?.ToString();
		var selected = await StyledSelectorModalPage.PickAsync(this, "Selecciona tipo", options, current);
		if (string.IsNullOrWhiteSpace(selected))
		{
			return;
		}

		MovementTypePicker.SelectedItem = selected;
		MovementTypeSelectorLabel.Text = selected;
	}

	private async void OnPaymentMethodSelectorTapped(object? sender, TappedEventArgs e)
	{
		var options = PaymentMethodPicker.ItemsSource?.Cast<string>().ToList() ?? [];
		if (options.Count == 0)
		{
			return;
		}

		var current = PaymentMethodPicker.SelectedItem?.ToString();
		var selected = await StyledSelectorModalPage.PickAsync(this, "Selecciona medio de pago", options, current);
		if (string.IsNullOrWhiteSpace(selected))
		{
			return;
		}

		PaymentMethodPicker.SelectedItem = selected;
		PaymentMethodSelectorLabel.Text = selected;
	}
}
