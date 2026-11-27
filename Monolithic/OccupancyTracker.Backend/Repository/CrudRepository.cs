using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OccupancyTracker.IRepository;
using OccupancyTracker.Models;

namespace OccupancyTracker.Repository;

internal sealed class CrudRepository<T>(OccupancyContext context) : ICrudRepository<T> where T : class
{
    private IQueryable<T> Query(Expression<Func<T, bool>>? predicate, Expression<Func<T, object?>>[] includes)
    {
        IQueryable<T> query = context.Set<T>();
        if (predicate != null) query = query.Where(predicate);
        foreach (var include in includes) query = query.Include(include);
        return query;
    }

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes) =>
        Query(predicate, includes).ToListAsync(cancellationToken);
    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes) =>
        Query(predicate, includes).FirstOrDefaultAsync(cancellationToken);
    public Task<T?> FirstOrDefaultAsync<TKey>(Expression<Func<T, bool>> predicate,
        Expression<Func<T, TKey>> orderBy, bool descending = false, CancellationToken cancellationToken = default)
    {
        var query = context.Set<T>().Where(predicate);
        return (descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy))
            .FirstOrDefaultAsync(cancellationToken);
    }
    public Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes) =>
        Query(predicate, includes).SingleOrDefaultAsync(cancellationToken);
    public Task<T?> SingleOrDefaultDetachedAsync(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default, params Expression<Func<T, object?>>[] includes) =>
        Query(predicate, includes).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    public Task<T> SingleAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        context.Set<T>().SingleAsync(predicate, cancellationToken);
    public Task<bool> AnyAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        Query(predicate, []).AnyAsync(cancellationToken);
    public Task<int> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        context.Set<T>().Where(predicate).ExecuteDeleteAsync(cancellationToken);
    public void Add(T entity) => context.Set<T>().Add(entity);
    public void AddRange(IEnumerable<T> entities) => context.Set<T>().AddRange(entities);
    public void Update(T entity) => context.Set<T>().Update(entity);
    public void Remove(T entity) => context.Set<T>().Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => context.Set<T>().RemoveRange(entities);
}
