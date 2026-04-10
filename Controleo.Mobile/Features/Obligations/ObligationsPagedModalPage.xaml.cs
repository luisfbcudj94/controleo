using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Controleo.Mobile.Shared.Modals;

namespace Controleo.Mobile.Features.Obligations;

internal enum ObligationListActionKind
{
    Edit,
    Delete
}

internal sealed record ObligationListActionResult(ObligationListActionKind Action, ObligationItem Item);

public partial class ObligationsPagedModalPage : ContentPage
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20];

    private readonly IExpenseApiClient _apiClient;
    private readonly ObservableCollection<ObligationItem> _items = [];
    private readonly TaskCompletionSource<ObligationListActionResult?> _completionSource = new();

    private bool _isRefreshing;
    private bool _isPageSizeSyncing;
    private int _pageNumber = 1;
    private int _pageSize = 5;

    private ObligationsPagedModalPage(IExpenseApiClient apiClient)
    {
        InitializeComponent();

        _apiClient = apiClient;
        ObligationsCollection.ItemsSource = _items;

        PageSizePicker.ItemsSource = AllowedPageSizes.Select(item => item.ToString(CultureInfo.InvariantCulture)).ToList();
        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = _pageSize.ToString(CultureInfo.InvariantCulture);
        _isPageSizeSyncing = false;
        PageSizeSelectorLabel.Text = _pageSize.ToString(CultureInfo.InvariantCulture);
    }

    internal static async Task<ObligationListActionResult?> ShowAsync(Page host, IExpenseApiClient apiClient)
    {
        var modal = new ObligationsPagedModalPage(apiClient);
        await host.Navigation.PushModalAsync(modal);
        return await modal._completionSource.Task;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var page = await _apiClient.GetObligationsPageAsync(_pageNumber, _pageSize, CancellationToken.None);
            _pageNumber = page.PageNumber;

            _items.Clear();
            foreach (var item in page.Items)
            {
                _items.Add(item);
            }

            SummaryLabel.Text = $"{page.TotalCount} obligación(es) registradas";
            PaginationStatusLabel.Text = $"Página {page.PageNumber}/{page.TotalPages}";
            PrevPageButton.IsEnabled = page.HasPreviousPage;
            NextPageButton.IsEnabled = page.HasNextPage;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (_isPageSizeSyncing || PageSizePicker.SelectedItem is not string pageSizeText)
        {
            return;
        }

        if (!int.TryParse(pageSizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedPageSize))
        {
            return;
        }

        _pageSize = AllowedPageSizes.Contains(selectedPageSize) ? selectedPageSize : 5;
        PageSizeSelectorLabel.Text = _pageSize.ToString(CultureInfo.InvariantCulture);
        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void OnPageSizeSelectorTapped(object? sender, TappedEventArgs e)
    {
        var options = AllowedPageSizes.Select(size => size.ToString(CultureInfo.InvariantCulture)).ToList();
        var selected = await StyledSelectorModalPage.PickAsync(this, "Mostrar elementos", options, PageSizeSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        if (PageSizePicker.SelectedItem?.ToString() == selected)
        {
            return;
        }

        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = selected;
        _isPageSizeSyncing = false;

        if (int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selectedPageSize)
            && AllowedPageSizes.Contains(selectedPageSize))
        {
            _pageSize = selectedPageSize;
            PageSizeSelectorLabel.Text = _pageSize.ToString(CultureInfo.InvariantCulture);
            _pageNumber = 1;
            await LoadDataAsync();
        }
    }

    private async void OnPrevPageClicked(object? sender, EventArgs e)
    {
        if (_pageNumber <= 1)
        {
            return;
        }

        _pageNumber--;
        await LoadDataAsync();
    }

    private async void OnNextPageClicked(object? sender, EventArgs e)
    {
        _pageNumber++;
        await LoadDataAsync();
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ObligationItem item })
        {
            return;
        }

        await CloseWithResultAsync(new ObligationListActionResult(ObligationListActionKind.Edit, item));
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ObligationItem item })
        {
            return;
        }

        await CloseWithResultAsync(new ObligationListActionResult(ObligationListActionKind.Delete, item));
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await CloseWithResultAsync(null);
    }

    private async Task CloseWithResultAsync(ObligationListActionResult? result)
    {
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(result);
        }

        await Navigation.PopModalAsync();
    }
}
