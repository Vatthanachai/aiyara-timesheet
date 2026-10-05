using System.Reflection;

using Aiyara.Timesheet.Component.Abstractions.Extensions;
using Aiyara.Timesheet.Component.Abstractions.Swaggers.Builders;
using Aiyara.Timesheet.Component.Abstractions.Swaggers.Filters;

using Asp.Versioning;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aiyara.Timesheet.Component.Abstractions.Swaggers;

public static class SwaggerExtension
{
    private static SwaggerSettings GetSwaggerSettings(IConfiguration configuration)
        => configuration.GetOptions<SwaggerSettings>(nameof(SwaggerSettings));

    private static MapToApiVersionAttribute? GetMapToApiVersionAttribute(ApiDescription description)
        => description.TryGetMethodInfo(out MethodInfo attrMethod) && attrMethod != null
            ? attrMethod.GetCustomAttribute<MapToApiVersionAttribute>(true)
            : null;

    private static bool IsMatchVersion(string version, MapToApiVersionAttribute apiVersionAttribute
        , IEnumerable<ApiVersion> apiVersions)
    {
        if (apiVersions is null)
        {
            return false;
        }

        if (apiVersionAttribute is null)
        {
            return apiVersions.Any(v => v.ToString().VersionFormatter() == version);
        }

        bool isValidMapVersion = apiVersionAttribute.Versions.Any(v => v.ToString().VersionFormatter() == version);
        return isValidMapVersion && apiVersions.Any(v => v.ToString().VersionFormatter() == version);
    }

    private static void AddIncludeXmlDocument(this SwaggerGenOptions options, params Assembly[] assemblies)
    {
        if (assemblies is null) return;

        foreach (Assembly assembly in assemblies)
        {
            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
            if (!File.Exists(xmlFile)) throw new FileNotFoundException($"Cannot find the file input path: [{xmlFile}]");
            options.IncludeXmlComments(xmlFile);
        }
    }

    private static SwaggerGenOptions AddSwaggerDocuments(this SwaggerGenOptions options, SwaggerSettings settings)
    {
        foreach (var item in settings.Versions)
        {
            options.SwaggerDoc(item.Version, new OpenApiInfo { Version = item.Version, Title = item.Title, Description = item.Description });
        }

        return options;
    }

    private static SwaggerGenOptions AddControllerOrders(this SwaggerGenOptions options)
    {
        options.OrderActionsBy(options =>
        {
            var orderBuilder = new SwaggerControllerActionOrderBuilder(options.ActionDescriptor);
            var newName = orderBuilder.Build();
            return newName;
        });

        return options;
    }

    private static SwaggerGenOptions AddBearerSecurityWithJWT(this SwaggerGenOptions options)
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });

        return options;
    }

    private static SwaggerGenOptions AddBearerSecurityWithPaseto(this SwaggerGenOptions options)
    {
        options.AddSecurityDefinition("Paseto", new OpenApiSecurityScheme
        {
            Description = "Paseto Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Paseto"
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Paseto", document)] = [] });

        return options;
    }

    public static IServiceCollection AddSwaggerDocument(this IServiceCollection services, params Assembly[] assemblies)
    {
        var serviceProvider = services.BuildServiceProvider();
        var httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        var configuration = serviceProvider.GetService<IConfiguration>();

        var swaggerSetting = GetSwaggerSettings(configuration);

        services
            .AddApiVersioning(options =>
            {
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = ApiVersion.Default;
                options.ReportApiVersions = true;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<RemoveVersionFromParameterFilter>();
            options.DocumentFilter<ReplaceVersionWithExtractValueInPathFilter>();

            options.AddSwaggerDocuments(swaggerSetting);
            options.DocInclusionPredicate((s, desc)
                => desc.ApplyDocInclusionPredicate(s, httpContext));
            options.AddIncludeXmlDocument(assemblies);

            if (swaggerSetting.IsEnableBearerSecurity)
            {
                options.AddBearerSecurityWithPaseto();
            }
        });

        services.AddSwaggerGenNewtonsoftSupport();

        return services;
    }

    public static string VersionFormatter(this string versionString)
    {
        var version = versionString.Length == 1 ? new Version($"{versionString}.0") : new Version(versionString);

        return version switch
        {
            { Build: > 0, Minor: > 0, Major: > 0, Revision: > 0 } => $"v{version.ToString(4)}",
            { Build: > 0, Minor: > 0, Major: > 0 } => $"v{version.ToString(3)}",
            { Build: > 0, Minor: > 0 } => $"v{version.ToString(2)}",
            _ => $"v{version}"
        };
    }

    public static bool ApplyDocInclusionPredicate(this ApiDescription description, string version, HttpContext httpContext)
    {
        if (!description.TryGetMethodInfo(out MethodInfo methodInfo)) return false;

        var versions = methodInfo.ReflectedType?.GetCustomAttributes(true)
            .OfType<ApiVersionAttribute>().SelectMany(s => s.Versions);

        var apiVersionAttribute = GetMapToApiVersionAttribute(description);
        bool isMatchVersion = IsMatchVersion(version, apiVersionAttribute, versions);

        if (isMatchVersion)
        {
            var filter = httpContext?.Request?.Query["filter"].ToString();
            var filterOut = httpContext?.Request?.Query["filterout"].ToString();

            if (!string.IsNullOrWhiteSpace(filterOut))
            {
                var filterOutArr = filterOut.Split(",");
                if (filterOutArr.Any())
                    if (filterOutArr.Any(itemFilter =>
                            description.ActionDescriptor.DisplayName.Contains(itemFilter, StringComparison.OrdinalIgnoreCase)))
                        return false;
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var filterListArr = filter.Split(",");
                if (filterListArr.Any())
                    return filterListArr.Any(itemFilter =>
                        description.ActionDescriptor.DisplayName.Contains(itemFilter, StringComparison.OrdinalIgnoreCase));
            }
        }

        return isMatchVersion;
    }
}

public class ReplaceVersionWithExtractValueInPathFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var paths = swaggerDoc.Paths
            .ToDictionary(p
                => p.Key.Replace("v{version}", swaggerDoc.Info.Version.Replace("v", "").VersionFormatter()), path => path.Value).ToList();
        swaggerDoc.Paths.Clear();
        paths.ForEach(paths => swaggerDoc.Paths.Add(paths.Key, paths.Value));
    }
}