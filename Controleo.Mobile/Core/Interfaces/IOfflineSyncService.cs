namespace Controleo.Mobile.Core.Interfaces;

public interface IOfflineSyncService
{
    bool IsSyncInProgress { get; }
    Task TriggerSyncAsync(CancellationToken cancellationToken = default);
}
