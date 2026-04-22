using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.Goals;

public partial class GoalsPage : ContentPage
{
    private readonly IExpenseApiClient _api;
    private readonly IAuthService? _authService;
    private readonly IGoalNotificationService? _goalNotificationService;
    private readonly ObservableCollection<GoalListItem> _goals = [];
    private string _selectedIcon = "🎯";
    private string _selectedPriority = "Media";
    private string? _editingGoalId;
    private DateOnly _selectedGoalDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));

    private static readonly string[] PastelColors = ["#D8F3DC", "#D4EAFF", "#FFE5D4", "#E8DEFF", "#FFF0D4", "#DFEEF7"];
    private static readonly Border[] EmptyIconBorders = [];

    public GoalsPage(IExpenseApiClient api, IAuthService? authService = null, IGoalNotificationService? goalNotificationService = null)
    {
        InitializeComponent();
        _api = api;
        _authService = authService;
        _goalNotificationService = goalNotificationService;
        GoalsCollection.ItemsSource = _goals;
        MoneyFormatHelper.Attach(GoalAmountEntry);
        UpdatePriorityButtons();
        UpdateIconSelection();
        UpdateGoalDateLabel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadGoalsAsync();
    }

    private void ShowLoading(bool show)
    {
        LoadingIndicator.IsRunning = show;
        LoadingIndicator.IsVisible = show;
        ContentContainer.IsVisible = !show;
    }

    private async Task LoadGoalsAsync()
    {
        ShowLoading(true);
        try
        {
            var goals = await _api.GetGoalsAsync(CancellationToken.None);
            var alerts = await _api.GetGoalAlertsAsync(CancellationToken.None);

            _goals.Clear();
            int colorIndex = 0;
            foreach (var g in goals)
            {
                var color = PastelColors[colorIndex % PastelColors.Length];
                colorIndex++;
                _goals.Add(GoalListItem.From(g, color));
            }

            // Hero summary
            var activeGoals = goals.Where(g => g.Status == "Active").ToList();
            var totalTarget = goals.Sum(g => g.TargetAmount);
            var totalCurrent = goals.Sum(g => g.CurrentAmount);
            var overallPercent = totalTarget > 0 ? Math.Min(100m, Math.Round(totalCurrent / totalTarget * 100m, 0)) : 0m;
            var onTrackCount = activeGoals.Count(g => g.IsOnTrack);

            if (goals.Count > 0)
            {
                HeroTitleLabel.Text = $"{onTrackCount} de {activeGoals.Count} metas en camino";
                HeroSubtitleLabel.Text = activeGoals.Count == 0 ? "Todas completadas 🎉" : "¡Sigue así, vas muy bien!";
                HeroAmountLabel.Text = $"${totalCurrent:N0} ahorrados de ${totalTarget:N0}";
                HeroPercentLabel.Text = $"{overallPercent:F0}%";
            }
            else
            {
                HeroTitleLabel.Text = "Sin metas aún";
                HeroSubtitleLabel.Text = "Crea tu primera meta y empieza a ahorrar";
                HeroAmountLabel.Text = "";
                HeroPercentLabel.Text = "0%";
            }

HeroRingView.Drawable = new GoalProgressRingDrawable((float)(overallPercent / 100.0m));
            HeroRingView.Invalidate();

            EmptyStateContainer.IsVisible = goals.Count == 0;
            GoalsCollection.IsVisible = goals.Count > 0;

            // Alerts
            RenderAlerts(alerts);

            // Sync goal notifications
            await SyncGoalNotificationsAsync(goals);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoalsPage] Load error: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private async Task SyncGoalNotificationsAsync(IReadOnlyList<SavingsGoalItem> goals)
    {
        try
        {
            if (_goalNotificationService is null || _authService is null) return;
            var isPremium = _authService.IsCurrentUserPremium;
            await _goalNotificationService.EnsurePermissionAsync(isPremium, CancellationToken.None);
            await _goalNotificationService.SyncGoalRemindersAsync(isPremium, goals, CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoalsPage] Notification sync error: {ex.Message}");
        }
    }

    private void RenderAlerts(IReadOnlyList<GoalAlertItem> alerts)
    {
        AlertsContainer.Children.Clear();
        AlertsContainer.IsVisible = alerts.Count > 0;
        foreach (var a in alerts.Take(3))
        {
            var bgColor = a.Severity == "high" ? Color.FromArgb("#FDE3E5") : Color.FromArgb("#FFF5E9");
            var textColor = a.Severity == "high" ? Color.FromArgb("#A11D2A") : Color.FromArgb("#8A4D11");
            var border = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                StrokeThickness = 0,
                BackgroundColor = bgColor,
                Padding = new Thickness(12, 8)
            };
            border.Content = new Label { Text = $"⚠️ {a.Message}", FontSize = 12, TextColor = textColor, LineBreakMode = LineBreakMode.WordWrap };
            AlertsContainer.Children.Add(border);
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadGoalsAsync();
        GoalsRefreshView.IsRefreshing = false;
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }

    private void OnCreateGoalClicked(object? sender, EventArgs e)
    {
        _editingGoalId = null;
        OverlayTitle.Text = "Nueva Meta";
        GoalNameEntry.Text = "";
        GoalAmountEntry.Text = "";
        _selectedGoalDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));
        UpdateGoalDateLabel();
        _selectedIcon = "🎯";
        _selectedPriority = "Media";
        UpdateIconSelection();
        UpdatePriorityButtons();
        GoalEditOverlay.IsVisible = true;
    }

    private void OnOverlayCloseClicked(object? sender, EventArgs e)
    {
        GoalEditOverlay.IsVisible = false;
    }

    private async void OnPickGoalDateTapped(object? sender, TappedEventArgs e)
    {
        var minDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var maxDate = DateOnly.FromDateTime(DateTime.Today.AddYears(10));
        var picked = await CalendarDateModalPage.PickAsync(this, "Fecha objetivo", _selectedGoalDate, minDate, maxDate);
        if (picked is null) return;
        _selectedGoalDate = picked.Value;
        UpdateGoalDateLabel();
    }

    private void UpdateGoalDateLabel()
    {
        GoalDateLabel.Text = _selectedGoalDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    private void OnIconSelected(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string icon)
        {
            _selectedIcon = icon;
            UpdateIconSelection();
        }
    }

    private void UpdateIconSelection()
    {
        var icons = new[] { "🎯", "🏠", "✈️", "🚗", "🎓", "💎" };
        var borders = new[] { Icon0, Icon1, Icon2, Icon3, Icon4, Icon5 };
        for (int i = 0; i < icons.Length; i++)
        {
            borders[i].Stroke = icons[i] == _selectedIcon
                ? new SolidColorBrush(Color.FromArgb("#2D6A4F"))
                : new SolidColorBrush(Color.FromArgb("#DCE5DF"));
            borders[i].BackgroundColor = icons[i] == _selectedIcon
                ? Color.FromArgb("#D8F3DC")
                : Colors.White;
        }
    }

    private void OnPrioritySelected(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string priority)
        {
            _selectedPriority = priority;
            UpdatePriorityButtons();
        }
    }

    private void UpdatePriorityButtons()
    {
        PriorityHigh.BackgroundColor = _selectedPriority == "Alta" ? Color.FromArgb("#FDE3E5") : Color.FromArgb("#EFF3F1");
        PriorityMedium.BackgroundColor = _selectedPriority == "Media" ? Color.FromArgb("#FFF5E9") : Color.FromArgb("#EFF3F1");
        PriorityLow.BackgroundColor = _selectedPriority == "Baja" ? Color.FromArgb("#D8F3DC") : Color.FromArgb("#EFF3F1");
    }

    private async void OnSaveGoalClicked(object? sender, EventArgs e)
    {
        var name = GoalNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "El nombre de la meta es requerido.");
            return;
        }

        if (!MoneyFormatHelper.TryParse(GoalAmountEntry.Text, out var amount) || amount <= 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "El monto objetivo debe ser mayor a cero.");
            return;
        }

        var targetDate = _selectedGoalDate;
        var request = new GoalUpsertRequest(name, _selectedIcon, amount, targetDate, _selectedPriority, _editingGoalId is not null ? "Active" : null);

        GoalEditOverlay.IsVisible = false;
        ShowLoading(true);

        OperationResult result;
        if (_editingGoalId is not null)
            result = await _api.UpdateGoalAsync(_editingGoalId, request, CancellationToken.None);
        else
            result = await _api.CreateGoalAsync(request, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(this, result.IsSuccess, result.IsSuccess ? "¡Listo!" : "Error", result.Message);
        if (result.IsSuccess) await LoadGoalsAsync();
    }

    private async void OnGoalTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string goalId)
        {
            var detailPage = new GoalDetailPage(_api, goalId);
            await Navigation.PushAsync(detailPage);
        }
    }

    private async void OnEditGoalClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string goalId)
        {
            var goal = await _api.GetGoalDetailAsync(goalId, CancellationToken.None);
            if (goal is null) return;

            _editingGoalId = goalId;
            OverlayTitle.Text = "Editar Meta";
            GoalNameEntry.Text = goal.Name;
            MoneyFormatHelper.TryParse($"{goal.TargetAmount}", out _);
            GoalAmountEntry.Text = $"{goal.TargetAmount:N0}";
            _selectedGoalDate = goal.TargetDate;
            UpdateGoalDateLabel();
            _selectedIcon = goal.Icon;
            _selectedPriority = goal.Priority;
            UpdateIconSelection();
            UpdatePriorityButtons();
            GoalEditOverlay.IsVisible = true;
        }
    }

    private async void OnDeleteGoalClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string goalId)
        {
            var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar meta", "¿Eliminar esta meta? Esta acción no se puede deshacer.");
            if (!confirmed) return;

            var result = await _api.DeleteGoalAsync(goalId, CancellationToken.None);
            await StyledResultModalPage.ShowAsync(this, result.IsSuccess, result.IsSuccess ? "Eliminada" : "Error", result.Message);
            if (result.IsSuccess) await LoadGoalsAsync();
        }
    }
}

// View model for list items
public sealed class GoalListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "🎯";
    public string Subtitle { get; set; } = "";
    public string AmountLabel { get; set; } = "";
    public string PercentLabel { get; set; } = "0%";
    public string TimeLabel { get; set; } = "";
    public string SuggestedLabel { get; set; } = "";
    public string StatusBadge { get; set; } = "";
    public Color StatusBadgeColor { get; set; } = Colors.Transparent;
    public Color StatusBadgeTextColor { get; set; } = Colors.Black;
    public Color ProgressColor { get; set; } = Color.FromArgb("#2D6A4F");
    public Color CardColor { get; set; } = Color.FromArgb("#D8F3DC");
    public double ProgressBarWidth { get; set; }

    private const double MaxBarWidth = 260;

    public static GoalListItem From(SavingsGoalItem g, string cardColorHex)
    {
        var percent = (double)g.ProgressPercent;
        var progressColor = percent >= 75 ? "#2D6A4F" : percent >= 50 ? "#40916C" : percent >= 25 ? "#E6A817" : "#D4765A";
        var isOnTrack = g.IsOnTrack;
        var isCompleted = g.Status == "Completed";

        return new GoalListItem
        {
            Id = g.Id,
            Name = g.Name,
            Icon = g.Icon,
            Subtitle = isCompleted ? "¡Meta alcanzada! 🎉" : $"${g.CurrentAmount:N0} de ${g.TargetAmount:N0}",
            AmountLabel = $"${g.TargetAmount:N0}",
            PercentLabel = $"{percent:F0}%",
            TimeLabel = isCompleted ? "Completada" : g.DaysRemaining == 0 ? "Hoy" : $"Faltan {g.DaysRemaining} días",
            SuggestedLabel = isCompleted ? "" : g.SuggestedMonthlyContribution > 0 ? $"~${g.SuggestedMonthlyContribution:N0}/mes" : "",
            StatusBadge = isCompleted ? "✓ Completada" : isOnTrack ? "En camino" : "Atención",
            StatusBadgeColor = Color.FromArgb(isCompleted ? "#D8F3DC" : isOnTrack ? "#D8F3DC" : "#FFF5E9"),
            StatusBadgeTextColor = Color.FromArgb(isCompleted ? "#1B4332" : isOnTrack ? "#1B4332" : "#8A4D11"),
            ProgressColor = Color.FromArgb(progressColor),
            CardColor = Color.FromArgb(cardColorHex),
            ProgressBarWidth = Math.Max(4, MaxBarWidth * percent / 100.0)
        };
    }
}

// Custom drawable for the progress ring
public sealed class GoalProgressRingDrawable : IDrawable
{
    private readonly float _progress;
    public GoalProgressRingDrawable(float progress) => _progress = Math.Clamp(progress, 0f, 1f);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2;
        var cy = dirtyRect.Height / 2;
        var radius = Math.Min(cx, cy) - 6;
        var strokeWidth = 7f;

        // Track
        canvas.StrokeColor = Color.FromArgb("#DCE5DF");
        canvas.StrokeSize = strokeWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawCircle(cx, cy, radius);

        // Progress arc
        if (_progress > 0.001f)
        {
            var color = _progress >= 0.75f ? "#2D6A4F" : _progress >= 0.5f ? "#40916C" : _progress >= 0.25f ? "#E6A817" : "#D4765A";
            canvas.StrokeColor = Color.FromArgb(color);
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Round;
            var sweepAngle = _progress * 360f;
            canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2, 270, 270 - sweepAngle, false, false);
        }
    }
}
