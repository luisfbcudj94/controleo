namespace Controleo.Application.DTOs;

public sealed record ReportExportFile(
    byte[] Content,
    string ContentType,
    string FileName);
