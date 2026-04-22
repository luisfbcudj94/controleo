using System.Collections.ObjectModel;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.MoneyCoach;

public partial class MoneyCoachPage : ContentPage
{
    private readonly IExpenseApiClient _api;
    private readonly ObservableCollection<ChatBubbleItem> _messages = [];

    public MoneyCoachPage(IExpenseApiClient api)
    {
        InitializeComponent();
        _api = api;
        MessagesCollection.ItemsSource = _messages;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadHistoryAndSuggestionsAsync();
    }

    private async Task LoadHistoryAndSuggestionsAsync()
    {
        ChatLoadingIndicator.IsRunning = true;
        ChatLoadingIndicator.IsVisible = true;
        WelcomeContainer.IsVisible = false;

        try
        {
            // Load history
            var history = await _api.GetCoachHistoryAsync(30, CancellationToken.None);
            _messages.Clear();
            foreach (var m in history.Messages)
                _messages.Add(ChatBubbleItem.From(m));

            if (_messages.Count > 0)
            {
                WelcomeContainer.IsVisible = false;
                MessagesCollection.IsVisible = true;
                ScrollToEnd();
            }
            else
            {
                WelcomeContainer.IsVisible = true;
                MessagesCollection.IsVisible = false;
            }

            // Load suggestions
            var suggestions = await _api.GetCoachSuggestionsAsync(CancellationToken.None);
            RenderSuggestions(suggestions.Suggestions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MoneyCoach] Load error: {ex.Message}");
            WelcomeContainer.IsVisible = true;
        }
        finally
        {
            ChatLoadingIndicator.IsRunning = false;
            ChatLoadingIndicator.IsVisible = false;
        }
    }

    private void RenderSuggestions(IReadOnlyList<string> suggestions)
    {
        SuggestionsContainer.Children.Clear();
        foreach (var s in suggestions)
        {
            var btn = new Button
            {
                Text = s,
                FontSize = 12,
                TextColor = Color.FromArgb("#44433F"),
                BackgroundColor = Color.FromArgb("#F4F3EF"),
                CornerRadius = 16,
                Padding = new Thickness(14, 8),
                HeightRequest = 36,
                BorderColor = Color.FromArgb("#14000000"),
                BorderWidth = 1
            };
            btn.Clicked += async (_, __) => await SendMessageAsync(s);
            SuggestionsContainer.Children.Add(btn);
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        MessageEntry.Text = "";
        await SendMessageAsync(text);
    }

    private async Task SendMessageAsync(string content)
    {
        // Show user bubble immediately
        _messages.Add(new ChatBubbleItem { Role = "user", Content = content, IsUser = true, IsCoach = false });
        WelcomeContainer.IsVisible = false;
        MessagesCollection.IsVisible = true;
        ScrollToEnd();

        // Show typing indicator
        TypingIndicator.IsVisible = true;
        SendButton.IsEnabled = false;
        MessageEntry.IsEnabled = false;

        try
        {
            var response = await _api.SendCoachMessageAsync(content, CancellationToken.None);
            TypingIndicator.IsVisible = false;

            if (response is not null)
            {
                _messages.Add(new ChatBubbleItem { Role = "assistant", Content = response.Content, IsUser = false, IsCoach = true });
            }
            else
            {
                _messages.Add(new ChatBubbleItem { Role = "assistant", Content = "Lo siento, no pude procesar tu consulta. Intenta de nuevo. 🙏", IsUser = false, IsCoach = true });
            }

            ScrollToEnd();
        }
        catch
        {
            TypingIndicator.IsVisible = false;
            _messages.Add(new ChatBubbleItem { Role = "assistant", Content = "Error de conexión. Verifica tu conexión e intenta de nuevo.", IsUser = false, IsCoach = true });
            ScrollToEnd();
        }
        finally
        {
            SendButton.IsEnabled = true;
            MessageEntry.IsEnabled = true;
        }
    }

    private async void OnClearHistoryClicked(object? sender, EventArgs e)
    {
        var confirmed = await StyledConfirmModalPage.ConfirmAsync(this, "Limpiar historial", "¿Eliminar toda la conversación? Esta acción no se puede deshacer.");
        if (!confirmed) return;

        var result = await _api.ClearCoachHistoryAsync(CancellationToken.None);
        if (result.IsSuccess)
        {
            _messages.Clear();
            WelcomeContainer.IsVisible = true;
            MessagesCollection.IsVisible = false;
            await LoadHistoryAndSuggestionsAsync();
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }

    private void ScrollToEnd()
    {
        if (_messages.Count > 0)
        {
            MessagesCollection.Dispatcher.Dispatch(() =>
            {
                try { MessagesCollection.ScrollTo(_messages.Count - 1, position: ScrollToPosition.End, animate: true); }
                catch { /* ignore scroll errors */ }
            });
        }
    }
}

public sealed class ChatBubbleItem
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsUser { get; set; }
    public bool IsCoach { get; set; }

    public static ChatBubbleItem From(ChatMessageItem m) => new()
    {
        Role = m.Role,
        Content = m.Content,
        IsUser = m.IsUser,
        IsCoach = !m.IsUser
    };
}
