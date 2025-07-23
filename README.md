# IntraDotNet.EFCore.Infrastructure.Repositories

A convenience library that provides a comprehensive implementation of the Repository and Unit of Work patterns for Entity Framework Core, designed to eliminate boilerplate code and streamline data access operations.

## Features

- **Repository Pattern Implementation**: Base repository classes with common CRUD operations
- **Unit of Work Pattern**: Coordinate changes across multiple repositories with transaction management
- **Auditable Entity Support**: Built-in support for entities with audit fields (Created, Updated, Deleted)
- **Soft Delete Support**: Automatic handling of soft-deleted entities
- **Concurrency Control**: Built-in optimistic concurrency handling with retry logic
- **Async/Await Support**: Full async operations with cancellation token support
- **Generic Type Safety**: Strongly-typed repositories and operations

## Installation

```bash
dotnet add package IntraDotNet.EFCore.Infrastructure.Repositories
```

## Dependencies

- Microsoft.EntityFrameworkCore 9.0.7+
- IntraDotNet.Application.Core 1.0.1+
- .NET 9.0

## Quick Start

### 1. Define Your Entities

For basic entities, implement any class:

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```

For auditable entities, implement `IAuditable` from `IntraDotNet.Infrastructure.Core`:

```csharp
public class AuditableProduct : IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    // Audit fields
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastUpdateOn { get; set; }
    public string? LastUpdateBy { get; set; }
    public DateTime? DeletedOn { get; set; }
    public string? DeletedBy { get; set; }
}
```

### 2. Create Repository Implementations

For basic entities, inherit from [`BaseRepository`](intradotnet-efcore-infrastructure-repositories/BaseRepository.cs):

```csharp
public class ProductRepository : BaseRepository<Product, MyDbContext>
{
    public ProductRepository(MyDbContext context) : base(context) { }
    
    protected override IQueryable<Product> AddIncludes(IQueryable<Product> query)
    {
        // Add any includes for related entities
        return query.Include(p => p.Category)
                   .Include(p => p.Reviews);
    }
}
```

For auditable entities, inherit from [`BaseAuditableRepository`](intradotnet-efcore-infrastructure-repositories/BaseAuditableRepository.cs):

```csharp
public class AuditableProductRepository : BaseAuditableRepository<AuditableProduct, MyDbContext>
{
    public AuditableProductRepository(MyDbContext context) : base(context) { }
    
    protected override IQueryable<AuditableProduct> AddIncludes(IQueryable<AuditableProduct> query)
    {
        return query.Include(p => p.Category);
    }
}
```

### 3. Implement Unit of Work

Create a concrete implementation of the [`UnitOfWork`](intradotnet-efcore-infrastructure-repositories/UnitOfWork/UnitOfWork.cs) class:

```csharp
public class MyUnitOfWork : UnitOfWork<MyDbContext>
{
    public MyUnitOfWork(IDbContextFactory<MyDbContext> contextFactory) 
        : base(contextFactory) { }

    protected override TRepository CreateRepository<TRepository>()
    {
        return typeof(TRepository).Name switch
        {
            nameof(ProductRepository) => (TRepository)(object)new ProductRepository(Context),
            nameof(AuditableProductRepository) => (TRepository)(object)new AuditableProductRepository(Context),
            _ => throw new NotSupportedException($"Repository type {typeof(TRepository).Name} is not supported")
        };
    }

    public ProductRepository Products => GetRepository<ProductRepository>();
    public AuditableProductRepository AuditableProducts => GetRepository<AuditableProductRepository>();
}
```

### 4. Register Services

Register your services in your DI container:

```csharp
services.AddDbContextFactory<MyDbContext>(options =>
    options.UseSqlServer(connectionString));

services.AddScoped<MyUnitOfWork>();
services.AddScoped<ProductRepository>();
services.AddScoped<AuditableProductRepository>();
```

## Usage Examples

### Basic Repository Operations

```csharp
public class ProductService
{
    private readonly ProductRepository _productRepository;
    
    public ProductService(ProductRepository productRepository)
    {
        _productRepository = productRepository;
    }
    
    // Get a single product
    public async Task<Product?> GetProductAsync(int id)
    {
        return await _productRepository.GetAsync(p => p.Id == id);
    }
    
    // Get all products
    public async Task<IEnumerable<Product>> GetAllProductsAsync()
    {
        return await _productRepository.GetAllAsync();
    }
    
    // Find products by criteria
    public async Task<IEnumerable<Product>> GetExpensiveProductsAsync()
    {
        return await _productRepository.FindAsync(p => p.Price > 100);
    }
    
    // Add or update a product
    public async Task SaveProductAsync(Product product)
    {
        await _productRepository.AddOrUpdateAsync(product, p => p.Id == product.Id);
    }
    
    // Delete a product
    public async Task DeleteProductAsync(int id)
    {
        await _productRepository.DeleteAsync(p => p.Id == id);
    }
}
```

### Unit of Work with Transactions

```csharp
public class OrderService
{
    private readonly MyUnitOfWork _unitOfWork;
    
    public OrderService(MyUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    
    public async Task ProcessOrderAsync(Order order)
    {
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        
        try
        {
            // Add the order
            await _unitOfWork.Orders.AddOrUpdateAsync(order, o => o.Id == order.Id);
            
            // Update product inventory
            foreach (var item in order.Items)
            {
                var product = await _unitOfWork.Products.GetAsync(p => p.Id == item.ProductId);
                if (product != null)
                {
                    product.Stock -= item.Quantity;
                    await _unitOfWork.Products.AddOrUpdateAsync(product, p => p.Id == product.Id);
                }
            }
            
            // Save all changes
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

### Working with Auditable Entities

```csharp
public class AuditableProductService
{
    private readonly AuditableProductRepository _repository;
    
    public AuditableProductService(AuditableProductRepository repository)
    {
        _repository = repository;
    }
    
    // Get all products including soft-deleted ones
    public async Task<IEnumerable<AuditableProduct>> GetAllProductsIncludingDeletedAsync()
    {
        return await _repository.GetAllAsync(includeDeleted: true);
    }
    
    // Soft delete a product (sets DeletedOn timestamp)
    public async Task SoftDeleteProductAsync(int id)
    {
        await _repository.DeleteAsync(p => p.Id == id);
    }
    
    // The repository automatically handles audit fields
    public async Task CreateProductAsync(AuditableProduct product)
    {
        product.CreatedOn = DateTime.UtcNow;
        product.CreatedBy = "current-user";
        
        await _repository.AddOrUpdateAsync(product, p => p.Id == product.Id);
    }
}
```

## Repository Interface

The [`IBaseRepository<TEntity>`](intradotnet-efcore-infrastructure-repositories/IBaseRepository.cs) interface provides the following operations:

### Query Operations
- `GetQueryable()` - Get an IQueryable for custom queries
- `GetAsync()` - Get a single entity by predicate
- `GetAllAsync()` - Get all entities
- `FindAsync()` - Find entities by predicate
- `Get()`, `GetAll()`, `Find()` - Synchronous versions

### Modification Operations
- `AddOrUpdateAsync()` - Add new or update existing entity
- `DeleteAsync()` - Delete entity (soft delete for auditable entities)
- `AddOrUpdate()`, `Delete()` - Synchronous versions

### Method Parameters
- `withIncludes` - Include related entities (default: true)
- `asNoTracking` - Use no-tracking queries for read-only operations (default: true)
- `includeDeleted` - Include soft-deleted entities for auditable repositories (default: false)

## Unit of Work Interface

The [`IUnitOfWork<TDbContext>`](intradotnet-efcore-infrastructure-repositories/UnitOfWork/IUnitOfWork.cs) interface provides:

### Transaction Management
- `BeginTransactionAsync()` / `BeginTransaction()` - Start new transaction
- `SaveChangesAsync()` / `SaveChanges()` - Save all pending changes
- `SaveChangesAsync(handleConcurrencyConflict)` - Save with custom concurrency handling

### Context Management
- `Context` - Access to the underlying DbContext
- `ResetAsync()` / `Reset()` - Reset and recreate the context
- `DisposeAsync()` / `Dispose()` - Proper resource cleanup

## Advanced Features

### Concurrency Handling

The Unit of Work includes built-in optimistic concurrency handling with automatic retry logic:

```csharp
// Automatic retry with default conflict resolution
await _unitOfWork.SaveChangesAsync();

// Custom concurrency conflict handling
await _unitOfWork.SaveChangesAsync((proposed, database) =>
{
    // Custom logic to resolve conflicts
    // Return the values to use for the update
    return proposed; // Use proposed values
});
```

### Custom Query Operations

Override `AddIncludes` in your repository to define default related entity loading:

```csharp
protected override IQueryable<Product> AddIncludes(IQueryable<Product> query)
{
    return query
        .Include(p => p.Category)
        .Include(p => p.Reviews)
        .ThenInclude(r => r.Customer);
}
```

### Soft Delete Behavior

For auditable entities, the `DeleteAsync` method performs soft deletes by setting the `DeletedOn` timestamp instead of removing the record from the database.

## Error Handling

The library includes comprehensive error handling:

- `ValidationException` - Entity validation failures
- `DbUpdateConcurrencyException` - Concurrency conflicts
- `DBConcurrencyException` - Custom concurrency errors with retry exhaustion
- `NotSupportedException` - Unsupported operations or deleted entities

## Best Practices

1. **Use Async Methods**: Always prefer async operations for better scalability
2. **Leverage Unit of Work**: Use transactions for operations spanning multiple repositories
3. **Configure Includes**: Override `AddIncludes` to optimize related data loading
4. **Handle Concurrency**: Implement proper concurrency conflict resolution
5. **Proper Disposal**: Use `using` statements or DI container lifetime management

## Contributing

Contributions are welcome! Please feel free to submit issues and enhancement requests.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Repository

[GitHub Repository](https://github.com/MegaByteMark/intradotnet-entityframeworkcore-infrastructure-repositories)