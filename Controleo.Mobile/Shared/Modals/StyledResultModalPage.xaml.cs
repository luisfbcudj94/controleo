using Controleo.Mobile.Shared.Modals;
namespace Controleo.Mobile.Shared.Modals;

public partial class StyledResultModalPage : ContentPage
{
    private readonly int _autoCloseMilliseconds;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _autoCloseCts;
    private bool _isClosing;

    private StyledResultModalPage(bool isSuccess, string title, string message, int autoCloseMilliseconds)
    {
        InitializeComponent();
        _autoCloseMilliseconds = autoCloseMilliseconds;

        TitleLabel.Text = title;
        MessageLabel.Text = message;

        if (isSuccess)
        {
            IconLabel.Text = "✓";
            IconLabel.TextColor = Color.FromArgb("#0E7A3A");
            IconCircle.BackgroundColor = Color.FromArgb("#D9F7E8");
            IconCircle.Stroke = Color.FromArgb("#7ED6A5");
        }
        else
        {
            IconLabel.Text = "!";
            IconLabel.TextColor = Color.FromArgb("#A11D2A");
            IconCircle.BackgroundColor = Color.FromArgb("#FDE3E5");
            IconCircle.Stroke = Color.FromArgb("#F2A4AA");
        }
    }

    public static async Task ShowAsync(Page hostPage, bool isSuccess, string title, string message, int autoCloseMilliseconds = 5000)
    {
        var modal = new StyledResultModalPage(isSuccess, title, message, autoCloseMilliseconds);
        await hostPage.Navigation.PushModalAsync(modal, false);
        await modal._completion.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _autoCloseCts = new CancellationTokenSource();
        _ = AutoCloseAsync(_autoCloseCts.Token);
    }

    private async Task AutoCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_autoCloseMilliseconds, cancellationToken);
            await CloseAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _autoCloseCts?.Cancel();

        try
        {
            await Navigation.PopModalAsync(false);
        }
        catch
        {
        }
        finally
        {
            _completion.TrySetResult(true);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autoCloseCts?.Cancel();
        _completion.TrySetResult(true);
    }
}