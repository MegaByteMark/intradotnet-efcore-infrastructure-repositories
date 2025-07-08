using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System.ComponentModel.DataAnnotations;

namespace IntraDotNet.EntityFrameworkCore.Infrastructure.Repositories.UnitOfWork;

/// <summary>
/// Unit of Work implementation that manages database transactions and coordinates changes.
/// </summary>
/// <typeparam name="TDbContext">The type of the database context.</typeparam>
public abstract class UnitOfWork<TDbContext> : IUnitOfWork<TDbContext>
    where TDbContext : DbContext
{
    protected readonly IDbContextFactory<TDbContext> _contextFactory;
    protected TDbContext? _context;
    private readonly Dictionary<Type, object> _repositories = new();
    private bool _disposed = false;

    public UnitOfWork(IDbContextFactory<TDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <summary>
    /// Gets the database context. Created lazily and managed by the unit of work.
    /// </summary>
    protected TDbContext Context => _context ??= _contextFactory.CreateDbContext();

    TDbContext IUnitOfWork<TDbContext>.Context => Context;

    /// <summary>
    /// Gets or creates a repository of the specified type.
    /// Override this method in concrete implementations to provide repository instances.
    /// </summary>
    /// <typeparam name="TRepository">The type of repository to get or create.</typeparam>
    /// <returns>The repository instance.</returns>
    protected virtual TRepository GetRepository<TRepository>() where TRepository : class
    {
        Type type = typeof(TRepository);

        if (_repositories.TryGetValue(type, out var existingRepository))
        {
            return (TRepository)existingRepository;
        }

        TRepository repository = CreateRepository<TRepository>();
        RegisterRepository(repository);

        return repository;
    }

    /// <summary>
    /// Creates a new repository instance. Override this in concrete implementations.
    /// </summary>
    /// <typeparam name="TRepository">The type of repository to create.</typeparam>
    /// <returns>The created repository instance.</returns>
    protected abstract TRepository CreateRepository<TRepository>() where TRepository : class;

    /// <summary>
    /// Registers a repository instance with the unit of work.
    /// </summary>
    /// <typeparam name="TRepository">The type of repository.</typeparam>
    /// <param name="repository">The repository instance.</param>
    protected void RegisterRepository<TRepository>(TRepository repository) where TRepository : class
    {
        _repositories[typeof(TRepository)] = repository;
    }

    /// <summary>
    /// Gets all registered repositories.
    /// </summary>
    protected IEnumerable<object> GetAllRepositories()
    {
        return _repositories.Values;
    }

    /// <summary>
    /// Begins a new database transaction.
    /// </summary>
    ///  <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the transaction as the result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the context is disposed or not initialized.</exception>
    /// <exception cref="DbUpdateException">Thrown if there is an error starting the transaction.</exception>
    /// <exception cref="NotSupportedException">Thrown if the database provider does not support transactions.</exception>
    /// <exception cref="DBConcurrencyException">Thrown if the database is in a state that does not allow transactions.</exception>
    public async ValueTask<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Begins a new database transaction.
    /// </summary>
    ///  <returns>An IDbContextTransaction representing the transaction.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the context is disposed or not initialized.</exception>
    /// <exception cref="DbUpdateException">Thrown if there is an error starting the transaction.</exception>
    /// <exception cref="NotSupportedException">Thrown if the database provider does not support transactions.</exception>
    /// <exception cref="DBConcurrencyException">Thrown if the database is in a state that does not allow transactions.</exception>
    public IDbContextTransaction BeginTransaction()
    {
        return Context.Database.BeginTransaction();
    }

    /// <summary>
    /// Asynchronously saves all changes made in this unit of work to the database.
    /// </summary>
    ///  <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the number of state entries written to the database as the result.</returns>
    /// <exception cref="ValidationException">Thrown if validation fails for any entity.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Thrown if a concurrency conflict occurs while saving changes.</exception>
    /// <exception cref="NotSupportedException">Thrown if the entity has been deleted in the database.</exception>
    /// <exception cref="DBConcurrencyException">Thrown if the record has been modified in the database.</exception>
    public async ValueTask<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await SaveChangesAsync(null, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves all changes with concurrency conflict handling.
    /// </summary>
    ///  <param name="handleConcurrencyConflict">Optional function to handle concurrency conflicts.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, with the number of state entries written to
    /// <exception cref="ValidationException">Thrown if validation fails for any entity.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Thrown if a concurrency conflict occurs while saving changes.</exception>
    /// <exception cref="NotSupportedException">Thrown if the entity has been deleted in the database.</exception>
    /// <exception cref="DBConcurrencyException">Thrown if the record has been modified in the database.</exception>
    public async ValueTask<int> SaveChangesAsync(Func<PropertyValues, PropertyValues, PropertyValues>? handleConcurrencyConflict, CancellationToken cancellationToken = default)
    {
        bool success = false;
        PropertyValues? proposedValues, databaseValues;
        object? proposedValue, databaseValue;
        int retryCount = 0;
        int result;

        while (!success)
        {
            try
            {
                result = await Context.SaveChangesAsync(cancellationToken);
                success = true;

                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                success = false;

                foreach (EntityEntry entry in ex.Entries)
                {
                    proposedValues = entry.CurrentValues;
                    databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);

                    if (databaseValues == null)
                    {
                        throw new NotSupportedException("The entity has been deleted in the database.");
                    }

                    if (handleConcurrencyConflict != null)
                    {
                        proposedValues = handleConcurrencyConflict(proposedValues, databaseValues);
                    }
                    else
                    {
                        // Default concurrency resolution - use proposed values
                        foreach (Microsoft.EntityFrameworkCore.Metadata.IProperty property in proposedValues.Properties)
                        {
                            proposedValue = proposedValues[property];
                            databaseValue = databaseValues[property];
                            proposedValues[property] = proposedValue;
                        }
                    }

                    // Refresh original values to bypass next concurrency check
                    entry.OriginalValues.SetValues(databaseValues);
                }

                // Add jitter to avoid thundering herd
                await Task.Delay(new Random().Next(0, 1000), cancellationToken);

                if (retryCount++ > 5)
                {
                    throw new DBConcurrencyException("The record has been modified in the database. 5 attempts to retry have failed, please refresh the data and try again.");
                }
            }
        }

        return 0; // Should never reach here, bad times
    }

    /// <summary>
    /// Saves all changes made in this unit of work to the database.
    /// </summary>
    ///  <returns>The number of state entries written to the database.</returns>
    /// <exception cref="ValidationException">Thrown if validation fails for any entity.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Thrown if a concurrency conflict occurs while saving changes.</exception>
    /// <exception cref="NotSupportedException">Thrown if the entity has been deleted in the database.</exception>
    /// <exception cref="DBConcurrencyException">Thrown if the record has been modified in the database.</exception>
    public int SaveChanges()
    {
        var task = SaveChangesAsync().AsTask();

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Resets the unit of work asynchronously.
    /// </summary>
    ///  <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask ResetAsync()
    {
        // Clear repositories so they get recreated with the new context
        _repositories.Clear();

        if (_context != null)
        {
            await _context.DisposeAsync();
            _context = null;
        }
    }

    /// <summary>
    /// Resets the unit of work.
    /// </summary>
    ///  <exception cref="ObjectDisposedException">Thrown if the unit of work has already been disposed.</exception>
    public void Reset()
    {
        // Clear repositories so they get recreated with the new context
        _repositories.Clear();

        _context?.Dispose();
        _context = null;
    }

    /// <summary>
    ///  Disposes the unit of work and its resources.
    ///  This method is called by the Dispose method and can be overridden in derived classes.
    ///  It is responsible for disposing the database context and any other resources.
    ///  If disposing is true, it indicates that the method has been called directly or indirectly
    ///  by a user's code, and managed resources can be disposed.
    ///  If disposing is false, it indicates that the method has been called by the finalizer and only unmanaged resources should be disposed.
    ///  After disposing, the _disposed flag is set to true to prevent multiple disposals.
    ///  This method should not be called directly; instead, use the Dispose method to ensure proper disposal of resources.
    /// </summary>
    ///  <param name="disposing">Indicates whether the method has been called directly or indirectly by a user's code.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _context?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Disposes the unit of work and its resources.
    /// This method is called by the Dispose method and can be overridden in derived classes.
    /// It is responsible for disposing the database context and any other resources.
    /// If disposing is true, it indicates that the method has been called directly or indirectly
    /// by a user's code, and managed resources can be disposed.
    /// If disposing is false, it indicates that the method has been called by the finalizer and only unmanaged resources should be disposed.
    /// After disposing, the _disposed flag is set to true to prevent multiple disposals.
    /// This method should not be called directly; instead, use the Dispose method to ensure proper disposal of resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the unit of work and its resources.
    /// This method is called by the DisposeAsync method and can be overridden in derived classes.
    /// It is responsible for disposing the database context and any other resources.
    /// If the context is not null, it will be disposed asynchronously.
    /// After disposing, the _disposed flag is set to true to prevent multiple disposals.
    /// This method should not be called directly; instead, use the DisposeAsync method to ensure proper disposal of resources.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
    }

    /// <summary>
    /// Asynchronously disposes the unit of work and its resources.
    /// This method is called by the DisposeAsync method and can be overridden in derived classes.
    /// It is responsible for disposing the database context and any other resources.
    /// If the context is not null, it will be disposed asynchronously.
    /// After disposing, the _disposed flag is set to true to prevent multiple disposals.
    /// This method should not be called directly; instead, use the DisposeAsync method to ensure proper disposal of resources.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }
}