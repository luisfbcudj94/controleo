using System.Globalization;
using System.Text;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Controleo.Application.Services;

public sealed class ReportExportService(IExpenseRepository expenseRepository) : IReportExportService
{
    private const int MaxRangeDays = 366;
    private const int MaxBreakdownItems = 8;
    private const int MaxTopExpenses = 10;

    private static int _pdfLicenseConfigured;

    public async Task<ReportPreviewResponse> BuildPreviewAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var report = await BuildReportDataAsync(userId, startDate, endDate, ct);
        return report.Preview;
    }

    public async Task<ReportExportFile> BuildCsvAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var report = await BuildReportDataAsync(userId, startDate, endDate, ct);
        var csv = BuildCsv(report);
        var fileName = $"controleo-reporte-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv";
        return new ReportExportFile(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    public async Task<ReportExportFile> BuildPdfAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var report = await BuildReportDataAsync(userId, startDate, endDate, ct);
        EnsurePdfLicenseConfigured();

        var fileName = $"controleo-reporte-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.pdf";
        var bytes = BuildPdf(report);
        return new ReportExportFile(bytes, "application/pdf", fileName);
    }

    private async Task<ReportData> BuildReportDataAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        ValidateDateRange(startDate, endDate);
        var expenses = await LoadExpensesAsync(userId, startDate, endDate, ct);

        var (previousStartDate, previousEndDate) = ResolvePreviousRange(startDate, endDate);
        var previousExpenses = await LoadExpensesAsync(userId, previousStartDate, previousEndDate, ct);

        var preview = BuildPreview(
            expenses,
            previousExpenses,
            startDate,
            endDate,
            previousStartDate,
            previousEndDate);

        return new ReportData(preview, expenses, previousExpenses, previousStartDate, previousEndDate);
    }

    private async Task<IReadOnlyList<ExpenseItem>> LoadExpensesAsync(string userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var monthCursor = new DateOnly(startDate.Year, startDate.Month, 1);
        var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);
        var result = new List<ExpenseItem>();

        while (monthCursor <= endMonth)
        {
            var monthKey = monthCursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var monthExpenses = await expenseRepository.GetExpensesAsync(userId, monthKey, ct);

            result.AddRange(monthExpenses.Where(item => item.Date >= startDate && item.Date <= endDate));
            monthCursor = monthCursor.AddMonths(1);
        }

        return result
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Amount)
            .ToArray();
    }

    private static ReportPreviewResponse BuildPreview(
        IReadOnlyList<ExpenseItem> expenses,
        IReadOnlyList<ExpenseItem> previousExpenses,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly previousStartDate,
        DateOnly previousEndDate)
    {
        var totalTransactions = expenses.Count;
        var totalAmount = expenses.Sum(item => item.Amount);
        var days = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var averageDailyAmount = Math.Round(totalAmount / days, 2, MidpointRounding.AwayFromZero);

        var previousTransactions = previousExpenses.Count;
        var previousAmount = previousExpenses.Sum(item => item.Amount);
        var amountChangePercentage = CalculateChangePercentage(totalAmount, previousAmount);
        var transactionsChangePercentage = CalculateChangePercentage(totalTransactions, previousTransactions);

        var categoryBreakdown = BuildBreakdown(expenses, item => item.MovementType, totalAmount);
        var paymentBreakdown = BuildBreakdown(expenses, item => item.PaymentMethod, totalAmount);
        var dailyTrend = BuildDailyTrend(expenses, startDate, endDate);

        var topExpenses = expenses
            .OrderByDescending(item => item.Amount)
            .ThenByDescending(item => item.Date)
            .Take(MaxTopExpenses)
            .Select(item => new ReportTopExpenseItem(
                item.Date,
                NormalizeLabel(item.Description, "Sin descripción"),
                NormalizeLabel(item.MovementType, "Sin categoría"),
                NormalizeLabel(item.PaymentMethod, "Sin medio"),
                item.Amount))
            .ToArray();

        var preview = new ReportPreviewResponse(
            startDate,
            endDate,
            totalTransactions,
            totalAmount,
            averageDailyAmount,
            previousTransactions,
            previousAmount,
            amountChangePercentage,
            transactionsChangePercentage,
            categoryBreakdown,
            paymentBreakdown,
            topExpenses,
            dailyTrend,
            []);

        var insights = BuildInsights(preview, previousStartDate, previousEndDate);
        return preview with { Insights = insights };
    }

    private static (DateOnly PreviousStartDate, DateOnly PreviousEndDate) ResolvePreviousRange(DateOnly startDate, DateOnly endDate)
    {
        var days = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var previousEndDate = startDate.AddDays(-1);
        var previousStartDate = previousEndDate.AddDays(-(days - 1));
        return (previousStartDate, previousEndDate);
    }

    private static IReadOnlyList<ReportDailyTrendPoint> BuildDailyTrend(IReadOnlyList<ExpenseItem> expenses, DateOnly startDate, DateOnly endDate)
    {
        var byDate = expenses
            .GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => new
            {
                Amount = group.Sum(item => item.Amount),
                Transactions = group.Count()
            });

        var days = new List<ReportDailyTrendPoint>();
        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            if (byDate.TryGetValue(day, out var values))
            {
                days.Add(new ReportDailyTrendPoint(day, values.Amount, values.Transactions));
            }
            else
            {
                days.Add(new ReportDailyTrendPoint(day, 0, 0));
            }
        }

        return days;
    }

    private static decimal CalculateChangePercentage(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return current == 0 ? 0 : 100;
        }

        return Math.Round(((current - previous) / previous) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<string> BuildInsights(ReportPreviewResponse preview, DateOnly previousStartDate, DateOnly previousEndDate)
    {
        var insights = new List<string>();

        if (preview.TotalTransactions == 0)
        {
            insights.Add("No se registraron gastos en este periodo. Úsalo para planear un presupuesto base antes del próximo mes.");
            return insights;
        }

        var amountTrend = preview.AmountChangePercentage switch
        {
            > 0 => $"Tus gastos subieron {preview.AmountChangePercentage:0.##}% frente al periodo anterior ({previousStartDate:dd/MM} - {previousEndDate:dd/MM}).",
            < 0 => $"Tus gastos bajaron {Math.Abs(preview.AmountChangePercentage):0.##}% frente al periodo anterior ({previousStartDate:dd/MM} - {previousEndDate:dd/MM}).",
            _ => "Tu gasto total se mantuvo estable frente al periodo anterior."
        };
        insights.Add(amountTrend);

        var topCategory = preview.CategoryBreakdown.FirstOrDefault();
        if (topCategory is not null)
        {
            insights.Add($"{topCategory.Name} concentra {topCategory.Percentage:0.##}% del gasto del periodo. Revisa oportunidades de ajuste ahí primero.");
        }

        var topPaymentMethod = preview.PaymentMethodBreakdown.FirstOrDefault();
        if (topPaymentMethod is not null)
        {
            insights.Add($"{topPaymentMethod.Name} es tu medio de pago principal con {topPaymentMethod.Percentage:0.##}% del total.");
        }

        var highestDay = preview.DailyTrend.OrderByDescending(item => item.Amount).FirstOrDefault();
        if (highestDay is not null && highestDay.Amount > 0)
        {
            insights.Add($"El pico de gasto fue el {highestDay.Date:dd MMM} con {highestDay.Amount.ToString("C0", CultureInfo.GetCultureInfo("es-CO"))}.");
        }

        if (insights.Count > 4)
        {
            return insights.Take(4).ToArray();
        }

        return insights;
    }

    private static IReadOnlyList<ReportBreakdownItem> BuildBreakdown(
        IEnumerable<ExpenseItem> expenses,
        Func<ExpenseItem, string> keySelector,
        decimal totalAmount)
    {
        return expenses
            .GroupBy(item => NormalizeLabel(keySelector(item), "Sin dato"), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var amount = group.Sum(item => item.Amount);
                var percentage = totalAmount <= 0
                    ? 0
                    : Math.Round((amount / totalAmount) * 100m, 2, MidpointRounding.AwayFromZero);

                return new ReportBreakdownItem(group.Key, amount, group.Count(), percentage);
            })
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxBreakdownItems)
            .ToArray();
    }

    private static string BuildCsv(ReportData report)
    {
        var sb = new StringBuilder();
        var preview = report.Preview;

        sb.Append('\uFEFF');
        sb.AppendLine("Fecha,Descripción,Tipo,Medio de pago,Monto");

        foreach (var item in report.Expenses)
        {
            sb.Append(EscapeCsv(item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(NormalizeLabel(item.Description, "Sin descripción"))).Append(',');
            sb.Append(EscapeCsv(NormalizeLabel(item.MovementType, "Sin categoría"))).Append(',');
            sb.Append(EscapeCsv(NormalizeLabel(item.PaymentMethod, "Sin medio"))).Append(',');
            sb.Append(EscapeCsv(item.Amount.ToString("0.00", CultureInfo.InvariantCulture)));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"Rango,{preview.StartDate:yyyy-MM-dd} a {preview.EndDate:yyyy-MM-dd}");
        sb.AppendLine($"Total transacciones,{preview.TotalTransactions}");
        sb.AppendLine($"Monto total,{preview.TotalAmount:0.00}");
        sb.AppendLine($"Promedio diario,{preview.AverageDailyAmount:0.00}");
        sb.AppendLine($"Periodo anterior,{report.PreviousStartDate:yyyy-MM-dd} a {report.PreviousEndDate:yyyy-MM-dd}");
        sb.AppendLine($"Transacciones periodo anterior,{preview.PreviousPeriodTransactions}");
        sb.AppendLine($"Monto periodo anterior,{preview.PreviousPeriodAmount:0.00}");
        sb.AppendLine($"Cambio de monto (%),{preview.AmountChangePercentage:0.##}");
        sb.AppendLine($"Cambio de transacciones (%),{preview.TransactionsChangePercentage:0.##}");

        sb.AppendLine();
        sb.AppendLine("Tendencia diaria");
        sb.AppendLine("Fecha,Monto,Movimientos");
        foreach (var day in preview.DailyTrend)
        {
            sb.Append(EscapeCsv(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(day.Amount.ToString("0.00", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(day.Transactions.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Insights premium");
        sb.AppendLine("Insight");
        foreach (var insight in preview.Insights)
        {
            sb.AppendLine(EscapeCsv(insight));
        }

        return sb.ToString();
    }

    private static byte[] BuildPdf(ReportData report)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var preview = report.Preview;

        return Document
            .Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(26);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor("#1A2E23"));

                    page.Header().Column(column =>
                    {
                        column.Spacing(4);
                        column.Item().Text("Controleo | Reporte Premium")
                            .Bold().FontSize(22).FontColor("#1B4332");
                        column.Item().Text($"Periodo: {preview.StartDate:dd MMM yyyy} - {preview.EndDate:dd MMM yyyy}")
                            .FontSize(11).FontColor("#55786A");
                    });

                    page.Content().PaddingTop(12).Column(column =>
                    {
                        column.Spacing(14);

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Element(c => ComposeMetricCard(c, "Transacciones", preview.TotalTransactions.ToString(CultureInfo.InvariantCulture), "#EAF6EE", "#1B4332"));
                            row.ConstantItem(8);
                            row.RelativeItem().Element(c => ComposeMetricCard(c, "Monto total", preview.TotalAmount.ToString("C0", CultureInfo.GetCultureInfo("es-CO")), "#E9F3FD", "#0D4A7A"));
                            row.ConstantItem(8);
                            row.RelativeItem().Element(c => ComposeMetricCard(c, "Promedio diario", preview.AverageDailyAmount.ToString("C0", CultureInfo.GetCultureInfo("es-CO")), "#FFF5E9", "#8A4D11"));
                        });

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Element(c => ComposeChangeCard(
                                c,
                                "Variación monto",
                                preview.AmountChangePercentage,
                                $"Periodo anterior: {report.PreviousStartDate:dd/MM} - {report.PreviousEndDate:dd/MM}"));
                            row.ConstantItem(8);
                            row.RelativeItem().Element(c => ComposeChangeCard(
                                c,
                                "Variación transacciones",
                                preview.TransactionsChangePercentage,
                                $"Antes: {preview.PreviousPeriodTransactions} mov."));
                        });

                        column.Item().Text("Pulso diario del gasto")
                            .Bold().FontSize(13).FontColor("#1A2E23");
                        column.Item().Element(c => ComposeDailyTrend(c, preview.DailyTrend));

                        column.Item().Text("Distribución por categoría")
                            .Bold().FontSize(13).FontColor("#1A2E23");
                        column.Item().Element(c => ComposeBreakdownTable(c, preview.CategoryBreakdown));

                        column.Item().Text("Distribución por medio de pago")
                            .Bold().FontSize(13).FontColor("#1A2E23");
                        column.Item().Element(c => ComposeBreakdownTable(c, preview.PaymentMethodBreakdown));

                        column.Item().Text("Top gastos del periodo")
                            .Bold().FontSize(13).FontColor("#1A2E23");
                        column.Item().Element(c => ComposeTopExpensesTable(c, preview.TopExpenses));

                        column.Item().Text("Insights premium")
                            .Bold().FontSize(13).FontColor("#1A2E23");
                        column.Item().Element(c => ComposeInsightsList(c, preview.Insights));
                    });

                    page.Footer().AlignCenter().Text($"Generado: {generatedAt:yyyy-MM-dd HH:mm} UTC").FontSize(9).FontColor("#7A9183");
                });
            })
            .GeneratePdf();
    }

    private static void ComposeMetricCard(IContainer container, string title, string value, string bgColor, string valueColor)
    {
        container
            .Border(1)
            .BorderColor("#DCE5DF")
            .Background(bgColor)
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(3);
                column.Item().Text(title).FontSize(9).FontColor("#5E766A");
                column.Item().Text(value).Bold().FontSize(15).FontColor(valueColor);
            });
    }

    private static void ComposeChangeCard(IContainer container, string title, decimal percentage, string subtitle)
    {
        var isPositive = percentage > 0;
        var isNegative = percentage < 0;

        var background = isPositive
            ? "#FDEBE9"
            : isNegative
                ? "#EAF6EE"
                : "#EFF3F1";

        var color = isPositive
            ? "#8E1D13"
            : isNegative
                ? "#1B4332"
                : "#4C6256";

        var icon = isPositive ? "▲" : isNegative ? "▼" : "■";
        var signed = percentage > 0 ? $"+{percentage:0.##}%" : $"{percentage:0.##}%";

        container
            .Border(1)
            .BorderColor("#DCE5DF")
            .Background(background)
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(3);
                column.Item().Text(title).FontSize(9).FontColor("#5E766A");
                column.Item().Text($"{icon} {signed}").Bold().FontSize(14).FontColor(color);
                column.Item().Text(subtitle).FontSize(8.5f).FontColor("#6E8779");
            });
    }

    private static void ComposeDailyTrend(IContainer container, IReadOnlyList<ReportDailyTrendPoint> trend)
    {
        if (trend.Count == 0 || trend.All(item => item.Amount <= 0))
        {
            container
                .Border(1)
                .BorderColor("#DCE5DF")
                .Background("#F7FAF8")
                .Padding(10)
                .Text("No hay datos diarios para este rango.")
                .FontColor("#7A9183");
            return;
        }

        var topDays = trend
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Date)
            .Take(14)
            .OrderBy(item => item.Date)
            .ToArray();

        var maxAmount = topDays.Max(item => item.Amount);

        container
            .Border(1)
            .BorderColor("#DCE5DF")
            .Background("#FFFFFF")
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text("Días con mayor intensidad de gasto").FontSize(9).FontColor("#5E766A");

                foreach (var day in topDays)
                {
                    var percentage = maxAmount <= 0 ? 0 : day.Amount / maxAmount;
                    var barWidth = Math.Max(2f, (float)(percentage * 200));

                    column.Item().Row(row =>
                    {
                        row.ConstantItem(48).Text(day.Date.ToString("dd MMM", CultureInfo.GetCultureInfo("es-CO"))).FontSize(8.5f);
                        row.RelativeItem().Column(inner =>
                        {
                            inner.Item().Background("#EFF5F1").Height(10).Padding(0).AlignLeft().Element(innerContainer =>
                                innerContainer.Background("#2D6A4F").Width(barWidth).Height(10));
                        });
                        row.ConstantItem(82).AlignRight().Text(day.Amount.ToString("C0", CultureInfo.GetCultureInfo("es-CO"))).FontSize(8.5f);
                    });
                }
            });
    }

    private static void ComposeInsightsList(IContainer container, IReadOnlyList<string> insights)
    {
        if (insights.Count == 0)
        {
            container
                .Border(1)
                .BorderColor("#DCE5DF")
                .Background("#F7FAF8")
                .Padding(10)
                .Text("Aún no hay insights para este rango.")
                .FontColor("#7A9183");
            return;
        }

        container
            .Border(1)
            .BorderColor("#DCE5DF")
            .Background("#FFFFFF")
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(6);
                foreach (var insight in insights)
                {
                    column.Item().Row(row =>
                    {
                        row.ConstantItem(10).Text("•").FontSize(10).FontColor("#2D6A4F");
                        row.RelativeItem().Text(insight).FontSize(9).FontColor("#1A2E23");
                    });
                }
            });
    }

    private static void ComposeBreakdownTable(IContainer container, IReadOnlyList<ReportBreakdownItem> rows)
    {
        if (rows.Count == 0)
        {
            container
                .Border(1)
                .BorderColor("#DCE5DF")
                .Background("#F7FAF8")
                .Padding(10)
                .Text("No se encontraron registros para este rango.")
                .FontColor("#7A9183");
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
                columns.RelativeColumn(1.5f);
                columns.RelativeColumn(1.5f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Nombre").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Monto").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Mov.").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("%").Bold();
            });

            foreach (var row in rows)
            {
                table.Cell().Element(BodyCell).Text(row.Name);
                table.Cell().Element(BodyCell).AlignRight().Text(row.Amount.ToString("C0", CultureInfo.GetCultureInfo("es-CO")));
                table.Cell().Element(BodyCell).AlignRight().Text(row.Count.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignRight().Text($"{row.Percentage:0.##}%");
            }
        });

        static IContainer HeaderCell(IContainer container)
        {
            return container
                .Background("#EFF5F1")
                .BorderBottom(1)
                .BorderColor("#DCE5DF")
                .PaddingVertical(6)
                .PaddingHorizontal(8)
                .DefaultTextStyle(x => x.FontSize(9).FontColor("#355D4A"));
        }

        static IContainer BodyCell(IContainer container)
        {
            return container
                .BorderBottom(1)
                .BorderColor("#ECF1EE")
                .PaddingVertical(5)
                .PaddingHorizontal(8)
                .DefaultTextStyle(x => x.FontSize(9).FontColor("#1A2E23"));
        }
    }

    private static void ComposeTopExpensesTable(IContainer container, IReadOnlyList<ReportTopExpenseItem> rows)
    {
        if (rows.Count == 0)
        {
            container
                .Border(1)
                .BorderColor("#DCE5DF")
                .Background("#F7FAF8")
                .Padding(10)
                .Text("No hay transacciones para destacar en este rango.")
                .FontColor("#7A9183");
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.5f);
                columns.RelativeColumn(3.5f);
                columns.RelativeColumn(2f);
                columns.RelativeColumn(2f);
                columns.RelativeColumn(1.8f);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Fecha").Bold();
                header.Cell().Element(HeaderCell).Text("Descripción").Bold();
                header.Cell().Element(HeaderCell).Text("Tipo").Bold();
                header.Cell().Element(HeaderCell).Text("Medio").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Monto").Bold();
            });

            foreach (var row in rows)
            {
                table.Cell().Element(BodyCell).Text(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(row.Description);
                table.Cell().Element(BodyCell).Text(row.MovementType);
                table.Cell().Element(BodyCell).Text(row.PaymentMethod);
                table.Cell().Element(BodyCell).AlignRight().Text(row.Amount.ToString("C0", CultureInfo.GetCultureInfo("es-CO")));
            }
        });

        static IContainer HeaderCell(IContainer container)
        {
            return container
                .Background("#EFF5F1")
                .BorderBottom(1)
                .BorderColor("#DCE5DF")
                .PaddingVertical(6)
                .PaddingHorizontal(6)
                .DefaultTextStyle(x => x.FontSize(9).FontColor("#355D4A"));
        }

        static IContainer BodyCell(IContainer container)
        {
            return container
                .BorderBottom(1)
                .BorderColor("#ECF1EE")
                .PaddingVertical(5)
                .PaddingHorizontal(6)
                .DefaultTextStyle(x => x.FontSize(8.5f).FontColor("#1A2E23"));
        }
    }

    private static string EscapeCsv(string value)
    {
        var normalized = value.Trim();
        if (normalized.Contains(',') || normalized.Contains('"') || normalized.Contains('\n') || normalized.Contains('\r'))
        {
            return $"\"{normalized.Replace("\"", "\"\"")}\"";
        }

        return normalized;
    }

    private static string NormalizeLabel(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static void ValidateDateRange(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate)
        {
            throw new ArgumentException("La fecha inicial no puede ser mayor que la fecha final.");
        }

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        if (days > MaxRangeDays)
        {
            throw new ArgumentException("El rango máximo permitido es de 12 meses.");
        }
    }

    private static void EnsurePdfLicenseConfigured()
    {
        if (Interlocked.Exchange(ref _pdfLicenseConfigured, 1) != 0)
        {
            return;
        }

        QuestPDF.Settings.License = LicenseType.Community;
    }

    private sealed record ReportData(
        ReportPreviewResponse Preview,
        IReadOnlyList<ExpenseItem> Expenses,
        IReadOnlyList<ExpenseItem> PreviousExpenses,
        DateOnly PreviousStartDate,
        DateOnly PreviousEndDate);
}
