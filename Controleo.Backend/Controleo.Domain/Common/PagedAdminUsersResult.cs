using Controleo.Domain.Entities;

namespace Controleo.Domain.Common;

public sealed record PagedAdminUsersResult(
    IReadOnlyList<AdminUserItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);