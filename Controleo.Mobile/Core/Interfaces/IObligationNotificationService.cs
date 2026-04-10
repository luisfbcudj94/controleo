using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Interfaces;

public interface IObligationNotificationService
{
    Task EnsurePermissionAsync(bool isPremium, CancellationToken cancellationToken);
    Task SyncReminderPlanAsync(bool isPremium, IReadOnlyList<ObligationItem> obligations, CancellationToken cancellationToken);
}
