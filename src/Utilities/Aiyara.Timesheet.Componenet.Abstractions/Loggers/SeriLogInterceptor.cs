using Aiyara.Timesheet.Component.Abstractions.DataTypes.Guids;

using Castle.DynamicProxy;

using IInterceptor = Castle.DynamicProxy.IInterceptor;

namespace Aiyara.Timesheet.Component.Abstractions.Loggers;

/// <summary>
/// SeriLog Interceptor
/// </summary>
public class SeriLogInterceptor : IInterceptor
{
    private readonly ILogger _logger;
    private readonly string _guid;
    private readonly string _guidShort;

    /// <summary>
    /// SeriLog Interceptor
    /// </summary>
    /// <param name="logger"></param>
    public SeriLogInterceptor(ILogger logger)
    {
        _logger = logger;
        _guid = GuidGenerator.NewGuid(1, 9).ToString();
        _guidShort ??= _guid[..8];
    }

    /// <summary>
    /// Intercept method
    /// </summary>
    /// <param name="invocation"></param>
    public void Intercept(IInvocation invocation)
    {
        var genericName = invocation.GenericArguments is { Length: > 0 }
            ? invocation.GenericArguments[0].Name
            : string.Empty;

        var name = $"{invocation.Method.DeclaringType}.{invocation.Method.Name}{(string.IsNullOrWhiteSpace(genericName) ? string.Empty : $"<{genericName}>")}";

        var target = $"{(invocation.InvocationTarget ?? "-")}";
        var args = JsonConvert.SerializeObject(invocation.Arguments, Formatting.None, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Serialize, PreserveReferencesHandling = PreserveReferencesHandling.Objects });

        Log.ForContext("ClassName", invocation.Method.DeclaringType);
        Log.ForContext("MethodName", name);
        Log.ForContext("Request", args);
        Log.ForContext("Response", string.Empty);
        Log.ForContext("Thread", Thread.CurrentThread.ManagedThreadId);

        Log.Information("({GuidShort}) Target: {target}", _guidShort, target);

        try
        {
            _logger.Information("({guid_short}) Calling method {MethodName} with parameters {@Parameters} on trac", _guidShort, name, args);
            invocation.Proceed();

            var taskProcess = invocation.ReturnValue as Task;
            if (taskProcess == null)
            {
                return;
            }

            Task.Run(async () => await taskProcess.ConfigureAwait(false));
            if (taskProcess?.Exception != null) throw taskProcess.Exception;

            var result = GetResultFromTask(taskProcess);
            var resultJson = result == null
                ? String.Empty
                : JsonConvert.SerializeObject(result, Formatting.None, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Serialize, PreserveReferencesHandling = PreserveReferencesHandling.Objects });

            Log.ForContext("Response", resultJson);
            _logger.Information("({guid_short}) Method name: {name} - Target: {target}", _guidShort, name, target);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "({guid_short}) Error while calling method {MethodName}. {error}", _guidShort, invocation.Method.Name, ex.Message);
            throw;
        }
    }

    private object? GetResultFromTask(Task task)
    {
        try
        {
            return task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
        }
        catch
        {
            return null;
        }
    }
}