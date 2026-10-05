using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Aiyara.Timesheet.Component.Data.Context;

public interface IBaseDbContext : IDisposable
{
    DatabaseFacade Database { get; }
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    ChangeTracker ChangeTracker { get; }
    EntityEntry Entry(object entity);
    int SaveChanges();
    Task<int> SaveChangesAsync();
}