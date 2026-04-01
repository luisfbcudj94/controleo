using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class DashboardPage : ContentPage
{
    private enum DashboardMode
    {
        Category,
        PaymentMethod
    }

    private static readonly string[] PaymentMethodCardPalette =
    [
        "#DDEEE4",
        "#DFEAF7",
        "#F8E5D8",
        "#E8E0F4",
        "#F7E1E7",
        "#E3F1EC"
    ];

    private static readonly string[] PaymentMethodProgressPalette =
    [
        "#2D6A4F",
        "#3A8FBF",
        "#D4765A",
        "#7B5EA7",
        "#C0608F",
        "#3A9E8E"
    ];

    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<DashboardListViewItem> _items = [];
    private readonly ObservableCollection<DonutLegendItem> _donutLegendItems = [];
    private bool _isRefreshing;
    private bool _isOpeningDetail;
    private bool _isMonthPickerSyncing;
    private bool _isActive;
    private DashboardMode _selectedMode = DashboardMode.Category;
    private IReadOnlyList<DashboardCategoryItem> _categoryData = [];
    private IReadOnlyList<DashboardPaymentMethodItem> _paymentMethodData = [];
    private readonly BudgetRingDrawable _budgetRingDrawable = new();
    private readonly DistributionDonutDrawable _distributionDonutDrawable = new();

    public DashboardPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        SyncMonthSelection();
        ChartCollection.ItemsSource = _items;
        DonutLegendCollection.ItemsSource = _donutLegendItems;
        BudgetRingView.Drawable = _budgetRingDrawable;
        DistributionDonutView.Drawable = _distributionDonutDrawable;
        UpdateModeButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isActive = true;
        await RefreshMonthOptionsAsync();
        await LoadDataAsync();
    }

    private async Task RefreshMonthOptionsAsync()
    {
        var monthKeys = await _apiClient.GetAvailableMonthsAsync(CancellationToken.None);
        _monthContext.SetAvailableMonths(monthKeys);
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
        try
        {
            var catalogTask = _apiClient.GetCatalogsAsync(CancellationToken.None);
            var categoryTask = _apiClient.GetDashboardByCategoryAsync(_monthContext.SelectedMonthKey, CancellationToken.None);
            var paymentTask = _apiClient.GetDashboardByPaymentMethodAsync(_monthContext.SelectedMonthKey, CancellationToken.None);

            await Task.WhenAll(catalogTask, categoryTask, paymentTask);

            var catalog = catalogTask.Result;
            Services.PastelColorHelper.SetConfigs(catalog.MovementTypeConfigs);

            _categoryData = categoryTask.Result;
            _paymentMethodData = paymentTask.Result;

            RenderBudgetSummary(_categoryData);
            ApplyModePresentation();
        }
        finally
        {
            _isRefreshing = false;
            DashboardRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private void RenderBudgetSummary(IReadOnlyList<DashboardCategoryItem> data)
    {
        var totalBudget = data.Sum(item => item.BudgetTotal);
        var spentFromBudget = data.Where(item => item.BudgetTotal > 0).Sum(item => item.ExpenseTotal);
        var availableBudget = totalBudget - spentFromBudget;
        var budgetProgress = totalBudget <= 0 ? 0d : (double)Math.Min(1m, spentFromBudget / totalBudget);

        RingPercentLabel.Text = $"{budgetProgress * 100:0}%";
        _budgetRingDrawable.Progress = budgetProgress;
        _budgetRingDrawable.TrackColor = Color.FromArgb("#DCE5DF");
        _budgetRingDrawable.ProgressColor = Color.FromArgb("#2D6A4F");
        BudgetRingView.Invalidate();

        BudgetSummaryLabel.Text = $"{FormatCompactCurrency(spentFromBudget)} / {FormatCompactCurrency(totalBudget)}";
        AvailableSummaryLabel.Text = $"${availableBudget:N0}";
        TotalBudgetSummaryLabel.Text = $"${totalBudget:N0}";
        SpentSummaryLabel.Text = $"${spentFromBudget:N0}";
    }

    private void ApplyModePresentation()
    {
        UpdateModeButtons();
        RenderDistribution();
        RenderList();
    }

    private void RenderDistribution()
    {
        _donutLegendItems.Clear();

        if (_selectedMode == DashboardMode.Category)
        {
            DistributionCenterLabel.Text = "Por tipo";
            SectionListTitleLabel.Text = "CATEGORIAS";

            var distribution = _categoryData
                .Where(item => item.ExpenseTotal > 0)
                .OrderByDescending(item => item.ExpenseTotal)
                .ToList();

            var total = distribution.Sum(item => item.ExpenseTotal);
            _distributionDonutDrawable.Segments = distribution
                .Select(item => new DistributionDonutDrawable.Segment(
                    Services.PastelColorHelper.ForMovementType(item.MovementType),
                    total <= 0 ? 0 : (double)(item.ExpenseTotal / total)))
                .ToList();

            if (total <= 0)
            {
                DistributionSummaryLabel.Text = "Anade gastos para ver la distribucion";
                DistributionSummaryLabel.TextColor = Color.FromArgb("#E5534B");
            }
            else
            {
                DistributionSummaryLabel.Text = "100% por categoria";
                DistributionSummaryLabel.TextColor = Color.FromArgb("#1A2E23");

                foreach (var item in distribution)
                {
                    var pct = Math.Round((item.ExpenseTotal / total) * 100m);
                    _donutLegendItems.Add(new DonutLegendItem(
                        $"{item.MovementType} {pct:0}%",
                        Services.PastelColorHelper.ForMovementType(item.MovementType)));
                }
            }

            DistributionDonutView.Invalidate();
            return;
        }

        DistributionCenterLabel.Text = "Por medio";
        SectionListTitleLabel.Text = "MEDIOS DE PAGO";

        var paymentDistribution = _paymentMethodData
            .Where(item => item.ExpenseTotal > 0)
            .OrderByDescending(item => item.ExpenseTotal)
            .ToList();

        var paymentTotal = paymentDistribution.Sum(item => item.ExpenseTotal);
        _distributionDonutDrawable.Segments = paymentDistribution
            .Select(item => new DistributionDonutDrawable.Segment(
                ProgressColorForPaymentMethod(item.PaymentMethod),
                paymentTotal <= 0 ? 0 : (double)(item.ExpenseTotal / paymentTotal)))
            .ToList();

        if (paymentTotal <= 0)
        {
            DistributionSummaryLabel.Text = "Anade gastos para ver la distribucion";
            DistributionSummaryLabel.TextColor = Color.FromArgb("#E5534B");
        }
        else
        {
            DistributionSummaryLabel.Text = "100% por medio de pago";
            DistributionSummaryLabel.TextColor = Color.FromArgb("#1A2E23");

            foreach (var item in paymentDistribution)
            {
                var pct = Math.Round((item.ExpenseTotal / paymentTotal) * 100m);
                _donutLegendItems.Add(new DonutLegendItem(
                    $"{item.PaymentMethod} {pct:0}%",
                    ProgressColorForPaymentMethod(item.PaymentMethod)));
            }
        }

        DistributionDonutView.Invalidate();
    }

    private void RenderList()
    {
        _items.Clear();

        if (_selectedMode == DashboardMode.Category)
        {
            var orderedData = _categoryData
                .OrderBy(item => item.BudgetTotal <= 0 ? 1 : 0)
                .ThenByDescending(item => item.BudgetTotal)
                .ThenByDescending(item => item.ExpenseTotal)
                .ToList();

            foreach (var row in orderedData)
            {
                var hasBudget = row.BudgetTotal > 0;
                var progress = hasBudget
                    ? (double)Math.Clamp(row.ExpenseTotal / row.BudgetTotal, 0m, 1m)
                    : (row.ExpenseTotal > 0 ? 1d : 0d);

                var subtitle = hasBudget
                    ? $"Presupuesto: ${row.BudgetTotal:N0} · Saldo: ${row.Balance:N0}"
                    : "Sin presupuesto asignado";

                _items.Add(new DashboardListViewItem(
                    row.MovementType,
                    row.MovementType,
                    row.ExpenseTotal,
                    progress,
                    hasBudget ? ProgressColorForType(row.MovementType) : Color.FromArgb("#E5534B"),
                    subtitle,
                    Services.PastelColorHelper.ForMovementType(row.MovementType),
                    Services.PastelColorHelper.IconForMovementType(row.MovementType)));
            }

            return;
        }

        var paymentData = _paymentMethodData
            .OrderByDescending(item => item.ExpenseTotal)
            .ToList();

        var totalPayment = paymentData.Sum(item => item.ExpenseTotal);
        foreach (var row in paymentData)
        {
            var ratio = totalPayment <= 0 ? 0d : (double)Math.Clamp(row.ExpenseTotal / totalPayment, 0m, 1m);
            var share = totalPayment <= 0 ? 0m : (row.ExpenseTotal / totalPayment) * 100m;

            _items.Add(new DashboardListViewItem(
                row.PaymentMethod,
                row.PaymentMethod,
                row.ExpenseTotal,
                ratio,
                ProgressColorForPaymentMethod(row.PaymentMethod),
                totalPayment <= 0 ? "Sin gastos en el periodo" : $"Participacion: {share:0.#}%",
                CardColorForPaymentMethod(row.PaymentMethod),
                IconForPaymentMethod(row.PaymentMethod)));
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private async void OnSectionTapped(object? sender, TappedEventArgs e)
    {
        if (_isOpeningDetail)
        {
            return;
        }

        if (e.Parameter is not string selectedKey || string.IsNullOrWhiteSpace(selectedKey))
        {
            return;
        }

        try
        {
            _isOpeningDetail = true;
            SetLoading(true);

            var detailPage = _selectedMode == DashboardMode.Category
                ? new SectionExpensesModalPage(_apiClient, _monthContext.SelectedMonthKey, selectedKey, movementType: selectedKey)
                : new SectionExpensesModalPage(_apiClient, _monthContext.SelectedMonthKey, selectedKey, paymentMethod: selectedKey);

            await Navigation.PushModalAsync(new NavigationPage(detailPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo abrir el detalle: {ex.Message}", "OK");
        }
        finally
        {
            _isOpeningDetail = false;
            SetLoading(false);
        }
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

    private void OnCategoryModeClicked(object? sender, EventArgs e)
    {
        if (_selectedMode == DashboardMode.Category)
        {
            return;
        }

        _selectedMode = DashboardMode.Category;
        ApplyModePresentation();
    }

    private void OnPaymentModeClicked(object? sender, EventArgs e)
    {
        if (_selectedMode == DashboardMode.PaymentMethod)
        {
            return;
        }

        _selectedMode = DashboardMode.PaymentMethod;
        ApplyModePresentation();
    }

    private void UpdateModeButtons()
    {
        var activeBackground = Color.FromArgb("#2D6A4F");
        var inactiveBackground = Color.FromArgb("#E8EFEA");
        var activeText = Colors.White;
        var inactiveText = Color.FromArgb("#1F2A24");

        var categoryActive = _selectedMode == DashboardMode.Category;

        CategoryModeButton.BackgroundColor = categoryActive ? activeBackground : inactiveBackground;
        CategoryModeButton.TextColor = categoryActive ? activeText : inactiveText;

        PaymentModeButton.BackgroundColor = categoryActive ? inactiveBackground : activeBackground;
        PaymentModeButton.TextColor = categoryActive ? inactiveText : activeText;
    }

    private static string FormatCompactCurrency(decimal value)
    {
        if (value >= 1_000_000m)
        {
            return $"${value / 1_000_000m:0.#}M";
        }

        if (value >= 1_000m)
        {
            return $"${value / 1_000m:0.#}K";
        }

        return $"${value:0}";
    }

    private static Color ProgressColorForType(string movementType)
    {
        return movementType.ToLowerInvariant() switch
        {
            "basicos para vivir" => Color.FromArgb("#2D6A4F"),
            "hogar" => Color.FromArgb("#3A8FBF"),
            "salidas" => Color.FromArgb("#D4765A"),
            "imprevistos" => Color.FromArgb("#7B5EA7"),
            "suscripciones" => Color.FromArgb("#C5A200"),
            "bienestar" => Color.FromArgb("#C0608F"),
            "viajes" => Color.FromArgb("#3A9E8E"),
            "deudas" => Color.FromArgb("#5A7FA0"),
            "prestamo" => Color.FromArgb("#B08A40"),
            "obra" => Color.FromArgb("#4A8B72"),
            _ => Color.FromArgb("#2D6A4F")
        };
    }

    private static Color CardColorForPaymentMethod(string paymentMethod)
    {
        return Color.FromArgb(PaymentMethodCardPalette[StableIndexFor(paymentMethod, PaymentMethodCardPalette.Length)]);
    }

    private static Color ProgressColorForPaymentMethod(string paymentMethod)
    {
        return Color.FromArgb(PaymentMethodProgressPalette[StableIndexFor(paymentMethod, PaymentMethodProgressPalette.Length)]);
    }

    private static int StableIndexFor(string key, int length)
    {
        if (length <= 0 || string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        uint hash = 2166136261;
        foreach (var ch in key.Trim().ToLowerInvariant())
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return (int)(hash % (uint)length);
    }

    private static string IconForPaymentMethod(string paymentMethod)
    {
        var normalized = paymentMethod.Trim().ToLowerInvariant();
        if (normalized.Contains("efectivo", StringComparison.Ordinal))
        {
            return "💵";
        }

        if (normalized.Contains("nequi", StringComparison.Ordinal) || normalized.Contains("transfer", StringComparison.Ordinal))
        {
            return "📲";
        }

        if (normalized.StartsWith("td", StringComparison.Ordinal) || normalized.Contains("deb", StringComparison.Ordinal))
        {
            return "🏦";
        }

        if (normalized.StartsWith("tc", StringComparison.Ordinal) || normalized.Contains("cred", StringComparison.Ordinal))
        {
            return "💳";
        }

        return "💳";
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

    private sealed record DashboardListViewItem(
        string Key,
        string Name,
        decimal Amount,
        double Ratio,
        Color ProgressColor,
        string Subtitle,
        Color CardColor,
        string Icon);

    private sealed record DonutLegendItem(string Label, Color DotColor);

    private sealed class BudgetRingDrawable : IDrawable
    {
        public double Progress { get; set; }
        public Color TrackColor { get; set; } = Colors.LightGray;
        public Color ProgressColor { get; set; } = Colors.MediumSlateBlue;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = 22f;
            var padding = stroke / 2 + 4;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (padding * 2);
            if (size <= 0)
            {
                return;
            }

            var x = (dirtyRect.Width - size) / 2;
            var y = (dirtyRect.Height - size) / 2;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Butt;

            canvas.StrokeColor = TrackColor;
            canvas.DrawArc(x, y, size, size, 0, 360, true, false);

            if (Progress <= 0)
            {
                return;
            }

            var sweep = (float)(360 * Math.Clamp(Progress, 0, 1));
            var start = -90f;
            var end = start - sweep;
            canvas.StrokeColor = ProgressColor;
            canvas.DrawArc(x, y, size, size, start, end, true, false);
        }
    }

    private sealed class DistributionDonutDrawable : IDrawable
    {
        public sealed record Segment(Color Color, double Ratio);
        public List<Segment> Segments { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = 34f;
            var padding = stroke / 2 + 4;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (padding * 2);
            if (size <= 0)
            {
                return;
            }

            var x = (dirtyRect.Width - size) / 2;
            var y = (dirtyRect.Height - size) / 2;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Butt;

            var start = -90f;
            var normalizedSegments = Segments
                .Where(segment => segment.Ratio > 0)
                .ToList();
            var total = normalizedSegments.Sum(segment => segment.Ratio);
            if (total <= 0)
            {
                canvas.StrokeColor = Color.FromArgb("#DCE5DF");
                canvas.DrawArc(x, y, size, size, 0, 360, true, false);
                return;
            }

            float consumedSweep = 0f;
            for (var i = 0; i < normalizedSegments.Count; i++)
            {
                var segment = normalizedSegments[i];
                var isLast = i == normalizedSegments.Count - 1;
                var sweep = isLast
                    ? 360f - consumedSweep
                    : (float)(360d * (segment.Ratio / total));

                if (sweep <= 0.01f)
                {
                    continue;
                }

                var end = start - sweep;
                canvas.StrokeColor = segment.Color;
                canvas.DrawArc(x, y, size, size, start, end, true, false);

                start = end;
                consumedSweep += sweep;
            }
        }
    }
}
