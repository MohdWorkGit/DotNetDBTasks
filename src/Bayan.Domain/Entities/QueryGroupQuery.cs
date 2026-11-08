namespace Bayan.Domain.Entities;

/// <summary>
/// Files a query in a group. A query may sit in any number of groups — the same lookup can
/// belong to both "Sales" and "Finance" — and a grant on any one of them reaches it.
/// </summary>
public class QueryGroupQuery
{
    public Guid QueryGroupId { get; set; }
    public QueryGroup QueryGroup { get; set; } = null!;

    public Guid DynamicQueryId { get; set; }
    public DynamicQuery DynamicQuery { get; set; } = null!;
}
