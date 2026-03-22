using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class SectionExpensesModalPage : ContentPage
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20];

    private readonly ExpenseApiClient _apiClient;
    private readonly string _monthKey;
    private readonly string _movementType;
    private readonly ObservableCollection<ExpenseItem> _items = [];
    private bool _isRefreshing;
    private bool _isPageSizeSyncing;
    private int _pageNumber = 1;
    private int _pageSize = 5;

    public SectionExpensesModalPage(ExpenseApiClient apiClient, string monthKey, string movementType)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _monthKey = monthKey;
        _movementType = movementType;

        SectionTitleLabel.Text = movementType;
        ExpensesBySectionCollection.ItemsSource = _items;

        PageSizePicker.ItemsSource = AllowedPageSizes.Select(item => item.ToString()).ToList();
        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = _pageSize.ToString();
        _isPageSizeSyncing = false;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
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
            var page = await _apiClient.GetExpensesPageAsync(
                _monthKey,
                _pageNumber,
                _pageSize,
                _movementType,
                CancellationToken.None);

            _pageNumber = page.PageNumber;

            _items.Clear();
            foreach (var item in page.Items)
            {
                _items.Add(item);
            }

            SummaryLabel.Text = $"{page.TotalCount} gasto(s) · Total: ${page.TotalAmount:N0}";
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

        if (!int.TryParse(pageSizeText, out var selectedPageSize))
        {
            return;
        }

        _pageSize = AllowedPageSizes.Contains(selectedPageSize) ? selectedPageSize : 5;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void OnPageSizeSelectorTapped(object? sender, EventArgs e)
    {
        var options = AllowedPageSizes.Select(size => size.ToString()).ToList();
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

        if (int.TryParse(selected, out var selectedPageSize) && AllowedPageSizes.Contains(selectedPageSize))
        {
            _pageSize = selectedPageSize;
            PageSizeSelectorLabel.Text = _pageSize.ToString();
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

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
