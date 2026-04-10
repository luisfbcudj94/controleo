namespace Controleo.Domain.Entities;
public sealed record PaymentMethodConfig(
	string Name,
	string Icon,
	bool IsCredit = false,
	int? DefaultInstallments = null,
	int? DueDayOfMonth = null,
	int? ReminderDaysBefore = null);