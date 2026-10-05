using Aiyara.Timesheet.Component.Data.Context;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Serilog;

namespace Aiyara.Timesheet.Component.Data.UnitOfWork;

public class BaseUnitOfWork<TDbContext>(TDbContext context, ILogger logger) : IBaseUnitOfWork
    where TDbContext : IBaseDbContext
{
    private bool _disposed;

    protected TDbContext Context { get; } = context;

    public bool Commit()
    {
        bool returnValue = true;
        var strategy = Context.Database.CreateExecutionStrategy();

        strategy.Execute(() =>
        {
            using var transaction = Context.Database.BeginTransaction();

            try
            {
                Context.SaveChanges();
                transaction.Commit();
            }
            catch (PostgresException ex)
            {
                logger.Error(ex, $"Error on commit data");
                returnValue = false;
                transaction.Rollback();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error on commit data");
                returnValue = false;
                transaction.Rollback();
            }
        });


        return returnValue;
    }

    public async Task<bool> CommitAsync()
    {
        bool returnValue = true;
        var strategy = Context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Context.Database.BeginTransactionAsync();

            try
            {
                await Context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (PostgresException ex)
            {
                logger.Error(ex, $"Error on commit data");
                returnValue = false;
                await transaction.RollbackAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error on commit data");
                returnValue = false;
                await transaction.RollbackAsync();
            }
        });
        return returnValue;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Context.Dispose();
            }

            _disposed = true;
        }
    }

    ~BaseUnitOfWork()
    {
        Dispose(false);
    }
}