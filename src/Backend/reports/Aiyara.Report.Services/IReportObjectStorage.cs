namespace Aiyara.Report.Services;

public interface IReportObjectStorage
{
    Task EnsureBucketAsync(CancellationToken cancellationToken = default);
    Task PutAsync(string key, Stream content, string contentType,
        CancellationToken cancellationToken = default);
    Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class ReportObjectDownload(Stream content, string contentType, IDisposable? owner = null)
    : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string ContentType { get; } = contentType;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        owner?.Dispose();
    }
}
