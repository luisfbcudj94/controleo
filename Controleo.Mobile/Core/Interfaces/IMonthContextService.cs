using Controleo.Mobile.Core.Services;

namespace Controleo.Mobile.Core.Interfaces;

public interface IMonthContextService
{
    IReadOnlyList<MonthContextService.MonthOption> MonthOptions { get; }
    DateOnly SelectedMonth { get; }
    string SelectedMonthKey { get; }
    void SetAvailableMonths(IEnumerable<string> monthKeys);
    void SetMonth(DateOnly month);
    void MoveMonths(int delta);
    event EventHandler<DateOnly>? MonthChanged;
    event EventHandler? MonthOptionsChanged;
}
