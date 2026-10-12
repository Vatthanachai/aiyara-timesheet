namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class NullCheckExtensions
{
    /// <summary>
    /// Validate object is null. If true is null, false not null
    /// </summary>
    /// <param name="value">Object for validate is null</param>
    /// <returns>true, false</returns>
    public static bool IsNullable(this object? value) => value == null;

    /// <summary>
    /// Validate string is null or empty. If true is null/empty. false is not null/not empty
    /// </summary>
    /// <param name="value">string value</param>
    /// <returns>true, false</returns>
    public static bool IsEmpty(this string? value) => string.IsNullOrWhiteSpace(value);
}
