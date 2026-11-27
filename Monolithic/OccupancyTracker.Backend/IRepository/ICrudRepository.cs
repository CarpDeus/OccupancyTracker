using System.Linq.Expressions;

namespace OccupancyTracker.IRepository;

public interface ICrudRepository<T> where T : class
{
    Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes);
    Task<T?> FirstOrDefaultAsync<TKey>(Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy, bool descending = false, CancellationToken cancellationToken = default);
    Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes);
    Task<T?> SingleOrDefaultDetachedAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes);
    Task<T> SingleAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
