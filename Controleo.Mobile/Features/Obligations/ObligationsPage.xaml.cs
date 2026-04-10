using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.Obligations;

public partial class ObligationsPage : ContentPage
{
    private static readonly CultureInfo EsCulture = new("es-CO");

    private readonly IExpenseApiClient _apiClient;
    private readonly IAuthService _authService;
    private readonly IObligationNotificationService _obligationNotificationService;
    private readonly ICatalogColorService _colorService;
    private readonly IPaymentIconService _paymentIconService;

    private readonly List<string> _movementTypes = [];
    private readonly List<string> _paymentMethods = [];
    private readonly List<ObligationItem> _allObligations = [];

    private string? _editingId;
    private string? _selectedMovementType;
    private string? _selectedPaymentMethod;

    private DateOnly _visibleMonth;
    private DateOnly _selectedDay;

    public ObligationsPage(
        IExpenseApiClient apiClient,
        IAuthService authService,
        IObligationNotificationService obligationNotificationService,
        ICatalogColorService colorService,
        IPaymentIconService paymentIconService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authService = authService;
        _obligationNotificationService = obligationNotificationService;
        _colorService = colorService;
        _paymentIconService = paymentIconService;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _visibleMonth = new DateOnly(today.Year, today.Month, 1);
        _selectedDay = today;

        MoneyFormatHelper.Attach(MonthlyPaymentEntry);

        StartMonthEntry.Text = _visibleMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        UpdateMonthHeader();
        RebuildCalendar();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadPageAsync();
    }

    private async Task ReloadPageAsync()
    {
        SetLoading(true);
        try
        {
            await LoadCatalogsAsync();
            await LoadObligationsAsync();
            RebuildCalendar();
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

        _movementTypes.Clear();
        _movementTypes.AddRange(catalogs.MovementTypes);
        _paymentMethods.Clear();
        _paymentMethods.AddRange(catalogs.PaymentMethods);

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

        if (_selectedMovementType is null || !_movementTypes.Contains(_selectedMovementType, StringComparer.OrdinalIgnoreCase))
        {
            _selectedMovementType = _movementTypes.FirstOrDefault();
        }

        if (_selectedPaymentMethod is null || !_paymentMethods.Contains(_selectedPaymentMethod, StringComparer.OrdinalIgnoreCase))
        {
            _selectedPaymentMethod = _paymentMethods.FirstOrDefault();
        }

        SelectedTypeLabel.Text = _selectedMovementType ?? "Seleccionar";
        SelectedPayLabel.Text = _selectedPaymentMethod is null
            ? "Seleccionar"
            : $"{_paymentIconService.IconForPaymentMethod(_selectedPaymentMethod)} {_selectedPaymentMethod}";
    }

    private async Task LoadObligationsAsync()
    {
        var obligations = await _apiClient.GetObligationsAsync(CancellationToken.None);
        _allObligations.Clear();
        _allObligations.AddRange(obligations
            .OrderBy(item => item.DueDayOfMonth)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase));

        if (_allObligations.Count == 0)
        {
            StatusLabel.Text = "Aún no tienes obligaciones registradas.";
        }
        else
        {
            StatusLabel.Text = string.Empty;
        }

        await SyncNotificationPlanAsync();
    }

    private async Task SyncNotificationPlanAsync()
    {
        try
        {
            var isPremium = _authService.IsCurrentUserPremium;
            await _obligationNotificationService.EnsurePermissionAsync(isPremium, CancellationToken.None);
            await _obligationNotificationService.SyncReminderPlanAsync(isPremium, _allObligations, CancellationToken.None);
        }
        catch
        {
        }
    }

    private void UpdateMonthHeader()
    {
        var monthText = _visibleMonth.ToString("MMMM yyyy", EsCulture);
        MonthTitleLabel.Text = EsCulture.TextInfo.ToTitleCase(monthText);
    }

    private void RebuildCalendar()
    {
        NormalizeSelectedDayForVisibleMonth();

        var dayBuckets = BuildDayBuckets(_visibleMonth);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var firstDay = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, 1);
        var mondayBasedOffset = ((int)firstDay.DayOfWeek + 6) % 7;
        var calendarStart = firstDay.AddDays(-mondayBasedOffset);

        var isCurrentVisibleMonth = _visibleMonth.Year == today.Year && _visibleMonth.Month == today.Month;
        var daysWithObligations = dayBuckets.Count;
        var totalObligationsInMonth = dayBuckets.Values.Sum(items => items.Count);

        CalendarSummaryLabel.Text = totalObligationsInMonth == 0
            ? "No hay obligaciones para este mes."
            : $"{totalObligationsInMonth} obligación(es) en {daysWithObligations} día(s).";

        var days = new List<CalendarDayViewItem>(42);
        for (var index = 0; index < 42; index++)
        {
            var date = calendarStart.AddDays(index);
            var isCurrentMonth = date.Year == _visibleMonth.Year && date.Month == _visibleMonth.Month;
            var isSelected = date == _selectedDay;
            var isToday = date == today;

            var obligationsForDay = dayBuckets.TryGetValue(date, out var bucket)
                ? (IReadOnlyList<ObligationItem>)bucket
                : [];

            var hasObligations = obligationsForDay.Count > 0;
            var hasOverdue = hasObligations && isCurrentMonth && isCurrentVisibleMonth && date < today;

            var backgroundColor = ResolveBackgroundColor(isCurrentMonth, hasObligations, hasOverdue, isSelected);
            var dayTextColor = ResolveDayTextColor(isCurrentMonth, hasOverdue, isSelected);
            var borderBrush = ResolveBorderBrush(isToday, isSelected);

            var countBackgroundColor = hasOverdue
                ? Color.FromArgb("#FDEBE9")
                : Color.FromArgb("#D8F3DC");
            var countTextColor = hasOverdue
                ? Color.FromArgb("#8E1D13")
                : Color.FromArgb("#1B4332");

            days.Add(new CalendarDayViewItem(
                date,
                isCurrentMonth,
                obligationsForDay,
                hasOverdue,
                backgroundColor,
                borderBrush,
                dayTextColor,
                countBackgroundColor,
                countTextColor));
        }

        CalendarCollection.ItemsSource = days;
    }

    private static Color ResolveBackgroundColor(bool isCurrentMonth, bool hasObligations, bool hasOverdue, bool isSelected)
    {
        if (!isCurrentMonth)
        {
            return Color.FromArgb("#EFF3F1");
        }

        if (hasOverdue)
        {
            return Color.FromArgb("#FDEBE9");
        }

        if (isSelected)
        {
            return Color.FromArgb("#EAF6EE");
        }

        if (hasObligations)
        {
            return Color.FromArgb("#E7F6EE");
        }

        return Color.FromArgb("#FFFFFF");
    }

    private static Color ResolveDayTextColor(bool isCurrentMonth, bool hasOverdue, bool isSelected)
    {
        if (!isCurrentMonth)
        {
            return Color.FromArgb("#A8B8AE");
        }

        if (hasOverdue)
        {
            return Color.FromArgb("#8E1D13");
        }

        if (isSelected)
        {
            return Color.FromArgb("#1B4332");
        }

        return Color.FromArgb("#1A2E23");
    }

    private static Brush ResolveBorderBrush(bool isToday, bool isSelected)
    {
        if (isSelected)
        {
            return new SolidColorBrush(Color.FromArgb("#2D6A4F"));
        }

        if (isToday)
        {
            return new SolidColorBrush(Color.FromArgb("#40916C"));
        }

        return new SolidColorBrush(Color.FromArgb("#DCE5DF"));
    }

    private Dictionary<DateOnly, List<ObligationItem>> BuildDayBuckets(DateOnly month)
    {
        var buckets = new Dictionary<DateOnly, List<ObligationItem>>();

        foreach (var obligation in _allObligations)
        {
            if (!IsObligationVisibleForMonth(obligation, month))
            {
                continue;
            }

            var dueDate = ResolveDueDateForMonth(obligation, month);
            if (!buckets.TryGetValue(dueDate, out var list))
            {
                list = [];
                buckets[dueDate] = list;
            }

            list.Add(obligation);
        }

        foreach (var day in buckets.Keys.ToList())
        {
            buckets[day] = buckets[day]
                .OrderBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return buckets;
    }

    private static bool IsObligationVisibleForMonth(ObligationItem obligation, DateOnly month)
    {
        if (!obligation.IsActive)
        {
            return false;
        }

        var startMonth = ResolveStartMonth(obligation.StartMonth, month);
        if (startMonth > month)
        {
            return false;
        }

        return true;
    }

    private static DateOnly ResolveStartMonth(string? rawStartMonth, DateOnly fallbackMonth)
    {
        if (TryParseMonth(rawStartMonth, out var parsedMonth))
        {
            return parsedMonth;
        }

        return fallbackMonth;
    }

    private static bool TryParseMonth(string? rawMonth, out DateOnly month)
    {
        month = default;
        var token = (rawMonth ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(
                $"{token}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    private static DateOnly ResolveDueDateForMonth(ObligationItem obligation, DateOnly month)
    {
        var safeDay = Math.Clamp(obligation.DueDayOfMonth, 1, 31);
        var maxDayOfMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var day = Math.Min(safeDay, maxDayOfMonth);
        return new DateOnly(month.Year, month.Month, day);
    }

    private void NormalizeSelectedDayForVisibleMonth()
    {
        if (_selectedDay.Year == _visibleMonth.Year && _selectedDay.Month == _visibleMonth.Month)
        {
            return;
        }

        var safeDay = Math.Min(_selectedDay.Day, DateTime.DaysInMonth(_visibleMonth.Year, _visibleMonth.Month));
        _selectedDay = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, safeDay);
    }

    private async Task ChangeVisibleMonthAsync(DateOnly targetMonth, bool requiresRemoteReload = false)
    {
        _visibleMonth = new DateOnly(targetMonth.Year, targetMonth.Month, 1);
        NormalizeSelectedDayForVisibleMonth();
        UpdateMonthHeader();

        if (requiresRemoteReload)
        {
            SetLoading(true);
            try
            {
                await LoadObligationsAsync();
            }
            finally
            {
                SetLoading(false);
            }
        }

        RebuildCalendar();
    }

    private Task MoveMonthAsync(int monthDelta, bool requiresRemoteReload = false)
    {
        var target = _visibleMonth.AddMonths(monthDelta);
        return ChangeVisibleMonthAsync(target, requiresRemoteReload);
    }

    private async void OnPrevMonthClicked(object? sender, EventArgs e)
    {
        await MoveMonthAsync(-1);
    }

    private async void OnNextMonthClicked(object? sender, EventArgs e)
    {
        await MoveMonthAsync(1);
    }

    private async void OnTodayClicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _selectedDay = today;
        await ChangeVisibleMonthAsync(today);
    }

    private async void OnCalendarDayTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not CalendarDayViewItem day)
        {
            return;
        }

        if (!day.IsCurrentMonth)
        {
            _selectedDay = day.Date;
            await ChangeVisibleMonthAsync(day.Date);
            return;
        }

        _selectedDay = day.Date;
        RebuildCalendar();

        if (!day.HasObligations)
        {
            return;
        }

        var action = await ObligationDayDetailsModalPage.ShowAsync(
            this,
            day.Date,
            day.Obligations,
            _paymentIconService,
            _colorService);

        if (action is null)
        {
            return;
        }

        if (action.Action == ObligationDayActionKind.Edit)
        {
            BeginEdit(action.Item);
            return;
        }

        await DeleteObligationAsync(action.Item);
    }

    private async void OnCalendarSwiped(object? sender, SwipedEventArgs e)
    {
        if (e.Direction == SwipeDirection.Left)
        {
            await MoveMonthAsync(1);
            return;
        }

        if (e.Direction == SwipeDirection.Right)
        {
            await MoveMonthAsync(-1);
        }
    }

    private async void OnViewAllClicked(object? sender, EventArgs e)
    {
        while (true)
        {
            var action = await ObligationsPagedModalPage.ShowAsync(this, _apiClient);
            if (action is null)
            {
                return;
            }

            if (action.Action == ObligationListActionKind.Edit)
            {
                BeginEdit(action.Item);
                return;
            }

            await DeleteObligationAsync(action.Item);
        }
    }

    private async void OnAddNewClicked(object? sender, EventArgs e)
    {
        ClearForm();
        FormTitle.Text = "Nueva obligacion";
        DueDayEntry.Text = _selectedDay.Day.ToString(CultureInfo.InvariantCulture);
        StartMonthEntry.Text = _visibleMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        FormOverlay.IsVisible = true;
        await Task.CompletedTask;
    }

    private void OnCancelForm(object? sender, EventArgs e)
    {
        FormOverlay.IsVisible = false;
        ClearForm();
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
            var result = await _apiClient.SaveObligationAsync(_editingId, request!, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Obligacion guardada" : "No se pudo guardar",
                result.IsSuccess ? "La obligacion se guardo correctamente." : result.Message);

            if (!result.IsSuccess)
            {
                return;
            }

            FormOverlay.IsVisible = false;
            ClearForm();

            var targetDay = ResolveDueDateForMonth(
                new ObligationItem(
                    string.Empty,
                    request!.Description,
                    request.MovementType,
                    request.PaymentMethod,
                    request.DueDayOfMonth,
                    request.MonthlyPayment,
                    request.ReminderDaysBefore,
                    request.StartMonth,
                    request.IsActive),
                _visibleMonth);
            _selectedDay = targetDay;

            await LoadObligationsAsync();
            RebuildCalendar();
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SetLoading(false);
        }
    }

    private async Task DeleteObligationAsync(ObligationItem item)
    {
        var confirm = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar obligacion", $"¿Eliminar '{item.Description}'?");
        if (!confirm)
        {
            return;
        }

        SetLoading(true);
        try
        {
            var result = await _apiClient.DeleteObligationAsync(item.Id, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(
                this,
                result.IsSuccess,
                result.IsSuccess ? "Obligacion eliminada" : "No se pudo eliminar",
                result.IsSuccess ? "La obligacion se elimino correctamente." : result.Message);

            if (result.IsSuccess)
            {
                await LoadObligationsAsync();
                RebuildCalendar();
            }
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void BeginEdit(ObligationItem item)
    {
        _editingId = item.Id;
        FormTitle.Text = "Editar obligacion";
        DescriptionEntry.Text = item.Description;
        MoneyFormatHelper.SetValue(MonthlyPaymentEntry, item.MonthlyPayment);
        DueDayEntry.Text = item.DueDayOfMonth.ToString(CultureInfo.InvariantCulture);
        _selectedMovementType = item.MovementType;
        _selectedPaymentMethod = item.PaymentMethod;
        SelectedTypeLabel.Text = _selectedMovementType;
        SelectedPayLabel.Text = $"{_paymentIconService.IconForPaymentMethod(_selectedPaymentMethod)} {_selectedPaymentMethod}";
        ReminderDaysEntry.Text = item.ReminderDaysBefore.ToString(CultureInfo.InvariantCulture);
        StartMonthEntry.Text = item.StartMonth;
        IsActiveCheck.IsChecked = item.IsActive;
        StatusLabel.Text = string.Empty;
        FormOverlay.IsVisible = true;
    }

    private bool TryBuildRequest(out ObligationUpsertRequest? request, out string errorMessage)
    {
        request = null;
        errorMessage = string.Empty;

        var description = DescriptionEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            errorMessage = "La descripcion es requerida.";
            return false;
        }

        if (!MoneyFormatHelper.TryParse(MonthlyPaymentEntry.Text, out var monthlyPayment) || monthlyPayment <= 0)
        {
            errorMessage = "La cuota mensual debe ser mayor a cero.";
            return false;
        }

        if (!int.TryParse(DueDayEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dueDay) || dueDay < 1 || dueDay > 31)
        {
            errorMessage = "El dia de cobro debe estar entre 1 y 31.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_selectedMovementType) || string.IsNullOrWhiteSpace(_selectedPaymentMethod))
        {
            errorMessage = "Debes elegir tipo de gasto y medio de pago.";
            return false;
        }

        var startMonth = (StartMonthEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(startMonth))
        {
            startMonth = DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        if (!DateOnly.TryParseExact($"{startMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errorMessage = "El mes de inicio debe tener formato yyyy-MM.";
            return false;
        }

        var reminderDays = 3;
        if (!string.IsNullOrWhiteSpace(ReminderDaysEntry.Text)
            && (!int.TryParse(ReminderDaysEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out reminderDays) || reminderDays < 0 || reminderDays > 30))
        {
            errorMessage = "El recordatorio debe estar entre 0 y 30 dias.";
            return false;
        }

        request = new ObligationUpsertRequest(
            description,
            _selectedMovementType,
            _selectedPaymentMethod,
            dueDay,
            monthlyPayment,
            reminderDays,
            startMonth,
            IsActiveCheck.IsChecked);

        return true;
    }

    private async void OnTypeSelectorTapped(object? sender, TappedEventArgs e)
    {
        if (_movementTypes.Count == 0)
        {
            return;
        }

        var selected = await StyledSelectorModalPage.PickAsync(this, "Tipo de gasto", _movementTypes, _selectedMovementType);
        if (selected is null)
        {
            return;
        }

        _selectedMovementType = selected;
        SelectedTypeLabel.Text = selected;
    }

    private async void OnPaySelectorTapped(object? sender, TappedEventArgs e)
    {
        if (_paymentMethods.Count == 0)
        {
            return;
        }

        var selected = await StyledSelectorModalPage.PickAsync(this, "Medio de pago", _paymentMethods, _selectedPaymentMethod);
        if (selected is null)
        {
            return;
        }

        _selectedPaymentMethod = selected;
        SelectedPayLabel.Text = $"{_paymentIconService.IconForPaymentMethod(selected)} {selected}";
    }

    private void ClearForm()
    {
        _editingId = null;
        DescriptionEntry.Text = string.Empty;
        MonthlyPaymentEntry.Text = string.Empty;
        DueDayEntry.Text = "1";

        if (_movementTypes.Count > 0)
        {
            _selectedMovementType = _movementTypes[0];
            SelectedTypeLabel.Text = _selectedMovementType;
        }
        else
        {
            _selectedMovementType = null;
            SelectedTypeLabel.Text = "Seleccionar";
        }

        if (_paymentMethods.Count > 0)
        {
            _selectedPaymentMethod = _paymentMethods[0];
            SelectedPayLabel.Text = $"{_paymentIconService.IconForPaymentMethod(_selectedPaymentMethod)} {_selectedPaymentMethod}";
        }
        else
        {
            _selectedPaymentMethod = null;
            SelectedPayLabel.Text = "Seleccionar";
        }

        ReminderDaysEntry.Text = "3";
        StartMonthEntry.Text = _visibleMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        DueDayEntry.Text = _selectedDay.Day.ToString(CultureInfo.InvariantCulture);
        IsActiveCheck.IsChecked = true;
        StatusLabel.Text = _allObligations.Count == 0 ? "Aún no tienes obligaciones registradas." : string.Empty;
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

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

    private sealed record CalendarDayViewItem(
        DateOnly Date,
        bool IsCurrentMonth,
        IReadOnlyList<ObligationItem> Obligations,
        bool HasOverdue,
        Color BackgroundColor,
        Brush BorderBrush,
        Color DayTextColor,
        Color CountBackgroundColor,
        Color CountTextColor)
    {
        public bool HasObligations => Obligations.Count > 0;
        public string DayNumberText => Date.Day.ToString(CultureInfo.InvariantCulture);
        public string ObligationCountText => Obligations.Count.ToString(CultureInfo.InvariantCulture);
    }
}
