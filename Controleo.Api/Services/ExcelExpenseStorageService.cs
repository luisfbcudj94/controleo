using ClosedXML.Excel;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Services;

public sealed class ExcelExpenseStorageService(IOptions<ExcelStorageOptions> options) : IExpenseStorageService
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private readonly ExcelStorageOptions _options = options.Value;

    public async Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        if (!IsAllowedValue(request.MovementType, _options.MovementTypes))
        {
            return new SaveExpenseResult(false, "Tipo de movimiento no permitido.", 0);
        }

        if (!IsAllowedValue(request.PaymentMethod, _options.PaymentMethods))
        {
            return new SaveExpenseResult(false, "Medio de pago no permitido.", 0);
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectoryExists(_options.FilePath);

            using var workbook = OpenOrCreateWorkbook(_options.FilePath, _options.CreateFileIfMissing);
            var worksheet = GetOrCreateWorksheet(workbook, _options.WorksheetName);

            var headerMap = EnsureHeadersAndGetColumns(worksheet);
            var nextRow = GetNextDataRow(worksheet);

            worksheet.Cell(nextRow, headerMap[_options.DateColumnHeader]).Value = request.Date.ToString("yyyy-MM-dd");
            worksheet.Cell(nextRow, headerMap[_options.DescriptionColumnHeader]).Value = request.Description.Trim();
            worksheet.Cell(nextRow, headerMap[_options.AmountColumnHeader]).Value = request.Amount;
            worksheet.Cell(nextRow, headerMap[_options.MovementTypeColumnHeader]).Value = request.MovementType.Trim();
            worksheet.Cell(nextRow, headerMap[_options.PaymentMethodColumnHeader]).Value = request.PaymentMethod.Trim();

            workbook.SaveAs(_options.FilePath);

            return new SaveExpenseResult(true, "Gasto guardado correctamente.", nextRow);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No se pudo guardar en Excel: {ex.Message}", 0);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private static bool IsAllowedValue(string value, IEnumerable<string> allowedValues)
    {
        var normalizedValue = value.Trim();
        return allowedValues.Any(item => string.Equals(item.Trim(), normalizedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("La ruta del Excel no es válida.");
        }

        Directory.CreateDirectory(directory);
    }

    private static XLWorkbook OpenOrCreateWorkbook(string filePath, bool createIfMissing)
    {
        if (File.Exists(filePath))
        {
            return new XLWorkbook(filePath);
        }

        if (!createIfMissing)
        {
            throw new FileNotFoundException("No existe el archivo de Excel configurado.", filePath);
        }

        return new XLWorkbook();
    }

    private static IXLWorksheet GetOrCreateWorksheet(XLWorkbook workbook, string worksheetName)
    {
        var worksheet = workbook.Worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, worksheetName, StringComparison.OrdinalIgnoreCase));

        return worksheet ?? workbook.Worksheets.Add(worksheetName);
    }

    private Dictionary<string, int> EnsureHeadersAndGetColumns(IXLWorksheet worksheet)
    {
        var requiredHeaders = new[]
        {
            _options.DateColumnHeader,
            _options.DescriptionColumnHeader,
            _options.AmountColumnHeader,
            _options.MovementTypeColumnHeader,
            _options.PaymentMethodColumnHeader
        };

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var firstRowCells = worksheet.Row(1).CellsUsed().ToList();
        if (firstRowCells.Count == 0)
        {
            for (var index = 0; index < requiredHeaders.Length; index++)
            {
                var column = index + 1;
                worksheet.Cell(1, column).Value = requiredHeaders[index];
                worksheet.Cell(1, column).Style.Font.Bold = true;
                map[requiredHeaders[index]] = column;
            }

            return map;
        }

        foreach (var cell in firstRowCells)
        {
            var header = cell.GetValue<string>().Trim();
            if (!string.IsNullOrWhiteSpace(header))
            {
                map[header] = cell.Address.ColumnNumber;
            }
        }

        foreach (var requiredHeader in requiredHeaders)
        {
            if (!map.ContainsKey(requiredHeader))
            {
                throw new InvalidOperationException($"No se encontró la columna requerida '{requiredHeader}' en la hoja '{_options.WorksheetName}'.");
            }
        }

        return map;
    }

    private static int GetNextDataRow(IXLWorksheet worksheet)
    {
        var lastUsedRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        return Math.Max(2, lastUsedRow + 1);
    }
}
