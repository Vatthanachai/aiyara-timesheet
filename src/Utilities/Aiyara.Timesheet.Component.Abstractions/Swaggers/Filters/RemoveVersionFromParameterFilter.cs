using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aiyara.Timesheet.Component.Abstractions.Swaggers.Filters;

public class RemoveVersionFromParameterFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Only applicable to routes that literally embed "{version}" in the path (e.g.
        // "api/v{version}/..."). None of this app's controllers do - versioning is implicit
        // via AssumeDefaultVersionWhenUnspecified - so there's usually nothing to remove.
        if (operation.Parameters is not { } parameters) return;
        var versionParameter = parameters.SingleOrDefault(p => p.Name == "version");
        if (versionParameter is not null)
        {
            parameters.Remove(versionParameter);
        }
    }
}