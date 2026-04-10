namespace Controleo.Mobile.Core.Models;

public sealed record OfflineExpenseCreateMutationPayload(
    string LocalExpenseId,
    string MonthKey,
    ExpenseEntryRequest Request
);

public sealed record OfflineExpenseUpdateMutationPayload(
    string ExpenseId,
    string MonthKey,
    ExpenseEntryRequest Request
);

public sealed record OfflineExpenseDeleteMutationPayload(
    string ExpenseId,
    string MonthKey
);
