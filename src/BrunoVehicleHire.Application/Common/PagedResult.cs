namespace BrunoVehicleHire.Application.Common;

/// <summary>
/// The one shape every paginated query in this application returns (AD-14): a page of
/// <typeparamref name="T"/> plus the total count across every page (not just this one), and the
/// paging parameters that produced it. Serializes camelCase (items/totalCount/page/pageSize) under
/// ASP.NET Core's default JSON casing -- no extra configuration needed.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
