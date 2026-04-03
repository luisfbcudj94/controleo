using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using System.Globalization;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Features.Register;

public partial class MainPage : ContentPage
{
	private const decimal MaxAllowedAmount = 10_000_000_000m;
	private const int MaxAllowedAmountDigits = 11;
	private const int MaxDescriptionLength = 50;
	private const double AmountFontSizeMax = 48d;
	private const double AmountFontSizeMin = 26d;

	private readonly IExpenseApiClient _apiClient;
	private readonly IMonthContextService _monthContext;
	private readonly ICatalogColorService _colorService;
	private readonly IPaymentIconService _paymentIconService;
	private readonly List<MovementTypeConfig> _movementTypeConfigs = [];
	private readonly List<PaymentMethodConfig> _paymentMethodConfigs = [];
	private readonly Dictionary<string, Border> _movementTiles = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Border> _paymentTiles = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Border> _addCatalogIconTiles = new(StringComparer.Ordinal);
	private bool _isRefreshing;
	private bool _isFormattingAmount;
	private bool _isAnimatingCatalogSelection;
	private bool _isAnimatingAddIconSelection;
	private bool _resetOnNextAppearance;
	private double _lastGridLayoutWidth;
	private AddCatalogMode _addCatalogMode;
	private string? _selectedMovementType;
	private string? _selectedPaymentMethod;
	private string? _selectedAddCatalogIcon;
	private DateOnly _selectedExpenseDate = DateOnly.FromDateTime(DateTime.Today);
	private DateOnly _minExpenseDate;
	private DateOnly _maxExpenseDate;

	private enum AddCatalogMode
	{
		MovementType,
		PaymentMethod,
	}

	public MainPage(IExpenseApiClient apiClient, IMonthContextService monthContext, ICatalogColorService colorService, IPaymentIconService paymentIconService)
	{
		InitializeComponent();
		_apiClient = apiClient;
		_monthContext = monthContext;
		_colorService = colorService;
		_paymentIconService = paymentIconService;
		_monthContext.MonthChanged += OnMonthChanged;
		_monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
		UpdateHeaderMonthBadge();
		ApplyDateBoundsForSelectedMonth();
		UpdateDescriptionCounter();
		UpdateAmountFieldFontSize(string.Empty);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshMonthOptionsAsync();
		await LoadCatalogsAsync();

		if (_resetOnNextAppearance)
		{
			_resetOnNextAppearance = false;
			await ResetRegisterStateAsync();
		}

		UpdateDescriptionCounter();
	}

	public void QueueResetAfterTabSwitch()
	{
		_resetOnNextAppearance = true;
	}

	private async Task ResetRegisterStateAsync()
	{
		ResetMainInputs();
		ApplyDateBoundsForSelectedMonth();
		ResetSelectedExpenseDate();

		if (MovementTypePicker.ItemsSource is IEnumerable<string> movements)
		{
			var movementList = movements.ToList();
			_selectedMovementType = movementList.Count > 0 ? movementList[0] : null;
			MovementTypePicker.SelectedItem = _selectedMovementType;
			BuildMovementTypeGrid(movementList);
		}
		else
		{
			_selectedMovementType = null;
			MovementTypePicker.SelectedItem = null;
		}

		if (PaymentMethodPicker.ItemsSource is IEnumerable<string> methods)
		{
			var paymentList = methods.ToList();
			_selectedPaymentMethod = paymentList.Count > 0 ? paymentList[0] : null;
			PaymentMethodPicker.SelectedItem = _selectedPaymentMethod;
			BuildPaymentMethodGrid(paymentList);
		}
		else
		{
			_selectedPaymentMethod = null;
			PaymentMethodPicker.SelectedItem = null;
		}

		await ScrollCatalogsToStartAsync();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);
		if (width <= 0)
		{
			return;
		}

		if (Math.Abs(width - _lastGridLayoutWidth) < 8)
		{
			return;
		}

		_lastGridLayoutWidth = width;
		if (MovementTypePicker.ItemsSource is IEnumerable<string> movements)
		{
			BuildMovementTypeGrid(movements.ToList());
		}

		if (PaymentMethodPicker.ItemsSource is IEnumerable<string> methods)
		{
			BuildPaymentMethodGrid(methods.ToList());
		}
	}

	private async Task RefreshMonthOptionsAsync()
	{
		var monthKeys = await _apiClient.GetAvailableMonthsAsync(CancellationToken.None);
		_monthContext.SetAvailableMonths(monthKeys);
		var currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
		_monthContext.SetMonth(currentMonth);
		UpdateHeaderMonthBadge();
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

			var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
			_colorService.SetConfigs(catalog.MovementTypeConfigs);
			_movementTypeConfigs.Clear();
			if (catalog.MovementTypeConfigs is not null)
			{
				_movementTypeConfigs.AddRange(catalog.MovementTypeConfigs);
			}

			_paymentMethodConfigs.Clear();
			if (catalog.PaymentMethodConfigs is not null)
			{
				_paymentMethodConfigs.AddRange(catalog.PaymentMethodConfigs);
			}

			var movements = catalog.MovementTypes.ToList();
			var payments = catalog.PaymentMethods.ToList();
			foreach (var payment in payments)
			{
				var configuredIcon = _paymentMethodConfigs
					.FirstOrDefault(cfg => string.Equals(cfg.Name, payment, StringComparison.OrdinalIgnoreCase))
					?.Icon;

				if (!string.IsNullOrWhiteSpace(configuredIcon))
				{
					_paymentIconService.SetIconForPaymentMethod(payment, configuredIcon);
				}

				UpsertPaymentMethodConfig(payment, _paymentIconService.IconForPaymentMethod(payment));
			}

			_paymentMethodConfigs.RemoveAll(cfg => !payments.Any(item => string.Equals(item, cfg.Name, StringComparison.OrdinalIgnoreCase)));

			MovementTypePicker.ItemsSource = movements;
			PaymentMethodPicker.ItemsSource = payments;

			_selectedMovementType = movements.Count > 0 ? movements[0] : null;
			_selectedPaymentMethod = payments.Count > 0 ? payments[0] : null;
			MovementTypePicker.SelectedItem = _selectedMovementType;
			PaymentMethodPicker.SelectedItem = _selectedPaymentMethod;

			BuildMovementTypeGrid(movements);
			BuildPaymentMethodGrid(payments);
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

		try
		{
			var request = new ExpenseEntryRequest(
				_selectedExpenseDate,
				DescriptionField.Text!.Trim(),
				amount,
				_selectedMovementType!,
				_selectedPaymentMethod!);

			var result = await _apiClient.SaveExpenseAsync(request, CancellationToken.None);
			await StyledResultModalPage.ShowAsync(
				this,
				result.IsSuccess,
				result.IsSuccess ? "Gasto guardado" : "No se pudo guardar",
				result.IsSuccess ? "El gasto se registró correctamente." : result.Message);

			if (result.IsSuccess)
			{
				await RefreshMonthOptionsAsync();
				await ResetRegisterStateAsync();
			}
			else
			{
				ResetMainInputs();
			}
		}
		finally
		{
			SaveButton.IsEnabled = true;
			SetLoading(false);
		}
	}

	private void ResetMainInputs()
	{
		DescriptionField.Text = string.Empty;
		AmountField.Text = string.Empty;
		UpdateDescriptionCounter();
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

		if (string.IsNullOrWhiteSpace(_selectedMovementType))
		{
			errorMessage = "Selecciona un tipo de movimiento.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(_selectedPaymentMethod))
		{
			errorMessage = "Selecciona un medio de pago.";
			return false;
		}

		return true;
	}

	private async void OnGoToExpensesClicked(object? sender, EventArgs e)
	{
		var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (TryNavigateToExpensesTab(rootPage))
		{
			return;
		}

		await DisplayAlert("Navegación", "No fue posible abrir la lista de gastos.", "OK");
	}

	private static bool TryNavigateToExpensesTab(Page? rootPage)
	{
		var tabbedPage = rootPage switch
		{
			TabbedPage tabs => tabs,
			FlyoutPage flyout when flyout.Detail is TabbedPage tabs => tabs,
			_ => null
		};

		if (tabbedPage is null)
		{
			return false;
		}

		var expensesTab = tabbedPage.Children
			.OfType<NavigationPage>()
			.FirstOrDefault(page => string.Equals(page.Title, "Gastos", StringComparison.OrdinalIgnoreCase));

		if (expensesTab is null && tabbedPage.Children.Count > 1)
		{
			expensesTab = tabbedPage.Children[1] as NavigationPage;
		}

		if (expensesTab is null)
		{
			return false;
		}

		tabbedPage.CurrentPage = expensesTab;
		return true;
	}

	private void OnMonthChanged(object? sender, DateOnly month)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			UpdateHeaderMonthBadge();
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void OnMonthOptionsChanged(object? sender, EventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			UpdateHeaderMonthBadge();
			ApplyDateBoundsForSelectedMonth();
		});
	}

	private void UpdateHeaderMonthBadge()
	{
		HeaderMonthBadgeLabel.Text = _monthContext.SelectedMonth.ToString("MMM yyyy", CultureInfo.CurrentCulture);
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
			_selectedExpenseDate = DateOnly.FromDateTime(DateTime.Today);
			if (_selectedExpenseDate < _minExpenseDate || _selectedExpenseDate > _maxExpenseDate)
				_selectedExpenseDate = _maxExpenseDate;
		}
	}

	private void ResetSelectedExpenseDate()
	{
		var today = DateOnly.FromDateTime(DateTime.Today);
		_selectedExpenseDate = today >= _minExpenseDate && today <= _maxExpenseDate
			? today
			: _maxExpenseDate;
	}

	private async Task ScrollCatalogsToStartAsync()
	{
		try
		{
			await MovementTypeScroll.ScrollToAsync(0, 0, false);
			await PaymentMethodScroll.ScrollToAsync(0, 0, false);
		}
		catch
		{
		}
	}

	private void BuildMovementTypeGrid(List<string> types)
	{
		MovementTypeGrid.Children.Clear();
		_movementTiles.Clear();
		var tileSide = CalculateRegisterTileSide(GetRegisterViewportWidth());

		var addTile = BuildAddTile("+", Color.FromArgb("#EFF3F1"));
		addTile.WidthRequest = tileSide;
		addTile.HeightRequest = tileSide;
		addTile.Margin = new Thickness(5, 5, 5, 10);
		var addTap = new TapGestureRecognizer();
		addTap.Tapped += async (_, _) => await OnAddMovementTypeFromRegisterAsync();
		addTile.GestureRecognizers.Add(addTap);
		MovementTypeGrid.Children.Add(addTile);

		foreach (var type in types)
		{
			var icon = _colorService.IconForMovementType(type);
			var color = _colorService.ForMovementType(type);
			var isSelected = string.Equals(type, _selectedMovementType, StringComparison.OrdinalIgnoreCase);

			var border = new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
				BackgroundColor = color,
				StrokeThickness = 0,
				WidthRequest = tileSide,
				HeightRequest = tileSide,
				Margin = new Thickness(5, 5, 5, 10),
				AnchorX = 0.5,
				AnchorY = 0.5,
				Opacity = isSelected ? 1.0 : 0.58,
				Scale = isSelected ? 1.1 : 0.95,
			};
			ApplyRegisterTileLook(border, isSelected, isPaymentTile: false);

			var stack = new VerticalStackLayout
			{
				Spacing = 2,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
				Children =
				{
					new Label
					{
						Text = icon,
						FontSize = 22,
						HorizontalTextAlignment = TextAlignment.Center,
						HorizontalOptions = LayoutOptions.Center,
					},
					new Label
					{
						Text = ShortLabel(type, 12),
						FontSize = 8,
						FontAttributes = FontAttributes.Bold,
						HorizontalTextAlignment = TextAlignment.Center,
						HorizontalOptions = LayoutOptions.Center,
						TextColor = Color.FromArgb("#1A2E23"),
					}
				}
			};

			border.Content = stack;

			var capturedType = type;
			var tap = new TapGestureRecognizer();
			tap.Tapped += async (_, _) =>
			{
				if (_isAnimatingCatalogSelection)
				{
					return;
				}

				_selectedMovementType = capturedType;
				MovementTypePicker.SelectedItem = capturedType;
				await AnimateRegisterSelectionAsync(_movementTiles, capturedType, isPaymentTiles: false);
			};
			border.GestureRecognizers.Add(tap);
			_movementTiles[capturedType] = border;

			MovementTypeGrid.Children.Add(border);
		}
	}

	private void BuildPaymentMethodGrid(List<string> methods)
	{
		PaymentMethodGrid.Children.Clear();
		_paymentTiles.Clear();
		var tileSide = CalculateRegisterTileSide(GetRegisterViewportWidth());

		var addTile = BuildAddTile("+", Color.FromArgb("#EFF3F1"));
		addTile.WidthRequest = tileSide;
		addTile.HeightRequest = tileSide;
		addTile.Margin = new Thickness(5, 5, 5, 10);
		var addTap = new TapGestureRecognizer();
		addTap.Tapped += async (_, _) => await OnAddPaymentMethodFromRegisterAsync();
		addTile.GestureRecognizers.Add(addTap);
		PaymentMethodGrid.Children.Add(addTile);

		foreach (var method in methods)
		{
			var isSelected = string.Equals(method, _selectedPaymentMethod, StringComparison.OrdinalIgnoreCase);

			var border = new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
				BackgroundColor = Color.FromArgb("#EFF3F1"),
				StrokeThickness = 0,
				WidthRequest = tileSide,
				HeightRequest = tileSide,
				Margin = new Thickness(5, 5, 5, 10),
				AnchorX = 0.5,
				AnchorY = 0.5,
				Opacity = isSelected ? 1.0 : 0.58,
				Scale = isSelected ? 1.1 : 0.95,
			};
			ApplyRegisterTileLook(border, isSelected, isPaymentTile: true);

			var icon = _paymentIconService.IconForPaymentMethod(method);
			var stack = new VerticalStackLayout
			{
				Spacing = 2,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
				Children =
				{
					new Label
					{
						Text = icon,
						FontSize = 21,
						HorizontalTextAlignment = TextAlignment.Center,
						HorizontalOptions = LayoutOptions.Center,
						TextColor = isSelected ? Colors.White : Color.FromArgb("#1A2E23"),
					},
					new Label
					{
						Text = ShortLabel(method, 12),
						FontSize = 8,
						FontAttributes = FontAttributes.Bold,
						HorizontalTextAlignment = TextAlignment.Center,
						HorizontalOptions = LayoutOptions.Center,
						TextColor = isSelected ? Colors.White : Color.FromArgb("#1A2E23"),
					}
				}
			};

			border.Content = stack;

			var capturedMethod = method;
			var tap = new TapGestureRecognizer();
			tap.Tapped += async (_, _) =>
			{
				if (_isAnimatingCatalogSelection)
				{
					return;
				}

				_selectedPaymentMethod = capturedMethod;
				PaymentMethodPicker.SelectedItem = capturedMethod;
				await AnimateRegisterSelectionAsync(_paymentTiles, capturedMethod, isPaymentTiles: true);
			};
			border.GestureRecognizers.Add(tap);
			_paymentTiles[capturedMethod] = border;

			PaymentMethodGrid.Children.Add(border);
		}
	}

	private Border BuildAddTile(string icon, Color backgroundColor)
	{
		var tile = new Border
		{
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
			BackgroundColor = backgroundColor,
			StrokeThickness = 0,
			WidthRequest = 72,
			HeightRequest = 72,
			Margin = new Thickness(4, 4, 4, 8),
			AnchorX = 0.5,
			AnchorY = 0.5,
		};

		tile.Content = new VerticalStackLayout
		{
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			Spacing = 2,
			Children =
			{
				new Label
				{
					Text = icon,
					FontSize = 28,
					HorizontalTextAlignment = TextAlignment.Center,
					HorizontalOptions = LayoutOptions.Center,
					TextColor = Color.FromArgb("#1A2E23"),
				},
				new Label
				{
					Text = "Agregar",
					FontSize = 9,
					FontAttributes = FontAttributes.Bold,
					HorizontalTextAlignment = TextAlignment.Center,
					HorizontalOptions = LayoutOptions.Center,
					TextColor = Color.FromArgb("#1A2E23"),
				}
			}
		};

		return tile;
	}

	private async Task OnAddMovementTypeFromRegisterAsync()
	{
		OpenAddCatalogModal(AddCatalogMode.MovementType);
		await Task.CompletedTask;
	}

	private async Task OnAddPaymentMethodFromRegisterAsync()
	{
		OpenAddCatalogModal(AddCatalogMode.PaymentMethod);
		await Task.CompletedTask;
	}

	private void OpenAddCatalogModal(AddCatalogMode mode)
	{
		_addCatalogMode = mode;
		AddCatalogTitleLabel.Text = mode == AddCatalogMode.MovementType ? "Nueva categoría" : "Nuevo método de pago";
		AddCatalogNameEntry.Placeholder = mode == AddCatalogMode.MovementType ? "Ej: Mascotas" : "Ej: Daviplata";
		AddCatalogNameEntry.Text = string.Empty;
		_selectedAddCatalogIcon = mode == AddCatalogMode.MovementType
			? _colorService.AvailableIcons[0].Emoji
			: _paymentIconService.AvailableIcons[0].Emoji;

		var sourceIcons = mode == AddCatalogMode.MovementType
			? _colorService.AvailableIcons
			: _paymentIconService.AvailableIcons;
		BuildAddCatalogIconGrid(sourceIcons);

		AddCatalogOverlay.IsVisible = true;
		MainThread.BeginInvokeOnMainThread(() => AddCatalogNameEntry.Focus());
	}

	private void BuildAddCatalogIconGrid(IReadOnlyList<(string Emoji, string Label)> icons)
	{
		AddCatalogIconGrid.Children.Clear();
		_addCatalogIconTiles.Clear();
		var tileSide = CalculateSquareTileSide(AddCatalogIconGrid.Width, 5, 8, 20);
		foreach (var option in icons)
		{
			var isSelected = string.Equals(option.Emoji, _selectedAddCatalogIcon, StringComparison.Ordinal);
			var tile = new Border
			{
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
				BackgroundColor = isSelected ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#EFF3F1"),
				StrokeThickness = 0,
				WidthRequest = tileSide,
				HeightRequest = tileSide,
				Margin = new Thickness(4, 4, 4, 8),
				AnchorX = 0.5,
				AnchorY = 0.5,
				Opacity = isSelected ? 1.0 : 0.6,
				Scale = isSelected ? 1.12 : 0.95,
			};

			var iconLabel = new Label
			{
				Text = option.Emoji,
				FontSize = 28,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
				HorizontalOptions = LayoutOptions.Center,
				VerticalOptions = LayoutOptions.Center,
				TextColor = isSelected ? Colors.White : Color.FromArgb("#1A2E23"),
			};
			tile.Content = iconLabel;
			tile.Shadow = isSelected
				? new Shadow { Brush = Colors.Black, Offset = new Point(0, 3), Radius = 10, Opacity = 0.2f }
				: null;

			var emoji = option.Emoji;
			var tap = new TapGestureRecognizer();
			tap.Tapped += async (_, _) =>
			{
				if (_isAnimatingAddIconSelection)
				{
					return;
				}

				_selectedAddCatalogIcon = emoji;
				await AnimateAddCatalogIconSelectionAsync(emoji);
			};
			tile.GestureRecognizers.Add(tap);
			_addCatalogIconTiles[emoji] = tile;

			AddCatalogIconGrid.Children.Add(tile);
		}
	}

	private async void OnAddCatalogConfirmClicked(object? sender, EventArgs e)
	{
		var name = AddCatalogNameEntry.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(name))
		{
			await StyledResultModalPage.ShowAsync(this, false, "Dato faltante", "Debes escribir un nombre.");
			return;
		}

		var selectedIcon = _selectedAddCatalogIcon;
		if (string.IsNullOrWhiteSpace(selectedIcon))
		{
			selectedIcon = _addCatalogMode == AddCatalogMode.MovementType
				? _colorService.AvailableIcons[0].Emoji
				: _paymentIconService.AvailableIcons[0].Emoji;
		}

		if (_addCatalogMode == AddCatalogMode.MovementType)
		{
			var currentMovements = (MovementTypePicker.ItemsSource?.Cast<string>().ToList() ?? []);
			if (currentMovements.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
			{
				await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Esa categoría ya existe.");
				return;
			}

			var availableColors = _colorService.GetAvailableColors(_movementTypeConfigs);
			if (availableColors.Count == 0)
			{
				await StyledResultModalPage.ShowAsync(this, false, "Sin colores", "No hay más colores disponibles para categorías.");
				return;
			}

			currentMovements.Add(name);
			_movementTypeConfigs.Add(new MovementTypeConfig(name, selectedIcon, availableColors[0].Hex));
			var saved = await SaveCatalogsFromRegisterAsync(currentMovements, PaymentMethodPicker.ItemsSource?.Cast<string>().ToList() ?? []);
			if (saved)
			{
				CloseAddCatalogModal();
			}
		}
		else
		{
			var currentPayments = (PaymentMethodPicker.ItemsSource?.Cast<string>().ToList() ?? []);
			if (currentPayments.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
			{
				await StyledResultModalPage.ShowAsync(this, false, "Duplicado", "Ese medio de pago ya existe.");
				return;
			}

			currentPayments.Add(name);
			_paymentIconService.SetIconForPaymentMethod(name, selectedIcon);
			UpsertPaymentMethodConfig(name, selectedIcon);
			var saved = await SaveCatalogsFromRegisterAsync(MovementTypePicker.ItemsSource?.Cast<string>().ToList() ?? [], currentPayments);
			if (saved)
			{
				CloseAddCatalogModal();
			}
		}
	}

	private void OnAddCatalogCancelClicked(object? sender, EventArgs e)
	{
		CloseAddCatalogModal();
	}

	private void CloseAddCatalogModal()
	{
		_selectedAddCatalogIcon = null;
		AddCatalogNameEntry.Text = string.Empty;
		AddCatalogOverlay.IsVisible = false;
	}

	private void ApplyRegisterTileLook(Border tile, bool isSelected, bool isPaymentTile)
	{
		tile.Shadow = isSelected
			? new Shadow { Brush = Colors.Black, Offset = new Point(0, 4), Radius = 12, Opacity = 0.2f }
			: null;

		if (!isPaymentTile)
		{
			return;
		}

		tile.BackgroundColor = isSelected ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#EFF3F1");
		if (tile.Content is VerticalStackLayout paymentStack)
		{
			foreach (var label in paymentStack.Children.OfType<Label>())
			{
				label.TextColor = isSelected ? Colors.White : Color.FromArgb("#1A2E23");
			}
		}
	}

	private async Task AnimateRegisterSelectionAsync(
		Dictionary<string, Border> tileMap,
		string selectedKey,
		bool isPaymentTiles)
	{
		if (_isAnimatingCatalogSelection)
		{
			return;
		}

		_isAnimatingCatalogSelection = true;
		try
		{
			var animationTasks = new List<Task>(tileMap.Count * 2);
			foreach (var (key, tile) in tileMap)
			{
				var isSelected = string.Equals(key, selectedKey, StringComparison.OrdinalIgnoreCase);
				ApplyRegisterTileLook(tile, isSelected, isPaymentTiles);
				tile.AnchorX = 0.5;
				tile.AnchorY = 0.5;

				if (isSelected)
				{
					animationTasks.Add(tile.ScaleTo(1.2, 105, Easing.CubicOut));
					animationTasks.Add(tile.FadeTo(1.0, 105, Easing.CubicOut));
				}
				else
				{
					animationTasks.Add(tile.ScaleTo(0.92, 100, Easing.CubicOut));
					animationTasks.Add(tile.FadeTo(0.52, 100, Easing.CubicOut));
				}
			}

			await Task.WhenAll(animationTasks);

			if (tileMap.TryGetValue(selectedKey, out var selectedTile))
			{
				await selectedTile.ScaleTo(1.1, 120, Easing.CubicInOut);
			}
		}
		catch
		{
			// Ignore animation races during fast taps.
		}
		finally
		{
			_isAnimatingCatalogSelection = false;
		}
	}

	private async Task AnimateAddCatalogIconSelectionAsync(string selectedEmoji)
	{
		if (_isAnimatingAddIconSelection)
		{
			return;
		}

		_isAnimatingAddIconSelection = true;
		try
		{
			var animationTasks = new List<Task>(_addCatalogIconTiles.Count * 2);
			foreach (var (emoji, tile) in _addCatalogIconTiles)
			{
				var isSelected = string.Equals(emoji, selectedEmoji, StringComparison.Ordinal);
				tile.BackgroundColor = isSelected ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#EFF3F1");
				tile.Shadow = isSelected
					? new Shadow { Brush = Colors.Black, Offset = new Point(0, 3), Radius = 10, Opacity = 0.2f }
					: null;

				if (tile.Content is Label iconLabel)
				{
					iconLabel.TextColor = isSelected ? Colors.White : Color.FromArgb("#1A2E23");
				}

				tile.AnchorX = 0.5;
				tile.AnchorY = 0.5;

				if (isSelected)
				{
					animationTasks.Add(tile.ScaleTo(1.22, 105, Easing.CubicOut));
					animationTasks.Add(tile.FadeTo(1.0, 105, Easing.CubicOut));
				}
				else
				{
					animationTasks.Add(tile.ScaleTo(0.92, 100, Easing.CubicOut));
					animationTasks.Add(tile.FadeTo(0.55, 100, Easing.CubicOut));
				}
			}

			await Task.WhenAll(animationTasks);

			if (_addCatalogIconTiles.TryGetValue(selectedEmoji, out var selectedTile))
			{
				await selectedTile.ScaleTo(1.12, 120, Easing.CubicInOut);
			}
		}
		catch
		{
			// Ignore animation races during fast taps.
		}
		finally
		{
			_isAnimatingAddIconSelection = false;
		}
	}

	private double CalculateSquareTileSide(double targetGridWidth, int columns, double spacing, double horizontalOuterPadding = 0)
	{
		var pageWidth = Width > 0 ? Width : DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
		var availableWidth = targetGridWidth > 0 ? targetGridWidth : Math.Max(300d, pageWidth - 46d);
		availableWidth = Math.Max(220d, availableWidth - horizontalOuterPadding);
		var totalSpacing = spacing * Math.Max(0, columns - 1);
		var side = Math.Floor((availableWidth - totalSpacing) / columns);
		return Math.Max(56d, side);
	}

	private double CalculateRegisterTileSide(double targetGridWidth)
	{
		var baseSide = CalculateSquareTileSide(targetGridWidth, 4, 12, 20);
		return Math.Max(56d, baseSide - 8d);
	}

	private double GetRegisterViewportWidth()
	{
		var pageWidth = Width > 0 ? Width : DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
		return Math.Max(260d, pageWidth - 40d);
	}

	private static string ShortLabel(string text, int maxLen)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		if (text.Length <= maxLen)
		{
			return text;
		}

		if (maxLen <= 3)
		{
			return text[..maxLen];
		}

		return text[..(maxLen - 3)] + "...";
	}

	private async Task<bool> SaveCatalogsFromRegisterAsync(List<string> movementTypes, List<string> paymentMethods)
	{
		if (movementTypes.Count == 0 || paymentMethods.Count == 0)
		{
			await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "Debe existir al menos una categoría y un medio de pago.");
			return false;
		}

		SetLoading(true);
		try
		{
			_paymentMethodConfigs.RemoveAll(cfg => !paymentMethods.Any(item => string.Equals(item, cfg.Name, StringComparison.OrdinalIgnoreCase)));
			foreach (var paymentMethod in paymentMethods)
			{
				UpsertPaymentMethodConfig(paymentMethod, _paymentIconService.IconForPaymentMethod(paymentMethod));
			}

			var request = new UpdateCatalogsRequest(movementTypes, paymentMethods, _movementTypeConfigs.ToList(), _paymentMethodConfigs.ToList());
			var result = await _apiClient.UpdateCatalogsAsync(request, CancellationToken.None);
			if (!result.IsSuccess)
			{
				await StyledResultModalPage.ShowAsync(this, false, "Error", result.Message);
				return false;
			}

			MovementTypePicker.ItemsSource = movementTypes;
			PaymentMethodPicker.ItemsSource = paymentMethods;
			if (!string.IsNullOrWhiteSpace(_selectedMovementType) && movementTypes.Contains(_selectedMovementType))
			{
				MovementTypePicker.SelectedItem = _selectedMovementType;
			}
			else
			{
				_selectedMovementType = movementTypes[0];
				MovementTypePicker.SelectedItem = _selectedMovementType;
			}

			if (!string.IsNullOrWhiteSpace(_selectedPaymentMethod) && paymentMethods.Contains(_selectedPaymentMethod))
			{
				PaymentMethodPicker.SelectedItem = _selectedPaymentMethod;
			}
			else
			{
				_selectedPaymentMethod = paymentMethods[0];
				PaymentMethodPicker.SelectedItem = _selectedPaymentMethod;
			}

			BuildMovementTypeGrid(movementTypes);
			BuildPaymentMethodGrid(paymentMethods);
			return true;
		}
		finally
		{
			SetLoading(false);
		}
	}

	private void UpsertPaymentMethodConfig(string paymentMethod, string icon)
	{
		if (string.IsNullOrWhiteSpace(paymentMethod) || string.IsNullOrWhiteSpace(icon))
		{
			return;
		}

		_paymentMethodConfigs.RemoveAll(cfg => string.Equals(cfg.Name, paymentMethod, StringComparison.OrdinalIgnoreCase));
		_paymentMethodConfigs.Add(new PaymentMethodConfig(paymentMethod.Trim(), icon.Trim()));
	}

	private void OnDescriptionFieldTextChanged(object? sender, TextChangedEventArgs e)
	{
		var incoming = e.NewTextValue ?? string.Empty;
		if (incoming.Length > MaxDescriptionLength)
		{
			DescriptionField.Text = incoming[..MaxDescriptionLength];
			return;
		}

		UpdateDescriptionCounter();
	}

	private void UpdateDescriptionCounter()
	{
		var length = DescriptionField.Text?.Length ?? 0;
		if (length > MaxDescriptionLength)
		{
			length = MaxDescriptionLength;
		}

		DescriptionCounterLabel.Text = $"{length}/{MaxDescriptionLength}";
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
				UpdateAmountFieldFontSize(string.Empty);
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
						UpdateAmountFieldFontSize(string.Empty);
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
				UpdateAmountFieldFontSize(formatted);
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
					UpdateAmountFieldFontSize(formatted);
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

	private void UpdateAmountFieldFontSize(string value)
	{
		var length = (value ?? string.Empty).Length;
		double targetSize;

		if (length <= 1)
		{
			targetSize = AmountFontSizeMax;
		}
		else if (length <= 5)
		{
			targetSize = 46;
		}
		else if (length <= 7)
		{
			targetSize = 42;
		}
		else if (length <= 9)
		{
			targetSize = 38;
		}
		else if (length <= 11)
		{
			targetSize = 34;
		}
		else if (length <= 13)
		{
			targetSize = 30;
		}
		else
		{
			targetSize = AmountFontSizeMin;
		}

		if (targetSize < AmountFontSizeMin)
		{
			targetSize = AmountFontSizeMin;
		}

		if (Math.Abs(AmountField.FontSize - targetSize) < 0.1)
		{
			return;
		}

		AmountField.FontSize = targetSize;
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