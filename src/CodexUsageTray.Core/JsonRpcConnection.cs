using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageTray.Core;

public sealed class JsonRpcConnection : IAsyncDisposable
{
    private readonly TextReader _reader;
    private readonly TextWriter _writer;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _nextId;
    private int _disposed;

    public JsonRpcConnection(TextReader reader, TextWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var effectiveToken = linkedCancellation.Token;
        await _requestGate.WaitAsync(effectiveToken).ConfigureAwait(false);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = new RpcRequest(method, id, parameters);
            await WriteAsync(request, effectiveToken).ConfigureAwait(false);

            while (true)
            {
                var line = await _reader.ReadLineAsync(effectiveToken).ConfigureAwait(false);
                if (line is null)
                {
                    throw new EndOfStreamException("Codex App Server 연결이 종료되었습니다.");
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.Number ||
                    !responseId.TryGetInt32(out var numericId))
                {
                    continue;
                }

                if (numericId != id)
                {
                    throw new InvalidDataException($"예상하지 못한 JSON-RPC 응답 ID입니다: {numericId}");
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var code = error.TryGetProperty("code", out var codeProperty) && codeProperty.TryGetInt32(out var parsedCode)
                        ? parsedCode
                        : -1;
                    var message = error.TryGetProperty("message", out var messageProperty)
                        ? messageProperty.GetString() ?? "알 수 없는 JSON-RPC 오류"
                        : "알 수 없는 JSON-RPC 오류";
                    throw new JsonRpcException(code, message);
                }

                return root.TryGetProperty("result", out var result)
                    ? result.Clone()
                    : EmptyObject();
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        await _requestGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            await WriteAsync(new RpcNotification(method, parameters), linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task WriteAsync<T>(T message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        await _requestGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _reader.Dispose();
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
            _requestGate.Dispose();
            _disposeCancellation.Dispose();
        }
    }

    private sealed record RpcRequest(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Parameters);

    private sealed record RpcNotification(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Parameters);
}

public sealed class JsonRpcException : Exception
{
    public JsonRpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
