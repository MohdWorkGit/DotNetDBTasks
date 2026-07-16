using System.Linq.Expressions;

namespace DotNetDBTasks.Domain.Interfaces;

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
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
}
