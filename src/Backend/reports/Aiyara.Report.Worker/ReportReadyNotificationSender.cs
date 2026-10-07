using System.Net.Http.Json;

namespace Aiyara.Report.Worker;

public sealed class ReportReadyNotificationSender(HttpClient client, string internalKey,
    TimeSpan retryDelay)
{
    private const int MaxAttempts = 3;

    public async Task<bool> SendAsync(ReportReadyNotification request,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/report-ready")
                {
                    Content = JsonContent.Create(request)
                };
                message.Headers.Add("X-Internal-Key", internalKey);
                using var response = await client.SendAsync(message, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
                if (!IsTransient(response.StatusCode) || attempt == MaxAttempts) return false;
            }
            catch (HttpRequestException)
            {
                // Retry connection failures; the report itself is already durable.
                if (attempt == MaxAttempts) return false;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry an HTTP timeout, but preserve caller cancellation.
                if (attempt == MaxAttempts) return false;
            }

            await Task.Delay(TimeSpan.FromTicks(retryDelay.Ticks * attempt), cancellationToken);
        }

        return false;
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.RequestTimeout or
            System.Net.HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}

public sealed record ReportReadyNotification(string Email, string EmployeeName, string Period,
    string ReportUrl, Guid ReportRunId);
