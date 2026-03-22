using Controleo.Mobile.Models;

namespace Controleo.Mobile;

public partial class SectionExpensesModalPage : ContentPage
{
    public SectionExpensesModalPage(string movementType, IReadOnlyList<ExpenseItem> expenses)
    {
        InitializeComponent();

        SectionTitleLabel.Text = movementType;
        ExpensesBySectionCollection.ItemsSource = expenses;

        var total = expenses.Sum(item => item.Amount);
        SummaryLabel.Text = $"{expenses.Count} gasto(s) · Total: ${total:N0}";
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
