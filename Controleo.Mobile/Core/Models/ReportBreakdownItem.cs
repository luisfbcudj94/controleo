namespace Controleo.Mobile.Core.Models;

public sealed record ReportBreakdownItem(
    string Name,
    decimal Amount,
    int Count,
    decimal Percentage);
