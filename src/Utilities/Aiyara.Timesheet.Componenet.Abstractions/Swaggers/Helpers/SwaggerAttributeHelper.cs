using Aiyara.Timesheet.Component.Abstractions.Swaggers.Attributes;

namespace Aiyara.Timesheet.Component.Abstractions.Swaggers.Helpers;

public class SwaggerAttributeHelper
{
    /// <summary>
    /// Get <see cref="List{T}"/> of <see cref="Type"/> of <see cref="Attribute"/>
    /// </summary>
    public static IEnumerable<Type> AttributeTypes => new List<Type>
    {
        typeof(SwaggerControllerOrderAttribute),
        typeof(SwaggerActionOrderAttribute),
    };

    /// <summary>
    /// Validate <paramref name="type"/> is exist <see cref="AttributeTypes"/>.
    /// </summary>
    /// <param name="type">The <paramref name="type"/> of attribute.</param>
    /// <returns>
    /// <see langword="true"/> exist, Otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsExist(Type type)
        => AttributeTypes.Any(w => w == type);

    /// <summary>
    /// Validate <paramref name="type"/> is type of <see cref="SwaggerActionOrderAttribute"/>
    /// </summary>
    /// <param name="type">The <paramref name="type"/> of attribute for validate.</param>
    /// <returns>
    /// If <see keyword="true"/> <paramref name="type"/> is equals type of <see cref="SwaggerActionOrderAttribute"/>, 
    /// Otherwise <see langword="false"/>
    /// </returns>
    public static bool IsTypeOfSwaggerActionOrderAttribute(Type type)
        => type == typeof(SwaggerActionOrderAttribute);

    /// <summary>
    /// Validate <paramref name="type"/> is type of <see cref="SwaggerControllerOrderAttribute"/>
    /// </summary>
    /// <param name="type">The <paramref name="type"/> of attribute for validate.</param>
    /// <returns>
    /// If <see keyword="true"/> <paramref name="type"/> is equals type of <see cref="SwaggerControllerOrderAttribute"/>, 
    /// Otherwise <see langword="false"/>
    /// </returns>
    public static bool IsTypeOfSwaggerControllerOrderAttribute(Type type)
        => type == typeof(SwaggerControllerOrderAttribute);
}