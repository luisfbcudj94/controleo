using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Shared.Modals;

public partial class StyledSelectorModalPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completionSource = new();

    public StyledSelectorModalPage(string title, IReadOnlyList<string> options, string? selected)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        OptionsCollection.ItemsSource = options
            .Select(option => new SelectorOption(option, string.Equals(option, selected, StringComparison.Ordinal)))
            .ToList();
    }

    public Task<string?> Result => _completionSource.Task;

    public static async Task<string?> PickAsync(Page host, string title, IReadOnlyList<string> options, string? selected)
    {
        var modal = new StyledSelectorModalPage(title, options, selected);
        await host.Navigation.PushModalAsync(modal);
        return await modal.Result;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(null);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnOptionTapped(object? sender, TappedEventArgs e)
    {
        var selected = (sender as BindableObject)?.BindingContext as SelectorOption;
        var selectedLabel = selected?.Label;
        if (string.IsNullOrWhiteSpace(selectedLabel))
        {
            return;
        }

        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(selectedLabel);
        }

        await Navigation.PopModalAsync();
    }

    private sealed record SelectorOption(string Label, bool IsSelected);
}