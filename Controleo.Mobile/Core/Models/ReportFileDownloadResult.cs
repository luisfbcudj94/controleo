namespace Controleo.Mobile.Core.Models;

public sealed record ReportFilePayload(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record ReportFileDownloadResult(
    bool IsSuccess,
    string Message,
    ReportFilePayload? File);
