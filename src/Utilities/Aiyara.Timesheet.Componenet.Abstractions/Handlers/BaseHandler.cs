namespace Aiyara.Timesheet.Component.Abstractions.Handlers;

public abstract class BaseHandler<TService, TRequest, TResult>(ILogger logger, TService service)
    : IBaseHandler<TRequest, TResult>
    where TService : class
    where TRequest : class
    where TResult : class
{
    public virtual Task<TResult> Handler(TRequest request)
    {
        throw new NotImplementedException();
    }
}