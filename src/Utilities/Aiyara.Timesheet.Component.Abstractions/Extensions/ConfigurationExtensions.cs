using Microsoft.Extensions.Configuration;

namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Bind a configuration section to a new instance of <typeparamref name="TModel"/>.
    /// </summary>
    /// <typeparam name="TModel">The settings type to bind to.</typeparam>
    /// <param name="configuration">The configuration to read from.</param>
    /// <param name="section">The configuration section name.</param>
    /// <returns>A new <typeparamref name="TModel"/> populated from the section.</returns>
    public static TModel GetOptions<TModel>(this IConfiguration? configuration, string section)
        where TModel : new()
    {
        TModel instance = new();
        configuration?.GetSection(section).Bind((object)instance);
        return instance;
    }
}
