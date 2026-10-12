namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class ServiceProviderExtensions
{
    /// <summary>
    /// Resolve a service from the container, returning null instead of throwing if resolution fails.
    /// </summary>
    /// <param name="service">The service provider to resolve from.</param>
    /// <param name="type">The service type to resolve.</param>
    /// <returns>The resolved service, or null if it could not be resolved.</returns>
    public static object? GetTryService(this IServiceProvider service, Type type)
    {
        try
        {
            return service.GetService(type);
        }
        catch
        {
            return null;
        }
    }
}
