using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.SmartScore;

public partial class SmartScorePage : ContentPage
{
    private static readonly CultureInfo EsCulture = CultureInfo.GetCultureInfo("es-CO");

    private readonly IExpenseApiClient _apiClient;
    private readonly SpeedometerDrawable _gaugeDrawable;
    private bool _loadedOnce;

    private DateOnly _startDate;
    private DateOnly _endDate;

    public SmartScorePage(IExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        var today = DateOnly.FromDateTime(DateTime.Today);
        _startDate = new DateOnly(today.Year, today.Month, 1);
        _endDate = today;

        _gaugeDrawable = new SpeedometerDrawable();
        GaugeView.Drawable = _gaugeDrawable;

        ResetState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loadedOnce) return;
        _loadedOnce = true;
        await LoadAllAsync(forceRecommendationRefresh: true);
    }

    private async Task LoadAllAsync(bool forceRecommendationRefresh = false)
    {
        SetLoading(true);
        try
        {
            var scoreTask = _apiClient.GetFinancialScoreAsync(_startDate, _endDate, CancellationToken.None);
            var recommendationsTask = _apiClient.GetFinancialRecommendationsAsync(_startDate, _endDate, forceRecommendationRefresh, CancellationToken.None);
            var historyTask = _apiClient.GetFinancialScoreHistoryAsync(6, CancellationToken.None);

            var score = await scoreTask;
            BindScore(score);

            var recommendations = await recommendationsTask;
            var history = await historyTask;
            BindRecommendations(recommendations, history, score);

            StatusLabel.Text = $"Periodo: {_startDate:dd MMM yyyy} – {_endDate:dd MMM yyyy}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error al cargar datos: {ex.Message}";
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void BindScore(FinancialScoreResult score)
    {
        if (!score.IsSuccess)
        {
            _gaugeDrawable.Score = 0;
            GaugeView.Invalidate();
            ScoreValueLabel.Text = "--";
            ScoreValueLabel.TextColor = Color.FromArgb("#4C6256");
            ScoreLevelLabel.Text = "Score no disponible";
            ScoreLevelLabel.TextColor = Color.FromArgb("#78909C");
            ScoreLevelBadge.BackgroundColor = Color.FromArgb("#F0F4F2");
            ScoreTrendLabel.Text = score.Message;
            ScoreConfidenceLabel.Text = string.Empty;
            IncomeHintLabel.Text = "Agrega ingreso mensual desde tu perfil para mejorar la precisión.";
            DriversCollection.ItemsSource = Array.Empty<DriverViewItem>();
            return;
        }

        _gaugeDrawable.Score = score.Score;
        GaugeView.Invalidate();

        ScoreValueLabel.Text = score.Score.ToString(CultureInfo.InvariantCulture);
        ScoreValueLabel.TextColor = ResolveScoreColor(score.Score);
        ScoreLevelLabel.Text = ResolveScoreLevel(score.Score);

        var (badgeBg, badgeText) = ResolveScoreBadgeColors(score.Score);
        ScoreLevelBadge.BackgroundColor = badgeBg;
        ScoreLevelLabel.TextColor = badgeText;

        ScoreTrendLabel.Text = $"{FormatTrend(score.Trend)} · Cambio: {FormatSignedPercentage(score.AmountChangePercentage)}";
        ScoreConfidenceLabel.Text = $"Confianza del modelo: {score.Confidence * 100m:0}%";

        IncomeHintLabel.Text = score.MonthlyIncome is > 0
            ? $"Gasto proyectado: {(score.SpendingToIncomeRatio ?? 0m) * 100m:0.##}% del ingreso mensual."
            : "Sin ingreso mensual registrado. Puedes agregarlo en Perfil para recomendaciones más precisas.";

        DriversCollection.ItemsSource = (score.Drivers ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => new DriverViewItem(d.Trim()))
            .ToArray();
    }

    private void BindRecommendations(
        FinancialRecommendationsResult recommendations,
        IReadOnlyList<FinancialScoreHistoryItem> history,
        FinancialScoreResult currentScore)
    {
        if (!recommendations.IsSuccess)
        {
            RecommendationSourceLabel.Text = recommendations.Message;
            RecommendationsCollection.ItemsSource = Array.Empty<RecommendationViewItem>();
            ScoreHistoryHintLabel.Text = BuildHistoryHint(history, currentScore);
            return;
        }

        RecommendationSourceLabel.Text = $"Fuente: {recommendations.SourceLabel} · Prioridad: {recommendations.Priority}";
        RecommendationsCollection.ItemsSource = (recommendations.Recommendations ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => new RecommendationViewItem(r.Trim()))
            .ToArray();

        ScoreHistoryHintLabel.Text = BuildHistoryHint(history, currentScore);
    }

    private void ResetState()
    {
        _gaugeDrawable.Score = 0;
        GaugeView.Invalidate();
        ScoreValueLabel.Text = "--";
        ScoreValueLabel.TextColor = Color.FromArgb("#4C6256");
        ScoreLevelLabel.Text = "Calculando...";
        ScoreLevelLabel.TextColor = Color.FromArgb("#78909C");
        ScoreLevelBadge.BackgroundColor = Color.FromArgb("#F0F4F2");
        ScoreTrendLabel.Text = string.Empty;
        ScoreConfidenceLabel.Text = string.Empty;
        IncomeHintLabel.Text = string.Empty;
        DriversCollection.ItemsSource = Array.Empty<DriverViewItem>();
        RecommendationSourceLabel.Text = "Recomendaciones no cargadas.";
        RecommendationsCollection.ItemsSource = Array.Empty<RecommendationViewItem>();
        ScoreHistoryHintLabel.Text = string.Empty;
        StatusLabel.Text = string.Empty;
    }

    private static Color ResolveScoreColor(int score)
    {
        if (score >= 80) return Color.FromArgb("#1B4332");
        if (score >= 60) return Color.FromArgb("#8A4D11");
        return Color.FromArgb("#8E1D13");
    }

    private static (Color Background, Color Text) ResolveScoreBadgeColors(int score)
    {
        if (score >= 80) return (Color.FromArgb("#EAF6EE"), Color.FromArgb("#1B4332"));
        if (score >= 60) return (Color.FromArgb("#FFF8E1"), Color.FromArgb("#8A4D11"));
        if (score >= 40) return (Color.FromArgb("#FFF3E0"), Color.FromArgb("#E65100"));
        return (Color.FromArgb("#FFEBEE"), Color.FromArgb("#8E1D13"));
    }

    private static string ResolveScoreLevel(int score)
    {
        if (score >= 80) return "Excelente";
        if (score >= 60) return "Bueno";
        if (score >= 40) return "Regular";
        return "En riesgo";
    }

    private static string FormatTrend(string? trend)
    {
        return trend?.Trim().ToLowerInvariant() switch
        {
            "up" => "↗ Tendencia positiva",
            "down" => "↘ Tendencia en riesgo",
            _ => "→ Tendencia estable"
        };
    }

    private static string FormatSignedPercentage(decimal value)
        => value > 0 ? $"+{value:0.##}%" : $"{value:0.##}%";

    private static string BuildHistoryHint(IReadOnlyList<FinancialScoreHistoryItem> history, FinancialScoreResult currentScore)
    {
        var ordered = (history ?? []).OrderByDescending(h => h.GeneratedAtUtc).ToArray();
        if (ordered.Length == 0)
            return $"Sin historial previo. Score actual: {currentScore.Score}.";

        var latest = ordered[0];
        if (ordered.Length == 1)
            return $"Último corte: {latest.Score} ({latest.GeneratedAtUtc:dd MMM}).";

        var previous = ordered[1];
        var delta = latest.Score - previous.Score;
        var signedDelta = delta > 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture);
        return $"Historial: {latest.Score} ({signedDelta} vs corte anterior).";
    }

    private void SetLoading(bool isLoading) => LoadingOverlay.IsVisible = isLoading;

    private async void OnRefreshRecommendationsClicked(object? sender, EventArgs e)
    {
        SetLoading(true);
        try
        {
            await LoadAllAsync(forceRecommendationRefresh: true);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void OnClosePageClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    private sealed record DriverViewItem(string Text);
    private sealed record RecommendationViewItem(string Text);

    private sealed class SpeedometerDrawable : IDrawable
    {
        private const float ArcThickness = 30f;
        private const float StartDeg = 150f;
        private const float EndDeg = 390f;
        private const float SweepDeg = EndDeg - StartDeg;

        private static readonly Color[] ArcColors =
        [
            Color.FromArgb("#C62828"),
            Color.FromArgb("#CF2F2B"),
            Color.FromArgb("#D9382E"),
            Color.FromArgb("#E64A19"),
            Color.FromArgb("#F57C00"),
            Color.FromArgb("#FB8C00"),
            Color.FromArgb("#FFA000"),
            Color.FromArgb("#FBC02D"),
            Color.FromArgb("#D4C533"),
            Color.FromArgb("#A7C53A"),
            Color.FromArgb("#7CB342"),
            Color.FromArgb("#5EA63E"),
            Color.FromArgb("#43A047"),
            Color.FromArgb("#2E7D32")
        ];

        private static readonly int[] TickValues = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        private static readonly (float Fraction, string Label, Color Color)[] ZoneLabels =
        [
            (0.16f, "Riesgo", Color.FromArgb("#E57373")),
            (0.50f, "Balance", Color.FromArgb("#FFB74D")),
            (0.84f, "Excelente", Color.FromArgb("#81C784"))
        ];

        public int Score { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var width = dirtyRect.Width;
            var height = dirtyRect.Height;
            if (width < 40 || height < 40) return;

            var cx = width / 2f;
            var cy = height * 0.86f;
            var radius = MathF.Min(width * 0.39f, height * 0.70f);
            if (radius < 36f) return;

            canvas.Antialias = true;

            // Base track
            DrawArcPolyline(canvas, cx, cy, radius, StartDeg, EndDeg, Color.FromArgb("#223743"), ArcThickness, LineCap.Round, 140);

            // Soft outer glow
            DrawArcPolyline(canvas, cx, cy, radius + 5f, StartDeg, EndDeg, Color.FromArgb("#2A4956").WithAlpha(0.45f), 3f, LineCap.Round, 120);

            // Colored segments
            var segmentSweep = SweepDeg / ArcColors.Length;
            for (var i = 0; i < ArcColors.Length; i++)
            {
                var segStart = StartDeg + (i * segmentSweep);
                var segEnd = segStart + segmentSweep;
                var cap = (i == 0 || i == ArcColors.Length - 1) ? LineCap.Round : LineCap.Butt;
                DrawArcPolyline(canvas, cx, cy, radius, segStart, segEnd, ArcColors[i], ArcThickness, cap, 18);
            }

            // Tick marks
            foreach (var tick in TickValues)
            {
                var tickFraction = tick / 100f;
                var tickDeg = StartDeg + (tickFraction * SweepDeg);
                var outer = PointOnArc(cx, cy, radius + (ArcThickness * 0.56f) + 8f, tickDeg);
                var inner = PointOnArc(cx, cy, radius + (ArcThickness * 0.56f) - (tick % 20 == 0 ? 4f : 0f), tickDeg);

                canvas.StrokeColor = tick % 20 == 0
                    ? Color.FromArgb("#D7E4EA")
                    : Color.FromArgb("#8AA2AD").WithAlpha(0.85f);
                canvas.StrokeSize = tick % 20 == 0 ? 2f : 1.3f;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawLine(inner.X, inner.Y, outer.X, outer.Y);
            }

            // End labels
            canvas.FontColor = Color.FromArgb("#9BB0B8");
            canvas.FontSize = 10f;
            var left = PointOnArc(cx, cy, radius + ArcThickness + 18f, StartDeg);
            var right = PointOnArc(cx, cy, radius + ArcThickness + 18f, EndDeg);
            canvas.DrawString("0", left.X, left.Y + 5f, HorizontalAlignment.Center);
            canvas.DrawString("100", right.X, right.Y + 5f, HorizontalAlignment.Center);

            // Zone labels
            foreach (var zone in ZoneLabels)
            {
                var deg = StartDeg + (zone.Fraction * SweepDeg);
                var p = PointOnArc(cx, cy, radius - ArcThickness + 2f, deg);
                canvas.FontColor = zone.Color.WithAlpha(0.78f);
                canvas.FontSize = 9f;
                canvas.DrawString(zone.Label, p.X, p.Y, HorizontalAlignment.Center);
            }

            // Needle geometry
            var clampedScore = Math.Clamp(Score, 0, 100);
            var scoreFraction = clampedScore / 100f;
            var needleDeg = StartDeg + (scoreFraction * SweepDeg);
            var needleRad = needleDeg * MathF.PI / 180f;

            var dirX = MathF.Cos(needleRad);
            var dirY = MathF.Sin(needleRad);
            var perpX = -dirY;
            var perpY = dirX;

            var tip = PointOnArc(cx, cy, radius - (ArcThickness * 0.53f), needleDeg);
            var baseCenterX = cx - (dirX * 7f);
            var baseCenterY = cy - (dirY * 7f);
            var baseHalf = 6f;
            var tail = PointOnArc(cx, cy, 12f, needleDeg + 180f);

            var leftBase = new PointF(baseCenterX + (perpX * baseHalf), baseCenterY + (perpY * baseHalf));
            var rightBase = new PointF(baseCenterX - (perpX * baseHalf), baseCenterY - (perpY * baseHalf));

            var shadow = new PathF();
            shadow.MoveTo(tip.X + 1.6f, tip.Y + 1.6f);
            shadow.LineTo(leftBase.X + 1.6f, leftBase.Y + 1.6f);
            shadow.LineTo(tail.X + 1.6f, tail.Y + 1.6f);
            shadow.LineTo(rightBase.X + 1.6f, rightBase.Y + 1.6f);
            shadow.Close();
            canvas.FillColor = Color.FromArgb("#28000000");
            canvas.FillPath(shadow);

            var needle = new PathF();
            needle.MoveTo(tip.X, tip.Y);
            needle.LineTo(leftBase.X, leftBase.Y);
            needle.LineTo(tail.X, tail.Y);
            needle.LineTo(rightBase.X, rightBase.Y);
            needle.Close();
            canvas.FillColor = Color.FromArgb("#1A2433");
            canvas.FillPath(needle);

            // Needle highlight
            canvas.StrokeColor = Color.FromArgb("#DDE5EA").WithAlpha(0.5f);
            canvas.StrokeSize = 1.1f;
            canvas.DrawLine(cx, cy, tip.X, tip.Y);

            // Pivot (three-layer chrome)
            canvas.FillColor = Color.FromArgb("#182230");
            canvas.FillCircle(cx, cy, 11f);
            canvas.FillColor = Color.FromArgb("#4B6170");
            canvas.FillCircle(cx, cy, 7.5f);
            canvas.FillColor = Color.FromArgb("#F6FBFF");
            canvas.FillCircle(cx, cy, 3f);
        }

        private static PointF PointOnArc(float cx, float cy, float radius, float degrees)
        {
            var radians = degrees * MathF.PI / 180f;
            return new PointF(
                cx + (radius * MathF.Cos(radians)),
                cy + (radius * MathF.Sin(radians)));
        }

        private static void DrawArcPolyline(
            ICanvas canvas,
            float cx,
            float cy,
            float radius,
            float startDeg,
            float endDeg,
            Color color,
            float thickness,
            LineCap cap,
            int steps)
        {
            canvas.StrokeColor = color;
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = cap;

            var previous = PointOnArc(cx, cy, radius, startDeg);
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var deg = startDeg + ((endDeg - startDeg) * t);
                var current = PointOnArc(cx, cy, radius, deg);
                canvas.DrawLine(previous.X, previous.Y, current.X, current.Y);
                previous = current;
            }
        }
    }
}