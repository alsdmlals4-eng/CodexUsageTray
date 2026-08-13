using System.IO.Pipes;
using System.Text.Json;
using CodexUsageTray.Core;

namespace CodexUsageTray;

internal sealed class ActivityPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;

    public event Action<ActivityEvent>? ActivityReceived;

    public void Start()
    {
        _listenerTask ??= Task.Run(() => ListenAsync(_shutdown.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ActivityPipeNames.PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var activity = JsonSerializer.Deserialize<ActivityEvent>(line);
                    if (activity is not null)
                    {
                        ActivityReceived?.Invoke(activity);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                await DelayAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await DelayAfterFailureAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DelayAfterFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            if (_listenerTask is not null)
            {
                await _listenerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }
}
