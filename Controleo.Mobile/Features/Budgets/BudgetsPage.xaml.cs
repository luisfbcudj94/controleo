using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Core.Services;

using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Features.Budgets;

public partial class BudgetsPage : ContentPage
{
    private readonly IExpenseApiClient _apiClient;
    private readonly ICatalogColorService _colorService;
    private readonly ObservableCollection<BudgetViewItem> _budgets = [];
    private bool _isRefreshing;
    private string? _editingMovementType;

    public BudgetsPage(IExpenseApiClient apiClient, IMonthContextService monthContext, ICatalogColorService colorService)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _colorService = colorService;
        BudgetsCollection.ItemsSource = _budgets;
        MoneyFormatHelper.Attach(EditBudgetAmountEntry);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        SetLoading(true);
        try
        {
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            _colorService.SetConfigs(catalog.MovementTypeConfigs);
            var movementTypes = catalog.MovementTypes.ToList();

            var budgets = await _apiClient.GetBudgetsAsync(CancellationToken.None);
            var budgetMap = budgets.ToDictionary(b => b.MovementType, b => b, StringComparer.OrdinalIgnoreCase);

            _budgets.Clear();
            var items = movementTypes.Select(mt =>
            {
                budgetMap.TryGetValue(mt, out var budget);
                var hasBudget = budget is not null && budget.Amount > 0;
                return new BudgetViewItem(
                    mt,
                    hasBudget ? budget!.Amount : 0m,
                    hasBudget ? budget!.UpdatedAt : (DateTimeOffset?)null,
                    hasBudget,
                    _colorService.ForMovementType(mt),
                    _colorService.IconForMovementType(mt));
            })
            .OrderByDescending(b => b.HasBudget ? 1 : 0)
            .ThenByDescending(b => b.Amount)
            .ToList();

            foreach (var item in items)
                _budgets.Add(item);
        }
        finally
        {
            _isRefreshing = false;
            BudgetsRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private void OnEditBudgetTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BudgetViewItem item) return;

        _editingMovementType = item.MovementType;
        EditBudgetIcon.Text = item.Icon;
        EditBudgetTitle.Text = item.MovementType;
        EditBudgetCurrentLabel.Text = item.HasBudget
            ? $"Presupuesto actual: ${item.Amount:N0}"
            : "Sin presupuesto asignado";
        EditBudgetAmountEntry.Text = item.HasBudget ? MoneyFormatHelper.FormatWithDots(((long)item.Amount).ToString()) : string.Empty;
        BudgetEditOverlay.IsVisible = true;
    }

    private async void OnSaveBudgetEdit(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_editingMovementType)) return;

        if (!MoneyFormatHelper.TryParse(EditBudgetAmountEntry.Text, out var amount))
        {
            await StyledResultModalPage.ShowAsync(this, false, "Valor inválido", "El presupuesto debe ser numérico.");
            return;
        }

        if (amount < 0)
        {
            await StyledResultModalPage.ShowAsync(this, false, "Valor inválido", "El presupuesto no puede ser negativo.");
            return;
        }

        BudgetEditOverlay.IsVisible = false;
        SetLoading(true);
        var result = await _apiClient.UpsertBudgetAsync(_editingMovementType, amount, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this, result.IsSuccess,
            result.IsSuccess ? "Presupuesto guardado" : "No se pudo guardar",
            result.IsSuccess ? "El presupuesto se guardó correctamente." : result.Message);
        SetLoading(false);
        if (result.IsSuccess) await LoadDataAsync();
    }

    private async void OnRemoveBudget(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_editingMovementType)) return;

        var confirm = await StyledConfirmModalPage.ConfirmAsync(
            this, "Quitar presupuesto", $"¿Quitar presupuesto de '{_editingMovementType}'?");
        if (!confirm) return;

        BudgetEditOverlay.IsVisible = false;
        SetLoading(true);
        var result = await _apiClient.DeleteBudgetAsync(_editingMovementType, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this, result.IsSuccess,
            result.IsSuccess ? "Presupuesto eliminado" : "No se pudo eliminar",
            result.IsSuccess ? "El presupuesto se eliminó correctamente." : result.Message);
        SetLoading(false);
        if (result.IsSuccess) await LoadDataAsync();
    }

    private void OnCancelBudgetEdit(object? sender, EventArgs e)
    {
        BudgetEditOverlay.IsVisible = false;
        _editingMovementType = null;
    }

    private async void OnRefreshing(object? sender, EventArgs e) => await LoadDataAsync();

    private void SetLoading(bool isLoading) => LoadingOverlay.IsVisible = isLoading;

    internal sealed record BudgetViewItem(
        string MovementType,
        decimal Amount,
        DateTimeOffset? UpdatedAt,
        bool HasBudget,
        Color CardColor,
        string Icon)
    {
        public string BudgetLabel => HasBudget ? $"${Amount:N0}" : "Sin asignar";
        public Color BudgetLabelColor => HasBudget
            ? Color.FromArgb("#1B4332")
            : Color.FromArgb("#E5534B");
        public string Subtitle => HasBudget && UpdatedAt.HasValue
            ? $"Actualizado: {UpdatedAt.Value:yyyy-MM-dd HH:mm}"
            : "Toca el lápiz para asignar presupuesto";
    }
}