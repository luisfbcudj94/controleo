namespace Controleo.Api.Options;

public sealed class ExcelStorageOptions
{
    public const string SectionName = "ExcelStorage";

    public string FilePath { get; set; } = @"C:\\Users\\Public\\DriveSync\\gastos.xlsx";
    public string WorksheetName { get; set; } = "Gastos";
    public bool CreateFileIfMissing { get; set; } = true;

    public string DateColumnHeader { get; set; } = "Fecha";
    public string DescriptionColumnHeader { get; set; } = "Descripcion";
    public string AmountColumnHeader { get; set; } = "Valor";
    public string MovementTypeColumnHeader { get; set; } = "TipoMovimiento";
    public string PaymentMethodColumnHeader { get; set; } = "MedioPago";

    public string[] MovementTypes { get; set; } = [];

    public string[] PaymentMethods { get; set; } = [];
}
