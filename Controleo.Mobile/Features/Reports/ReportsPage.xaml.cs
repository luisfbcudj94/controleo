using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Shared.Modals;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Controleo.Mobile.Features.Reports;

public partial class ReportsPage : ContentPage
{
    private static readonly CultureInfo EsCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IExpenseApiClient _apiClient;
    private bool _loadedOnce;

    private DateOnly _startDate;
    private DateOnly _endDate;

    public ReportsPage(IExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _startDate = new DateOnly(today.Year, today.Month, 1);
        _endDate = today;

        UpdateRangeLabels();
        ResetSummary();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        if (!TryValidateRange(out var errorMessage))
        {
            StatusLabel.Text = errorMessage;
            return;
        }

        SetLoading(true);
        try
        {
            var preview = await _apiClient.GetReportPreviewAsync(_startDate, _endDate, CancellationToken.None);
            if (!preview.IsSuccess)
            {
                ResetSummary();
                StatusLabel.Text = preview.Message;
                return;
            }

            BindPreview(preview);
            StatusLabel.Text = $"Resumen actualizado para {_startDate:dd MMM yyyy} - {_endDate:dd MMM yyyy}.";
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void BindPreview(ReportPreviewResult preview)
    {
        TotalTransactionsLabel.Text = preview.TotalTransactions.ToString("N0", EsCulture);
        TotalAmountLabel.Text = preview.TotalAmount.ToString("C0", EsCulture);
        AverageDailyLabel.Text = preview.AverageDailyAmount.ToString("C0", EsCulture);

        AmountChangeLabel.Text = FormatSignedPercentage(preview.AmountChangePercentage);
        AmountChangeDetailLabel.Text = $"Antes: {preview.PreviousPeriodAmount.ToString("C0", EsCulture)}";
        TransactionsChangeLabel.Text = FormatSignedPercentage(preview.TransactionsChangePercentage);
        TransactionsChangeDetailLabel.Text = $"Antes: {preview.PreviousPeriodTransactions:N0}";

        var amountChangeColor = ResolveChangeColor(preview.AmountChangePercentage);
        var txChangeColor = ResolveChangeColor(preview.TransactionsChangePercentage);
        AmountChangeLabel.TextColor = amountChangeColor;
        TransactionsChangeLabel.TextColor = txChangeColor;

        CategoryCollection.ItemsSource = (preview.CategoryBreakdown ?? [])
            .Select((item, index) => new BreakdownViewItem(
                item.Name,
                item.Amount.ToString("C0", EsCulture),
                $"{item.Count} movimiento(s)",
                ClampShare(item.Percentage),
                $"{item.Percentage:0.##}%",
                ResolvePalette(index)))
            .ToArray();

        PaymentMethodCollection.ItemsSource = (preview.PaymentMethodBreakdown ?? [])
            .Select((item, index) => new BreakdownViewItem(
                item.Name,
                item.Amount.ToString("C0", EsCulture),
                $"{item.Count} movimiento(s)",
                ClampShare(item.Percentage),
                $"{item.Percentage:0.##}%",
                ResolvePalette(index + 3)))
            .ToArray();

        TopExpensesCollection.ItemsSource = (preview.TopExpenses ?? [])
            .Select(item => new TopExpenseViewItem(
                item.Date.ToString("dd MMM", EsCulture),
                item.Description,
                $"{item.MovementType} · {item.PaymentMethod}",
                item.Amount.ToString("C0", EsCulture)))
            .ToArray();

        var trend = BuildDailyTrendItems(preview.DailyTrend ?? []);
        DailyTrendCollection.ItemsSource = trend;

        InsightsCollection.ItemsSource = (preview.Insights ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new InsightViewItem(item.Trim()))
            .ToArray();

        UpdatePremiumNarrative(preview);
    }

    private void ResetSummary()
    {
        TotalTransactionsLabel.Text = "0";
        TotalAmountLabel.Text = "$0";
        AverageDailyLabel.Text = "$0";
        AmountChangeLabel.Text = "0%";
        AmountChangeDetailLabel.Text = "Antes: $0";
        TransactionsChangeLabel.Text = "0%";
        TransactionsChangeDetailLabel.Text = "Antes: 0";
        AmountChangeLabel.TextColor = ResolveChangeColor(0);
        TransactionsChangeLabel.TextColor = ResolveChangeColor(0);
        CategoryCollection.ItemsSource = Array.Empty<BreakdownViewItem>();
        PaymentMethodCollection.ItemsSource = Array.Empty<BreakdownViewItem>();
        TopExpensesCollection.ItemsSource = Array.Empty<TopExpenseViewItem>();
        DailyTrendCollection.ItemsSource = Array.Empty<DailyTrendViewItem>();
        InsightsCollection.ItemsSource = Array.Empty<InsightViewItem>();
        PulseScoreLabel.Text = "--";
        PulseScoreLabel.TextColor = Color.FromArgb("#4C6256");
        PremiumNarrativeLabel.Text = "Selecciona un rango para generar lectura inteligente de tus hábitos de gasto.";
    }

    private void UpdateRangeLabels()
    {
        StartDateLabel.Text = _startDate.ToString("dd MMM yyyy", EsCulture);
        EndDateLabel.Text = _endDate.ToString("dd MMM yyyy", EsCulture);
    }

    private bool TryValidateRange(out string errorMessage)
    {
        if (_startDate > _endDate)
        {
            errorMessage = "La fecha inicial no puede ser mayor que la final.";
            return false;
        }

        var rangeDays = _endDate.DayNumber - _startDate.DayNumber + 1;
        if (rangeDays > 366)
        {
            errorMessage = "El rango máximo es de 12 meses.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static double ClampShare(decimal percentage)
    {
        var normalized = percentage / 100m;
        if (normalized < 0)
        {
            return 0;
        }

        if (normalized > 1)
        {
            return 1;
        }

        return (double)normalized;
    }

    private static Color ResolvePalette(int index)
    {
        var palette = new[]
        {
            "#2D6A4F",
            "#40916C",
            "#1B4332",
            "#1D5FA7",
            "#0D4A7A",
            "#8A4D11",
            "#B85C38",
            "#6A994E"
        };

        return Color.FromArgb(palette[Math.Abs(index) % palette.Length]);
    }

    private static Color ResolveChangeColor(decimal percentage)
    {
        if (percentage > 0)
        {
            return Color.FromArgb("#8E1D13");
        }

        if (percentage < 0)
        {
            return Color.FromArgb("#1B4332");
        }

        return Color.FromArgb("#4C6256");
    }

    private static string FormatSignedPercentage(decimal value)
    {
        return value > 0
            ? $"+{value:0.##}%"
            : $"{value:0.##}%";
    }

    private static IReadOnlyList<DailyTrendViewItem> BuildDailyTrendItems(IReadOnlyList<ReportDailyTrendPoint> points)
    {
        if (points.Count == 0)
        {
            return Array.Empty<DailyTrendViewItem>();
        }

        var visible = ReducePoints(points, 31);
        var maxAmount = visible.Max(item => item.Amount);

        return visible
            .Select(item =>
            {
                var ratio = maxAmount <= 0 ? 0 : (double)(item.Amount / maxAmount);
                var height = 8 + (ratio * 62);
                return new DailyTrendViewItem(
                    item.Date.ToString("dd", EsCulture),
                    height,
                    item.Amount > 0 ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#DCE5DF"));
            })
            .ToArray();
    }

    private static IReadOnlyList<ReportDailyTrendPoint> ReducePoints(IReadOnlyList<ReportDailyTrendPoint> points, int maxCount)
    {
        if (points.Count <= maxCount)
        {
            return points;
        }

        var result = new List<ReportDailyTrendPoint>(maxCount);
        var step = (points.Count - 1d) / (maxCount - 1d);

        for (var i = 0; i < maxCount; i++)
        {
            var index = (int)Math.Round(i * step);
            if (index < 0)
            {
                index = 0;
            }

            if (index >= points.Count)
            {
                index = points.Count - 1;
            }

            result.Add(points[index]);
        }

        return result;
    }

    private void UpdatePremiumNarrative(ReportPreviewResult preview)
    {
        var score = CalculatePulseScore(preview);
        PulseScoreLabel.Text = score.ToString(CultureInfo.InvariantCulture);

        if (score >= 80)
        {
            PulseScoreLabel.TextColor = Color.FromArgb("#1B4332");
            PremiumNarrativeLabel.Text = "Muy buen control del periodo. Mantén este ritmo y ajusta solo los gastos de mayor peso para optimizar aún más.";
            return;
        }

        if (score >= 60)
        {
            PulseScoreLabel.TextColor = Color.FromArgb("#8A4D11");
            PremiumNarrativeLabel.Text = "Comportamiento estable con oportunidades claras de mejora. Enfócate en la categoría principal y en los días pico para bajar el total.";
            return;
        }

        PulseScoreLabel.TextColor = Color.FromArgb("#8E1D13");
        PremiumNarrativeLabel.Text = "Se detecta presión en tus gastos. Te conviene revisar de inmediato los rubros dominantes y poner topes por semana.";
    }

    private static int CalculatePulseScore(ReportPreviewResult preview)
    {
        var score = 72;

        if (preview.AmountChangePercentage > 0)
        {
            score -= Math.Min(28, (int)Math.Round(preview.AmountChangePercentage / 2m, MidpointRounding.AwayFromZero));
        }
        else if (preview.AmountChangePercentage < 0)
        {
            score += Math.Min(12, (int)Math.Round(Math.Abs(preview.AmountChangePercentage) / 3m, MidpointRounding.AwayFromZero));
        }

        var topCategoryShare = preview.CategoryBreakdown.FirstOrDefault()?.Percentage ?? 0;
        if (topCategoryShare > 50)
        {
            score -= 14;
        }
        else if (topCategoryShare > 35)
        {
            score -= 8;
        }

        var topPaymentShare = preview.PaymentMethodBreakdown.FirstOrDefault()?.Percentage ?? 0;
        if (topPaymentShare > 60)
        {
            score -= 10;
        }

        var days = Math.Max(1, preview.EndDate.DayNumber - preview.StartDate.DayNumber + 1);
        var previousDailyAverage = preview.PreviousPeriodAmount <= 0
            ? 0
            : preview.PreviousPeriodAmount / days;

        if (previousDailyAverage > 0 && preview.AverageDailyAmount > (previousDailyAverage * 1.2m))
        {
            score -= 8;
        }

        if (preview.TotalTransactions <= 0)
        {
            score = 0;
        }

        return Math.Clamp(score, 0, 100);
    }

    private async Task ApplyPresetRangeAsync(DateOnly startDate, DateOnly endDate)
    {
        _startDate = startDate;
        _endDate = endDate;
        UpdateRangeLabels();
        await LoadPreviewAsync();
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

    private async void OnPickStartDateTapped(object? sender, TappedEventArgs e)
    {
        var minDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-5));
        var picked = await CalendarDateModalPage.PickAsync(this, "Fecha inicial", _startDate, minDate, _endDate);
        if (picked is null)
        {
            return;
        }

        _startDate = picked.Value;
        UpdateRangeLabels();
    }

    private async void OnPickEndDateTapped(object? sender, TappedEventArgs e)
    {
        var maxDate = DateOnly.FromDateTime(DateTime.Today);
        var picked = await CalendarDateModalPage.PickAsync(this, "Fecha final", _endDate, _startDate, maxDate);
        if (picked is null)
        {
            return;
        }

        _endDate = picked.Value;
        UpdateRangeLabels();
    }

    private async void OnRefreshPreviewClicked(object? sender, EventArgs e)
    {
        await LoadPreviewAsync();
    }

    private async void OnPreset7DaysClicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await ApplyPresetRangeAsync(today.AddDays(-6), today);
    }

    private async void OnPreset30DaysClicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await ApplyPresetRangeAsync(today.AddDays(-29), today);
    }

    private async void OnPresetCurrentMonthClicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await ApplyPresetRangeAsync(new DateOnly(today.Year, today.Month, 1), today);
    }

    private async void OnPresetPreviousMonthClicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var previousMonthStart = currentMonthStart.AddMonths(-1);
        var previousMonthEnd = currentMonthStart.AddDays(-1);
        await ApplyPresetRangeAsync(previousMonthStart, previousMonthEnd);
    }

    private async void OnDownloadCsvClicked(object? sender, EventArgs e)
    {
        await DownloadAndShareAsync(
            (fromDate, toDate, ct) => _apiClient.DownloadReportCsvAsync(fromDate, toDate, ct),
            "CSV generado");
    }

    private async void OnDownloadPdfClicked(object? sender, EventArgs e)
    {
        await DownloadAndShareAsync(
            (fromDate, toDate, ct) => _apiClient.DownloadReportPdfAsync(fromDate, toDate, ct),
            "PDF generado");
    }

    private async Task DownloadAndShareAsync(
        Func<DateOnly, DateOnly, CancellationToken, Task<ReportFileDownloadResult>> downloader,
        string successTitle)
    {
        if (!TryValidateRange(out var errorMessage))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Rango inválido", errorMessage);
            return;
        }

        SetLoading(true);
        try
        {
            var result = await downloader(_startDate, _endDate, CancellationToken.None);
            if (!result.IsSuccess || result.File is null)
            {
                await StyledResultModalPage.ShowAsync(this, false, "No se pudo descargar", result.Message);
                return;
            }

            var targetPath = Path.Combine(FileSystem.CacheDirectory, result.File.FileName);
            await File.WriteAllBytesAsync(targetPath, result.File.Content, CancellationToken.None);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Compartir reporte",
                File = new ShareFile(targetPath)
            });

            await StyledResultModalPage.ShowAsync(
                this,
                true,
                successTitle,
                "El archivo está listo. Usa el panel de compartir para guardarlo o enviarlo.");
        }
        catch (Exception ex)
        {
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo compartir", ex.Message);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void OnClosePageClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private sealed record BreakdownViewItem(
        string Name,
        string AmountText,
        string DetailText,
        double Share,
        string ShareText,
        Color BarColor);

    private sealed record TopExpenseViewItem(
        string DayText,
        string Description,
        string Meta,
        string AmountText);

    private sealed record DailyTrendViewItem(
        string DayLabel,
        double BarHeight,
        Color BarColor);

    private sealed record InsightViewItem(string Text);
}
