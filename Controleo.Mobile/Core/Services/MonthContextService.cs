using System.Globalization;
using Controleo.Mobile.Core.Interfaces;

namespace Controleo.Mobile.Core.Services;

public sealed class MonthContextService : IMonthContextService
{
    private readonly List<MonthOption> _monthOptions;
    private DateOnly _selectedMonth;

    public event EventHandler<DateOnly>? MonthChanged;
    public event EventHandler? MonthOptionsChanged;

    public MonthContextService()
    {
        var currentMonth = DateOnly.FromDateTime(DateTime.Today);
        _selectedMonth = new DateOnly(currentMonth.Year, currentMonth.Month, 1);
        _monthOptions =
        [
            new MonthOption(_selectedMonth, BuildMonthLabel(_selectedMonth))
        ];
    }

    public IReadOnlyList<MonthOption> MonthOptions => _monthOptions;

    public DateOnly SelectedMonth => _selectedMonth;

    public string SelectedMonthKey => _selectedMonth.ToString("yyyy-MM");

    public void SetAvailableMonths(IEnumerable<string> monthKeys)
    {
        var currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var parsedMonths = monthKeys
            .Select(ParseMonthKey)
            .Where(month => month is not null)
            .Select(month => month!.Value)
            .Where(month => month <= currentMonth)
            .Distinct()
            .OrderBy(month => month)
            .ToList();

        if (parsedMonths.All(month => month != currentMonth))
        {
            parsedMonths.Add(currentMonth);
            parsedMonths = parsedMonths
                .Distinct()
                .OrderBy(month => month)
                .ToList();
        }

        if (parsedMonths.Count == 0)
        {
            parsedMonths.Add(currentMonth);
        }

        _monthOptions.Clear();
        _monthOptions.AddRange(parsedMonths.Select(month => new MonthOption(month, BuildMonthLabel(month))));

        MonthOptionsChanged?.Invoke(this, EventArgs.Empty);

        if (_monthOptions.Any(item => item.Value == _selectedMonth))
        {
            return;
        }

        _selectedMonth = _monthOptions[^1].Value;
        MonthChanged?.Invoke(this, _selectedMonth);
    }

    public void SetMonth(DateOnly month)
    {
        var normalized = new DateOnly(month.Year, month.Month, 1);

        if (_monthOptions.Count > 0)
        {
            if (_monthOptions.Any(item => item.Value == normalized))
            {
                // valid option
            }
            else
            {
                normalized = normalized < _monthOptions[0].Value
                    ? _monthOptions[0].Value
                    : _monthOptions[^1].Value;
            }
        }

        if (normalized == _selectedMonth)
        {
            return;
        }

        _selectedMonth = normalized;
        MonthChanged?.Invoke(this, _selectedMonth);
    }

    public void MoveMonths(int delta)
    {
        if (_monthOptions.Count == 0)
        {
            return;
        }

        var currentIndex = _monthOptions.FindIndex(item => item.Value == _selectedMonth);
        if (currentIndex < 0)
        {
            currentIndex = _monthOptions.Count - 1;
        }

        var targetIndex = Math.Clamp(currentIndex + delta, 0, _monthOptions.Count - 1);
        SetMonth(_monthOptions[targetIndex].Value);
    }

    private static DateOnly? ParseMonthKey(string? monthKey)
    {
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            $"{monthKey}-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static string BuildMonthLabel(DateOnly month)
    {
        return month
            .ToDateTime(TimeOnly.MinValue)
            .ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    public sealed record MonthOption(DateOnly Value, string Label)
    {
        public override string ToString() => Label;
    }
}
