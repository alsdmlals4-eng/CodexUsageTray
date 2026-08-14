using System.Text;
using System.Text.Json;

namespace CodexUsageTray;

internal sealed class NtfyPushClient : IDisposable
{
    private static readonly Uri Endpoint = new("https://ntfy.sh", UriKind.Absolute);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public NtfyPushClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = DefaultTimeout };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public async Task SendAsync(
        string topic,
        string title,
        string message,
        int priority,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(message);
        if (priority is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        var payload = JsonSerializer.Serialize(new
        {
            topic = topic.Trim(),
            title,
            message,
            priority
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
