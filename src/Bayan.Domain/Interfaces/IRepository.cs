using System.Linq.Expressions;

namespace Bayan.Domain.Interfaces;

/// <summary>
/// Generic repository interface providing standard CRUD operations.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken, params string[] includes);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken, params string[] includes);
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken, params string[] includes);
    /// <summary>
    /// Retrieves one page of entities, filtered, ordered and projected entirely in the
    /// database. Use the selector to exclude large columns from the materialized results.
    /// </summary>
    Task<(IReadOnlyList<TResult> Items, int TotalCount)> GetPagedAsync<TResult>(
        Expression<Func<T, bool>>? predicate,
        Expression<Func<T, object>> orderBy,
        bool descending,
        int pageNumber,
        int pageSize,
        Expression<Func<T, TResult>> selector,
        CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Delete(T entity);
    /// <summary>
    /// Deletes every matching row in one statement, without loading them and without waiting for
    /// <c>SaveChangesAsync</c>. For bulk housekeeping such as log retention — never for rows the
    /// current unit of work is tracking, which would not see the delete.
    /// </summary>
    Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
}
