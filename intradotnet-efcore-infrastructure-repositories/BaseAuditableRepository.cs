using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IntraDotNet.Domain.Core;

namespace IntraDotNet.EntityFrameworkCore.Infrastructure.Repositories;

/// <summary>
/// Abstract base repository class for handling auditable entities.
/// Implements Repository pattern focused on data access operations.
/// </summary>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
/// <typeparam name="TDbContext">The type of the database context.</typeparam>
public abstract class BaseAuditableRepository<TEntity, TDbContext>(TDbContext context) : BaseRepository<TEntity, TDbContext>(context), IBaseRepository<TEntity>
 where TDbContext : DbContext
 where TEntity : class, IAuditable
{
    /// <summary>
    /// Gets the queryable for the entity.
    /// </summary>
    /// <param name="withIncludes">Whether to include related entities.</param>
    /// <param name="asNoTracking">Whether to track the entity.</param>
    /// <param name="includeDeleted">Whether to include soft deleted entities.</param>
    /// <returns>The queryable for the entity.</returns>
    public override IQueryable<TEntity> GetQueryable(bool withIncludes = true, bool asNoTracking = true, bool includeDeleted = false)
    {
        IQueryable<TEntity> query = asNoTracking ? Context.Set<TEntity>().AsNoTracking() : Context.Set<TEntity>();

        // As of the current date (23-Jan-2025) the current version of EF Core does not support the HasQueryFilter method with a name parameter.
        // this means that when using the HasQueryFilter method, the filter will be applied to all queries that are executed on the entity.
        // This is not always the desired behavior, you may have 3 global query filters, the soft delete filtering being one, and you want to ignore the soft delete but keep the others.
        // This logic is commented out here until the feature becomes available in EF Core.
        // In the meantime, we will manually apply a "query filter" here manually.
        /*if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }*/

        if (!includeDeleted)
        {
            // Apply soft delete filter.
            query = query.Where(x => x.DeletedOn == null);
        }

        return withIncludes ? AddIncludes(query) : query;
    }

    /// <summary>
    /// Asynchronously adds or updates an entity based on the specified identity predicate.
    /// </summary>
    /// <param name="value">The entity to add or update.</param>
    /// <param name="identityPredicate">The predicate to identify the entity.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override async ValueTask AddOrUpdateAsync(TEntity value, Expression<Func<TEntity, bool>> identityPredicate, CancellationToken cancellationToken = default)
    {
        TEntity? existing;
        DbSet<TEntity> dbSet;

        dbSet = Context.Set<TEntity>();
        existing = await dbSet.SingleOrDefaultAsync(identityPredicate, cancellationToken);

        if (existing != null)
        {
            Context.Entry(existing).CurrentValues.SetValues(value);

            // Undelete if row was soft deleted.
            existing.DeletedOn = null;
            existing.DeletedBy = null;

            // Can't set these properties on an update.
            dbSet.Entry(existing).Property(x => x.CreatedOn).IsModified = false;
            dbSet.Entry(existing).Property(x => x.CreatedBy).IsModified = false;
        }
        else
        {
            dbSet.Add(value);

            // Can't set these properties on a new entity.
            dbSet.Entry(value).Property(x => x.LastUpdateOn).IsModified = false;
            dbSet.Entry(value).Property(x => x.LastUpdateBy).IsModified = false;
            dbSet.Entry(value).Property(x => x.DeletedOn).IsModified = false;
            dbSet.Entry(value).Property(x => x.DeletedBy).IsModified = false;
        }
    }

    /// <summary>
    /// Asynchronously deletes an entity that matches the specified identity predicate.
    /// </summary>
    /// <param name="identityPredicate">The predicate to identify the entity to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override async ValueTask DeleteAsync(Expression<Func<TEntity, bool>> identityPredicate, CancellationToken cancellationToken = default)
    {
        TEntity? existing;
        int rowsAffected;
        DbSet<TEntity> dbSet = Context.Set<TEntity>();
        IQueryable<TEntity> res;

        existing = await dbSet.SingleOrDefaultAsync(identityPredicate, cancellationToken);

        if (existing != null)
        {
            if (existing.GetType() is IRowVersion)
            {
                res = dbSet.Where(
                    x => identityPredicate.Compile().Invoke(x)
                    && ((IRowVersion)x).RowVersion == ((IRowVersion)existing).RowVersion
                );

                rowsAffected = await res.CountAsync(cancellationToken);

                if (rowsAffected == 0)
                {
                    throw new DBConcurrencyException("The record has been modified in the database. Please refresh and try again.");
                }

                await res.ForEachAsync(x =>
                {
                    x.DeletedOn = DateTime.UtcNow;
                }, cancellationToken);
            }
            else
            {
                res = dbSet.Where(
                    x => identityPredicate.Compile().Invoke(x)
                );

                rowsAffected = await res.CountAsync(cancellationToken);

                if (rowsAffected > 0)
                {
                    await res.ForEachAsync(x =>
                    {
                        x.DeletedOn = DateTime.UtcNow;
                    }, cancellationToken);
                }
            }
        }
    }
}