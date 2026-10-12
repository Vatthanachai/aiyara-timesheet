namespace Aiyara.Timesheet.Component.Abstractions.Swaggers;

/// <summary>
/// Swagger Settings
/// </summary>
public class SwaggerSettings
{
    /// <summary>
    /// Document Title
    /// </summary>
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>
    /// Is Enabled Swagger
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Is Enabled Bearer Security
    /// </summary>
    public bool IsEnableBearerSecurity { get; set; }

    /// <summary>
    /// Document Versions
    /// </summary>
    public List<DocumentVersion> Versions { get; set; } = [];

    /// <summary>
    /// Document version
    /// </summary>
    public class DocumentVersion
    {
        /// <summary>
        /// Version Title
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Version
        /// </summary>
        public string Version { get; set; } = string.Empty;
    }
}