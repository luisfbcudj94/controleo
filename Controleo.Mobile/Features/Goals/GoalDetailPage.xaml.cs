using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.Goals;

public partial class GoalDetailPage : ContentPage
{
    private readonly IExpenseApiClient _api;
    private readonly string _goalId;
    private SavingsGoalItem? _goal;
    private readonly ObservableCollection<ContributionListItem> _contributions = [];
    private DateOnly _editGoalDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6));
    private string _editGoalPriority = "Media";

    public GoalDetailPage(IExpenseApiClient api, string goalId)
    {
        InitializeComponent();
        _api = api;
        _goalId = goalId;
        ContributionsCollection.ItemsSource = _contributions;
        MoneyFormatHelper.Attach(SimAmountEntry);
        MoneyFormatHelper.Attach(ContribAmountEntry);
        MoneyFormatHelper.Attach(EditGoalAmountEntry);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDetailAsync();
    }

    private void ShowLoading(bool show)
    {
        DetailLoadingIndicator.IsRunning = show;
        DetailLoadingIndicator.IsVisible = show;
        DetailScrollContent.Opacity = show ? 0.4 : 1.0;
    }

    private async Task LoadDetailAsync()
    {
        ShowLoading(true);
        try
        {
            _goal = await _api.GetGoalDetailAsync(_goalId, CancellationToken.None);
            if (_goal is null)
            {
                await StyledResultModalPage.ShowAsync(this, false, "Error", "No se encontró la meta.");
                await Navigation.PopAsync();
                return;
            }

            RenderDetail(_goal);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoalDetail] Load error: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private void RenderDetail(SavingsGoalItem g)
    {
        GoalIconLabel.Text = g.Icon;
        HeroGoalName.Text = g.Name;

        // Keep hidden ring for compat
        var percent = (float)(g.ProgressPercent / 100.0m);
        DetailRingView.Drawable = new DetailProgressRingDrawable(percent);

        // Amount row
        DetailAmountLabel.Text = $"${g.CurrentAmount:N0}";
        HeaderTitle.Text = $"Meta: ${g.TargetAmount:N0}";

        // Progress bar (max width ~300 for card)
        var barWidth = Math.Max(10, 300.0 * (double)g.ProgressPercent / 100.0);
        ProgressBarFill.WidthRequest = barWidth;
        ProgressPercentLabel.Text = $"{g.ProgressPercent:F0}%";
        var remaining = Math.Max(0, g.TargetAmount - g.CurrentAmount);
        ProgressRemainingLabel.Text = $"Faltan ${remaining:N0}";

        // Status badge
        var isCompleted = g.Status == "Completed";
        if (isCompleted)
        {
            DetailStatusLabel.Text = "Meta alcanzada";
            DetailStatusBadge.BackgroundColor = Color.FromArgb("#E8F5E9");
            DetailStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#81C784"));
            StatusDot.BackgroundColor = Color.FromArgb("#4CAF50");
            DetailStatusLabel.TextColor = Color.FromArgb("#2E7D32");
        }
        else if (g.IsOnTrack)
        {
            DetailStatusLabel.Text = "En camino";
            DetailStatusBadge.BackgroundColor = Color.FromArgb("#E8F5E9");
            DetailStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#81C784"));
            StatusDot.BackgroundColor = Color.FromArgb("#4CAF50");
            DetailStatusLabel.TextColor = Color.FromArgb("#2E7D32");
        }
        else
        {
            DetailStatusLabel.Text = "Necesita atención";
            DetailStatusBadge.BackgroundColor = Color.FromArgb("#FFF6E0");
            DetailStatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#F5D88A"));
            StatusDot.BackgroundColor = Color.FromArgb("#E8A800");
            DetailStatusLabel.TextColor = Color.FromArgb("#9A6500");
        }

        // Days label
        DetailDaysLabel.Text = isCompleted
            ? "Completada 🎉"
            : g.DaysRemaining == 0 ? "Fecha: Hoy" : $"Faltan {g.DaysRemaining} días · {g.TargetDate:dd/MM/yyyy}";

        // Stats
        SuggestedStatLabel.Text = $"${g.SuggestedMonthlyContribution:N0}";
        SuggestedSubLabel.Text = "por mes";
        ProjectionStatLabel.Text = g.ProjectedCompletionDate is { } pd ? pd.ToString("MMM yyyy", new CultureInfo("es-CO")) : "Sin datos";
        ProjectionSubLabel.Text = "al ritmo actual";
        PriorityStatLabel.Text = g.Priority;
        PrioritySubLabel.Text = g.Priority switch
        {
            "Alta" => "requiere acción",
            "Media" => "buen ritmo",
            _ => "a tu tiempo"
        };

        AddContributionButton.IsVisible = g.Status == "Active";

        // Contributions
        _contributions.Clear();
        NoContributionsLabel.IsVisible = g.Contributions.Count == 0;
        foreach (var c in g.Contributions)
        {
            _contributions.Add(new ContributionListItem
            {
                Id = c.Id,
                AmountLabel = $"${c.Amount:N0}",
                DateLabel = $"{c.Date:dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(c.Note) ? "" : $" • {c.Note}")
            });
        }
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        Navigation.PopAsync();
    }

    private async void OnDeleteGoalClicked(object? sender, EventArgs e)
    {
        var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar meta", $"¿Eliminar '{_goal?.Name}'? Esta acción no se puede deshacer.");
        if (!confirmed) return;

        ShowLoading(true);
        var result = await _api.DeleteGoalAsync(_goalId, CancellationToken.None);
        ShowLoading(false);
        await StyledResultModalPage.ShowAsync(this, result.IsSuccess, result.IsSuccess ? "Eliminada" : "Error", result.Message);
        if (result.IsSuccess) await Navigation.PopAsync();
    }

    private void OnEditGoalClicked(object? sender, EventArgs e)
    {
        if (_goal is null) return;
        EditGoalNameEntry.Text = _goal.Name;
        EditGoalAmountEntry.Text = $"{_goal.TargetAmount:N0}";
        _editGoalDate = _goal.TargetDate;
        _editGoalPriority = _goal.Priority;
        EditGoalDateLabel.Text = _editGoalDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
        UpdateEditPriorityButtons();
        EditGoalOverlay.IsVisible = true;
        EditGoalNameEntry.Focus();
    }

    private void OnEditGoalOverlayClose(object? sender, EventArgs e)
    {
        EditGoalOverlay.IsVisible = false;
    }

    private async void OnPickEditGoalDateTapped(object? sender, TappedEventArgs e)
    {
        var minDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var maxDate = DateOnly.FromDateTime(DateTime.Today.AddYears(10));
        var picked = await CalendarDateModalPage.PickAsync(this, "Fecha objetivo", _editGoalDate, minDate, maxDate);
        if (picked is null) return;
        _editGoalDate = picked.Value;
        EditGoalDateLabel.Text = _editGoalDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    private void OnEditPrioritySelected(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string priority)
        {
            _editGoalPriority = priority;
            UpdateEditPriorityButtons();
        }
    }

    private void UpdateEditPriorityButtons()
    {
        EditPriorityHigh.BackgroundColor = _editGoalPriority == "Alta" ? Color.FromArgb("#FDE3E5") : Color.FromArgb("#EFF3F1");
        EditPriorityMedium.BackgroundColor = _editGoalPriority == "Media" ? Color.FromArgb("#FFF5E9") : Color.FromArgb("#EFF3F1");
        EditPriorityLow.BackgroundColor = _editGoalPriority == "Baja" ? Color.FromArgb("#D8F3DC") : Color.FromArgb("#EFF3F1");
    }

    private async void OnConfirmEditGoalClicked(object? sender, EventArgs e)
    {
        if (_goal is null) return;
        var name = EditGoalNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "El nombre es requerido.");
            return;
        }

        if (!MoneyFormatHelper.TryParse(EditGoalAmountEntry.Text, out var amount) || amount <= 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "El monto debe ser mayor a cero.");
            return;
        }

        EditGoalOverlay.IsVisible = false;
        ShowLoading(true);
        var request = new GoalUpsertRequest(name, _goal.Icon, amount, _editGoalDate, _editGoalPriority, _goal.Status);
        var result = await _api.UpdateGoalAsync(_goalId, request, CancellationToken.None);
        ShowLoading(false);
        await StyledResultModalPage.ShowAsync(this, result.IsSuccess, result.IsSuccess ? "Meta actualizada" : "Error", result.Message);
        if (result.IsSuccess) await LoadDetailAsync();
    }

    private static readonly string[] MotivationMessages =
    [
        "¡Cada peso te acerca a tu sueño!",
        "¡Tú puedes! Un paso más hacia tu meta.",
        "¡Excelente decisión! El ahorro es poder.",
        "¡Sigue así! Tu futuro yo te lo agradecerá.",
        "💪 ¡Disciplina hoy, libertad mañana!",
        "🌟 ¡Genial! Cada aporte suma.",
    ];

    private void OnAddContributionClicked(object? sender, EventArgs e)
    {
        ContribAmountEntry.Text = "";
        ContribNoteEntry.Text = "";

        var motivation = MotivationMessages[Random.Shared.Next(MotivationMessages.Length)];
        ContribMotivationLabel.Text = motivation;

        if (_goal is not null && _goal.SuggestedMonthlyContribution > 0)
        {
            ContribSuggestedLabel.Text = $"💡 Aporte sugerido: ${_goal.SuggestedMonthlyContribution:N0}/mes";
        }
        else
        {
            ContribSuggestedLabel.Text = "💡 ¡Cualquier monto ayuda!";
        }

        ContributionOverlay.IsVisible = true;
        ContribAmountEntry.Focus();
    }

    private void OnContributionOverlayClose(object? sender, EventArgs e)
    {
        ContributionOverlay.IsVisible = false;
    }

    private async void OnConfirmContributionClicked(object? sender, EventArgs e)
    {
        if (!MoneyFormatHelper.TryParse(ContribAmountEntry.Text, out var amount) || amount <= 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "El monto del aporte debe ser mayor a cero.");
            return;
        }

        var note = ContribNoteEntry.Text?.Trim();
        var request = new GoalContributionRequest(amount, DateOnly.FromDateTime(DateTime.Today), string.IsNullOrWhiteSpace(note) ? null : note);
        ContributionOverlay.IsVisible = false;
        ShowLoading(true);

        var result = await _api.AddGoalContributionAsync(_goalId, request, CancellationToken.None);
        ShowLoading(false);
        await StyledResultModalPage.ShowAsync(this, result.IsSuccess, result.IsSuccess ? "¡Aporte registrado! 🎉" : "Error", result.Message);
        if (result.IsSuccess) await LoadDetailAsync();
    }

    private async void OnEditContributionClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is ContributionListItem item)
        {
            var newAmountStr = await DisplayPromptAsync("Editar Aporte", $"Monto actual: {item.AmountLabel}", "Guardar", "Cancelar", "Nuevo monto", -1, Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(newAmountStr)) return;

            var cleanAmount = newAmountStr.Replace(".", "").Replace(",", ".");
            if (!decimal.TryParse(cleanAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var newAmount) || newAmount <= 0)
            {
                await StyledResultModalPage.ShowAsync(this, false, "Error", "El monto debe ser mayor a cero.");
                return;
            }

            // Delete old + create new with same date
            ShowLoading(true);
            var deleteResult = await _api.DeleteGoalContributionAsync(_goalId, item.Id, CancellationToken.None);
            if (deleteResult.IsSuccess)
            {
                var addResult = await _api.AddGoalContributionAsync(_goalId, new GoalContributionRequest(newAmount, DateOnly.FromDateTime(DateTime.Today), null), CancellationToken.None);
                await StyledResultModalPage.ShowAsync(this, addResult.IsSuccess, addResult.IsSuccess ? "Aporte actualizado" : "Error", addResult.Message);
            }
            else
            {
                await StyledResultModalPage.ShowAsync(this, false, "Error", deleteResult.Message);
            }
            await LoadDetailAsync();
        }
    }

    private async void OnDeleteContributionClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string contributionId)
        {
            var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Eliminar aporte", "¿Eliminar este aporte?");
            if (!confirmed) return;

            ShowLoading(true);
            var result = await _api.DeleteGoalContributionAsync(_goalId, contributionId, CancellationToken.None);
            if (result.IsSuccess) await LoadDetailAsync();
        }
    }

    private async void OnSimulateClicked(object? sender, EventArgs e)
    {
        if (!MoneyFormatHelper.TryParse(SimAmountEntry.Text, out var amount) || amount <= 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "Ingresa un monto válido.");
            return;
        }

        var request = new GoalSimulationRequest("ChangeAmount", amount);
        ShowLoading(true);
        var result = await _api.SimulateGoalAsync(_goalId, request, CancellationToken.None);
        ShowLoading(false);
        if (result is not null)
        {
            SimResultLabel.Text = result.Description;
            SimResultBorder.IsVisible = true;
            SimResultBorder.BackgroundColor = Color.FromArgb(result.Feasibility == "Viable" ? "#EAF6EE" : "#FFF5E9");
        }
        else
        {
            await StyledResultModalPage.ShowAsync(this, false, "Error", "No fue posible simular el escenario.");
        }
    }
}

public sealed class ContributionListItem
{
    public string Id { get; set; } = "";
    public string AmountLabel { get; set; } = "";
    public string DateLabel { get; set; } = "";
}

public sealed class DetailProgressRingDrawable : IDrawable
{
    private readonly float _progress;
    public DetailProgressRingDrawable(float progress) => _progress = Math.Clamp(progress, 0f, 1f);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2;
        var cy = dirtyRect.Height / 2;
        var radius = Math.Min(cx, cy) - 10;
        var strokeWidth = 12f;

        // Track (light)
        canvas.StrokeColor = Color.FromArgb("#E4EAE6");
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

        // Center percentage text
        canvas.FontColor = Color.FromArgb("#1A2E23");
        canvas.FontSize = 28;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var percentText = $"{(_progress * 100):F0}%";
        canvas.DrawString(percentText, cx - 40, cy - 16, 80, 32, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
