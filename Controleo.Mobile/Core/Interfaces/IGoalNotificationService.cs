using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Interfaces;

public interface IGoalNotificationService
{
    Task EnsurePermissionAsync(bool isPremium, CancellationToken cancellationToken);
    Task SyncGoalRemindersAsync(bool isPremium, IReadOnlyList<SavingsGoalItem> goals, CancellationToken cancellationToken);
}
