using System.Data;
using System.Linq.Expressions;
using IntraDotNet.Application.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IntraDotNet.EntityFrameworkCore.Infrastructure.Repositories;

public abstract class BaseRepository<TEntity, TDbContext> : IBaseRepository<TEntity>
 where TDbContext : DbContext
 where TEntity : class
{
    private readonly TDbContext _context;

    protected BaseRepository(TDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Gets the database context.
    /// </summary>
    protected TDbContext Context => _context;

    /// <summary>
    /// Adds includes to the query. Override this method to add includes to the query in each concrete repository.
    /// </summary>
    /// <param name="query">The query to which includes will be added.</param>
    /// <returns>The query with includes added.</returns>
    protected virtual IQueryable<TEntity> AddIncludes(IQueryable<TEntity> query)
    {
        return query;
    }

    /// <summary>
    /// Gets the queryable for the entity.
    /// </summary>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <returns>The queryable for the entity.</returns>
    public virtual IQueryable<TEntity> GetQueryable(bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false)
    {
        IQueryable<TEntity> query = asNoTracking ? Context.Set<TEntity>().AsNoTracking() : Context.Set<TEntity>();

        return withIncludes ? AddIncludes(query) : query;
    }

    /// <summary>
    /// Asynchronously gets an entity that matches the specified identity predicate.
    /// </summary>
    /// <param name="identityPredicate">The predicate to identify the entity.</param>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the entity if found; otherwise, null.</returns>
    public virtual async ValueTask<TEntity?> GetAsync(Expression<Func<TEntity, bool>> identityPredicate, bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        return (await FindAsync(identityPredicate, withIncludes, asNoTracking, includeDeleted, cancellationToken)).FirstOrDefault();
    }

    /// <summary>
    /// Asynchronously gets all entities.
    /// </summary>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an enumerable of entities.</returns>
    public virtual async ValueTask<IEnumerable<TEntity>> GetAllAsync(bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        return await GetQueryable(withIncludes, asNoTracking, includeDeleted).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Asynchronously finds entities that match the specified predicate.
    /// </summary>
    /// <param name="wherePredicate">The predicate to filter the entities.</param>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an enumerable of entities that match the predicate.</returns>
    public virtual async ValueTask<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> wherePredicate, bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        return await GetQueryable(withIncludes, asNoTracking, includeDeleted).Where(wherePredicate).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Asynchronously adds or updates an entity based on the specified identity predicate.
    /// </summary>
    /// <param name="value">The entity to add or update.</param>
    /// <param name="identityPredicate">The predicate to identify the entity.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual async ValueTask AddOrUpdateAsync(TEntity value, Expression<Func<TEntity, bool>> identityPredicate, CancellationToken cancellationToken = default)
    {
        TEntity? existing;
        DbSet<TEntity> dbSet;

        dbSet = Context.Set<TEntity>();
        existing = await dbSet.SingleOrDefaultAsync(identityPredicate, cancellationToken);

        if (existing != null)
        {
            Context.Entry(existing).CurrentValues.SetValues(value);
        }
        else
        {
            dbSet.Add(value);
        }
    }

    /// <summary>
    /// Asynchronously deletes an entity that matches the specified identity predicate.
    /// </summary>
    /// <param name="identityPredicate">The predicate to identify the entity to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// If the entity implements <see cref="IRowVersion"/>, it will check for concurrency by comparing the row version.
    /// If the row version does not match, it will throw a <see cref="DBConcurrencyException"/>.
    /// If the entity does not implement <see cref="IRowVersion"/>, it will simply HARD delete the entity.
    /// </remarks>
    public virtual async ValueTask DeleteAsync(Expression<Func<TEntity, bool>> identityPredicate, CancellationToken cancellationToken = default)
    {
        DbSet<TEntity> dbSet = Context.Set<TEntity>();
        TEntity? existing = await dbSet.SingleOrDefaultAsync(identityPredicate, cancellationToken);

        if (existing != null)
        {
            // Mark for deletion - actual deletion happens at SaveChanges
            dbSet.Remove(existing);
        }
    }

    /// <summary>
    /// Adds or updates an entity based on the specified identity predicate.
    /// </summary>
    /// <param name="value">The entity to add or update.</param>
    /// <param name="identityPredicate">The predicate to identify the entity.</param>
    public void AddOrUpdate(TEntity value, Expression<Func<TEntity, bool>> identityPredicate)
    {
        AddOrUpdateAsync(value, identityPredicate).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Deletes an entity that matches the specified identity predicate.
    /// </summary>
    /// <param name="identityPredicate">The predicate to identify the entity to delete.</param>
    public void Delete(Expression<Func<TEntity, bool>> identityPredicate)
    {
        DeleteAsync(identityPredicate).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Finds entities that match the specified predicate.
    /// </summary>
    /// <param name="wherePredicate">The predicate to filter the entities.</param>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <returns>An enumerable of entities that match the predicate.</returns>
    public IEnumerable<TEntity> Find(Expression<Func<TEntity, bool>> wherePredicate, bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false)
    {
        return FindAsync(wherePredicate, withIncludes, asNoTracking, includeDeleted).Result;
    }

    /// <summary>
    /// Gets all entities.
    /// </summary>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <returns>An enumerable of all entities.</returns>
    public IEnumerable<TEntity> GetAll(bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false)
    {
        return GetAllAsync(withIncludes, asNoTracking, includeDeleted).Result;
    }

    /// <summary>
    /// Gets an entity that matches the specified identity predicate.
    /// </summary>
    /// <param name="identityPredicate">The predicate to identify the entity.</param>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <returns>The entity that matches the predicate, or null if no entity is found.</returns>
    public TEntity? Get(Expression<Func<TEntity, bool>> identityPredicate, bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false)
    {
        return GetAsync(identityPredicate, withIncludes, asNoTracking, includeDeleted).Result;
    }
}
