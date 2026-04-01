namespace Controleo.Mobile;

public sealed record ExpensesFilterSelection(string? MovementType, string? PaymentMethod);

public partial class ExpensesFiltersModalPage : ContentPage
{
    private const string AllOption = "Todos";
    private readonly TaskCompletionSource<ExpensesFilterSelection?> _completionSource = new();
    private readonly List<string> _movementOptions;
    private readonly List<string> _paymentOptions;
    private string _selectedMovementType;
    private string _selectedPaymentMethod;

    public ExpensesFiltersModalPage(
        IReadOnlyList<string> movementTypes,
        IReadOnlyList<string> paymentMethods,
        string? selectedMovementType,
        string? selectedPaymentMethod)
    {
        InitializeComponent();

        _movementOptions = BuildOptions(movementTypes);
        _paymentOptions = BuildOptions(paymentMethods);

        _selectedMovementType = ResolveSelection(selectedMovementType, _movementOptions);
        _selectedPaymentMethod = ResolveSelection(selectedPaymentMethod, _paymentOptions);

        MovementTypeLabel.Text = _selectedMovementType;
        PaymentMethodLabel.Text = _selectedPaymentMethod;
        UpdateSelectionVisuals();
    }

    public Task<ExpensesFilterSelection?> Result => _completionSource.Task;

    public static async Task<ExpensesFilterSelection?> PickAsync(
        Page host,
        IReadOnlyList<string> movementTypes,
        IReadOnlyList<string> paymentMethods,
        string? selectedMovementType,
        string? selectedPaymentMethod)
    {
        var modal = new ExpensesFiltersModalPage(movementTypes, paymentMethods, selectedMovementType, selectedPaymentMethod);
        await host.Navigation.PushModalAsync(modal);
        return await modal.Result;
    }

    private async void OnMovementTypeTapped(object? sender, EventArgs e)
    {
        var picked = await StyledSelectorModalPage.PickAsync(
            this, "Tipo de movimiento", _movementOptions, _selectedMovementType);
        if (picked is not null)
        {
            _selectedMovementType = picked;
            MovementTypeLabel.Text = picked;
            UpdateSelectionVisuals();
        }
    }

    private async void OnPaymentMethodTapped(object? sender, EventArgs e)
    {
        var picked = await StyledSelectorModalPage.PickAsync(
            this, "Medio de pago", _paymentOptions, _selectedPaymentMethod);
        if (picked is not null)
        {
            _selectedPaymentMethod = picked;
            PaymentMethodLabel.Text = picked;
            UpdateSelectionVisuals();
        }
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        var result = new ExpensesFilterSelection(
            NormalizeFilterValue(_selectedMovementType),
            NormalizeFilterValue(_selectedPaymentMethod));

        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(result);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(new ExpensesFilterSelection(null, null));
        }

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(null);
        }

        await Navigation.PopModalAsync();
    }

    private void UpdateSelectionVisuals()
    {
        var hasMovementFilter = !string.Equals(_selectedMovementType, AllOption, StringComparison.OrdinalIgnoreCase);
        var hasPaymentFilter = !string.Equals(_selectedPaymentMethod, AllOption, StringComparison.OrdinalIgnoreCase);

        MovementTypeBorder.Stroke = hasMovementFilter ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#DCE5DF");
        PaymentMethodBorder.Stroke = hasPaymentFilter ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#DCE5DF");

        MovementTypeLabel.TextColor = hasMovementFilter ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#1A2E23");
        PaymentMethodLabel.TextColor = hasPaymentFilter ? Color.FromArgb("#2D6A4F") : Color.FromArgb("#1A2E23");
    }

    private static string NormalizeFilterValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), AllOption, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return raw.Trim();
    }

    private static string ResolveSelection(string? selected, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(selected))
        {
            return AllOption;
        }

        var match = options.FirstOrDefault(option => string.Equals(option, selected.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? AllOption;
    }

    private static List<string> BuildOptions(IReadOnlyList<string> source)
    {
        var items = source
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();

        items.Insert(0, AllOption);
        return items;
    }
}
