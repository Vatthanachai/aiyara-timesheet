namespace Aiyara.Timesheet.Component.Abstractions.Services;

public interface IBaseService<TEntity> : IDisposable
    where TEntity : class;