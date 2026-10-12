using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Routing;

namespace Aiyara.Timesheet.Component.Abstractions.Middlewares;

public class SnakeCaseParameterTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value)
    {
        if (value is null) return null;
        var str = value.ToString();
        return string.IsNullOrEmpty(str)
            ? str
            : Regex.Replace(str, "([a-z])([A-Z])", "$1_$2").ToLower();
    }
}