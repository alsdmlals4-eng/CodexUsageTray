using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexUsageTray.Core;

namespace CodexUsageTray.RecoveryRunner;

public sealed record RecoveryResponse(
    string ResponseId,
    string Status,
    string OutputText);

public sealed class RecoveryHttpException : Exception
{
    public RecoveryHttpException(int statusCode, bool retryable, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Retryable = retryable;
    }

    public int StatusCode { get; }
    public bool Retryable { get; }
}

public sealed class ResponsesRecoveryClient
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");
    private const int MaxResponseCharacters = 2_000_000;
    private readonly HttpClient _httpClient;

    public ResponsesRecoveryClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<RecoveryResponse> SendAsync(
        RecoveryJob job,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var payload = JsonSerializer.Serialize(new
        {
            model = job.Model,
            input = job.Prompt,
            store = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > MaxResponseCharacters)
        {
            throw new InvalidDataException("Responses API payload exceeded the local checkpoint limit.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            throw new RecoveryHttpException(
                statusCode,
                IsRetryableStatus(statusCode),
                $"Responses API returned HTTP {statusCode}.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var responseId = RequiredString(root, "id");
            var status = OptionalString(root, "status") ?? "completed";
            var outputText = ExtractOutputText(root);
            return new RecoveryResponse(responseId, status, outputText);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Responses API returned invalid JSON.", exception);
        }
    }

    private static bool IsRetryableStatus(int statusCode) =>
        statusCode is 408 or 409 or 429 || statusCode >= 500;

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) &&
            direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var pieces = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                var type = OptionalString(part, "type");
                if (!string.Equals(type, "output_text", StringComparison.Ordinal) ||
                    !part.TryGetProperty("text", out var text) ||
                    text.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                pieces.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(string.Empty, pieces);
    }

    private static string RequiredString(JsonElement root, string propertyName) =>
        OptionalString(root, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Responses API field is missing: {propertyName}");

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
}
