using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace IntraDotNet.EntityFrameworkCore.Infrastructure.Repositories.UnitOfWork;

/// <summary>
/// Defines the contract for a Unit of Work pattern implementation.
/// Manages transactions and coordinates changes across multiple repositories.
/// </summary>
/// <typeparam name="TDbContext">The type of the database context.</typeparam>
public interface IUnitOfWork<TDbContext> : IDisposable, IAsyncDisposable
    where TDbContext : DbContext
{
    /// <summary>
    /// Gets the database context.
    /// </summary>
    TDbContext Context { get; }

    /// <summary>
    /// Begins a new database transaction.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new database transaction.
    /// </summary>
    /// <returns>The database transaction.</returns>
    IDbContextTransaction BeginTransaction();

    /// <summary>
    /// Asynchronously saves all changes made in this unit of work to the database.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    ValueTask<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously saves all changes with concurrency conflict handling.
    /// </summary>
    /// <param name="handleConcurrencyConflict">Callback to handle concurrency conflicts.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    ValueTask<int> SaveChangesAsync(Func<PropertyValues, PropertyValues, PropertyValues>? handleConcurrencyConflict, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes made in this unit of work to the database.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    int SaveChanges();

    /// <summary>
    /// Resets the unit of work by disposing and recreating the context.
    /// </summary>
    ValueTask ResetAsync();

    /// <summary>
    /// Resets the unit of work by disposing and recreating the context.
    /// </summary>
    void Reset();
}
