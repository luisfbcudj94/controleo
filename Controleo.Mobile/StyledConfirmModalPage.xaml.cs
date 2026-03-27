namespace Controleo.Mobile;

public partial class StyledConfirmModalPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completionSource = new();

    public StyledConfirmModalPage(string title, string message)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
    }

    public Task<bool> Result => _completionSource.Task;

    public static async Task<bool> ConfirmAsync(Page host, string title, string message)
    {
        var modal = new StyledConfirmModalPage(title, message);
        await host.Navigation.PushModalAsync(modal);
        return await modal.Result;
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(true);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(false);
        }

        await Navigation.PopModalAsync();
    }
}