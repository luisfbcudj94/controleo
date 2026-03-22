using System.Globalization;

namespace Controleo.Mobile.Services;

public sealed class MonthContextService
{
    private readonly List<MonthOption> _monthOptions;
    private DateOnly _selectedMonth;

    public event EventHandler<DateOnly>? MonthChanged;

    public MonthContextService()
    {
        var currentMonth = DateOnly.FromDateTime(DateTime.Today);
        _selectedMonth = new DateOnly(currentMonth.Year, currentMonth.Month, 1);

        _monthOptions = [];
        for (var offset = -24; offset <= 12; offset++)
        {
            var month = _selectedMonth.AddMonths(offset);
            _monthOptions.Add(new MonthOption(
                month,
                month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-CO"))));
        }
    }

    public IReadOnlyList<MonthOption> MonthOptions => _monthOptions;

    public DateOnly SelectedMonth => _selectedMonth;

    public string SelectedMonthKey => _selectedMonth.ToString("yyyy-MM");

    public void SetMonth(DateOnly month)
    {
        var normalized = new DateOnly(month.Year, month.Month, 1);
        if (normalized == _selectedMonth)
        {
            return;
        }

        _selectedMonth = normalized;
        MonthChanged?.Invoke(this, _selectedMonth);
    }

    public void MoveMonths(int delta)
    {
        SetMonth(_selectedMonth.AddMonths(delta));
    }

    public sealed record MonthOption(DateOnly Value, string Label)
    {
        public override string ToString() => Label;
    }
}
