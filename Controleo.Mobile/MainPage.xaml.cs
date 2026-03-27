using Controleo.Mobile.Models;
using Controleo.Mobile.Services;
using System.Globalization;

namespace Controleo.Mobile;

public partial class MainPage : ContentPage
{
	private const decimal MaxAllowedAmount = 10_000_000_000m;
	private const int MaxAllowedAmountDigits = 11;
	private const int MaxDescriptionLength = 100;

	private readonly ExpenseApiClient _apiClient;
	private readonly MonthContextService _monthContext;
	private bool _isRefreshing;
	private bool _isFormattingAmount;
	private DateOnly _selectedExpenseDate = DateOnly.FromDateTime(DateTime.Today);
	private DateOnly _minExpenseDate;
	private DateOnly _maxExpenseDate;

	public MainPage(ExpenseApiClient apiClient, MonthContextService monthContext)
	{
		InitializeComponent();
		_apiClient = apiClient;
		_monthContext = monthContext;
		_monthContext.MonthChanged += OnMonthChanged;
		_monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
		HeaderMonthBadgeLabel.Text = _monthContext.SelectedMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
		ApplyDateBoundsForSelectedMonth();
		UpdateDateSelectorLabel();
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
		HeaderMonthBadgeLabel.Text = _monthContext.SelectedMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
		ApplyDateBoundsForSelectedMonth();
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
				MovementTypeSelectorLabel.Text = selectedMovement;
			}
			else if (movements.Count > 0)
			{
				MovementTypePicker.SelectedIndex = 0;
				MovementTypeSelectorLabel.Text = movements[0];
			}

			if (!string.IsNullOrWhiteSpace(selectedPayment) && payments.Contains(selectedPayment))
			{
				PaymentMethodPicker.SelectedItem = selectedPayment;
				PaymentMethodSelectorLabel.Text = selectedPayment;
			}
			else if (payments.Count > 0)
			{
				PaymentMethodPicker.SelectedIndex = 0;
				PaymentMethodSelectorLabel.Text = payments[0];
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
			ResetMainInputs();
			await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", errorMessage);
			return;
		}

		SaveButton.IsEnabled = false;
		SetLoading(true);
		StatusLabel.Text = "Guardando...";

		var request = new ExpenseEntryRequest(
			_selectedExpenseDate,
			DescriptionField.Text!.Trim(),
			amount,
			MovementTypePicker.SelectedItem!.ToString()!,
			PaymentMethodPicker.SelectedItem!.ToString()!);

		var result = await _apiClient.SaveExpenseAsync(request, CancellationToken.None);
		await StyledResultModalPage.ShowAsync(
			this,
			result.IsSuccess,
			result.IsSuccess ? "Gasto guardado" : "No se pudo guardar",
			result.IsSuccess ? "El gasto se registró correctamente." : result.Message);

		if (result.IsSuccess)
		{
			DescriptionField.Text = string.Empty;
			AmountField.Text = string.Empty;
			ApplyDateBoundsForSelectedMonth();
			await RefreshMonthOptionsAsync();
		}
		else
		{
			ResetMainInputs();
		}

		SaveButton.IsEnabled = true;
		SetLoading(false);
	}

	private void ResetMainInputs()
	{
		DescriptionField.Text = "0";
		AmountField.Text = "0";
	}

	private bool ValidateForm(out decimal amount, out string errorMessage)
	{
		amount = 0;
		errorMessage = string.Empty;

		var description = DescriptionField.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(description))
		{
			errorMessage = "La descripción es requerida.";
			return false;
		}

		if (description.Length > MaxDescriptionLength)
		{
			errorMessage = $"La descripción no puede superar {MaxDescriptionLength} caracteres.";
			return false;
		}

		if (!TryParseAmount(AmountField.Text, out amount))
		{
			errorMessage = "El valor debe ser numérico.";
			return false;
		}

		if (amount == 0)
		{
			errorMessage = "El valor debe ser diferente de cero.";
			return false;
		}

		if (amount > MaxAllowedAmount)
		{
			errorMessage = "El valor máximo permitido es 10.000.000.000.";
			return false;
		}

		if (_selectedExpenseDate.Year != _monthContext.SelectedMonth.Year || _selectedExpenseDate.Month != _monthContext.SelectedMonth.Month)
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

	private void OnMonthChanged(object? sender, DateOnly month)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			HeaderMonthBadgeLabel.Text = month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void OnMonthOptionsChanged(object? sender, EventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			HeaderMonthBadgeLabel.Text = _monthContext.SelectedMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void SetLoading(bool isLoading)
	{
		LoadingOverlay.IsVisible = isLoading;
	}

	private void ApplyDateBoundsForSelectedMonth()
	{
		var month = _monthContext.SelectedMonth;
		_minExpenseDate = new DateOnly(month.Year, month.Month, 1);
		_maxExpenseDate = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

		if (_selectedExpenseDate < _minExpenseDate || _selectedExpenseDate > _maxExpenseDate)
		{
			_selectedExpenseDate = _minExpenseDate;
		}

		UpdateDateSelectorLabel();
	}

	private async void OnDateSelectorTapped(object? sender, TappedEventArgs e)
	{
		var selectedDate = await CalendarDateModalPage.PickAsync(
			this,
			"Fecha del gasto",
			_selectedExpenseDate,
			_minExpenseDate,
			_maxExpenseDate);

		if (selectedDate is null)
		{
			return;
		}

		_selectedExpenseDate = selectedDate.Value;
		UpdateDateSelectorLabel();
	}

	private void UpdateDateSelectorLabel()
	{
		DateSelectorLabel.Text = _selectedExpenseDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
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

	private void OnAmountFieldTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (_isFormattingAmount)
		{
			return;
		}

		try
		{
			var raw = e.NewTextValue ?? string.Empty;
			if (string.IsNullOrEmpty(raw))
			{
				return;
			}

			var digits = new string(raw.Where(char.IsDigit).ToArray());
			if (string.IsNullOrWhiteSpace(digits))
			{
				Dispatcher.Dispatch(() =>
				{
					if (_isFormattingAmount)
					{
						return;
					}

					_isFormattingAmount = true;
					try
					{
						AmountField.Text = string.Empty;
					}
					finally
					{
						_isFormattingAmount = false;
					}
				});
				return;
			}

			if (digits.Length > MaxAllowedAmountDigits)
			{
				digits = digits[..MaxAllowedAmountDigits];
			}

			if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDigits)
				&& parsedDigits > MaxAllowedAmount)
			{
				digits = ((long)MaxAllowedAmount).ToString(CultureInfo.InvariantCulture);
			}

			var formatted = FormatThousandsWithDots(digits);
			if (string.Equals(formatted, raw, StringComparison.Ordinal))
			{
				return;
			}

			Dispatcher.Dispatch(() =>
			{
				if (_isFormattingAmount)
				{
					return;
				}

				var currentText = AmountField.Text ?? string.Empty;
				if (string.Equals(currentText, formatted, StringComparison.Ordinal))
				{
					return;
				}

				_isFormattingAmount = true;
				try
				{
					AmountField.Text = formatted;
				}
				finally
				{
					_isFormattingAmount = false;
				}
			});
		}
		catch
		{
		}
	}

	private static bool TryParseAmount(string? text, out decimal amount)
	{
		amount = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
		if (!string.IsNullOrWhiteSpace(digitsOnly) && decimal.TryParse(digitsOnly, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDigits))
		{
			amount = parsedDigits;
			return true;
		}

		return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
			|| decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount);
	}

	private static string FormatThousandsWithDots(string digits)
	{
		if (string.IsNullOrWhiteSpace(digits))
		{
			return string.Empty;
		}

		var cleanDigits = digits.TrimStart('0');
		if (string.IsNullOrEmpty(cleanDigits))
		{
			cleanDigits = "0";
		}

		var chars = new List<char>(cleanDigits.Length + (cleanDigits.Length / 3));
		var count = 0;
		for (var index = cleanDigits.Length - 1; index >= 0; index--)
		{
			chars.Add(cleanDigits[index]);
			count++;
			if (count % 3 == 0 && index > 0)
			{
				chars.Add('.');
			}
		}

		chars.Reverse();
		return new string(chars.ToArray());
	}
}
