using System.IO.Pipes;
using System.Text.Json;
using CodexUsageTray.Core;

namespace CodexUsageTray;

internal sealed class ActivityPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<CancellationToken, Task<ActivityEvent?>> _receiveNext;
    private readonly Action<Exception> _recordUnexpectedFailure;
    private readonly Func<CancellationToken, Task> _delayAfterFailure;
    private Task? _listenerTask;

    public event Action<ActivityEvent>? ActivityReceived;

    public ActivityPipeServer()
        : this(
            ReceiveNextAsync,
            exception => DiagnosticLog.Append(exception, "Activity pipe listener failure"),
            DelayAfterFailureAsync)
    {
    }

    internal ActivityPipeServer(
        Func<CancellationToken, Task<ActivityEvent?>> receiveNext,
        Action<Exception> recordUnexpectedFailure,
        Func<CancellationToken, Task> delayAfterFailure)
    {
        _receiveNext = receiveNext ?? throw new ArgumentNullException(nameof(receiveNext));
        _recordUnexpectedFailure = recordUnexpectedFailure ??
            throw new ArgumentNullException(nameof(recordUnexpectedFailure));
        _delayAfterFailure = delayAfterFailure ?? throw new ArgumentNullException(nameof(delayAfterFailure));
    }

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
                var activity = await _receiveNext(cancellationToken).ConfigureAwait(false);
                if (activity is not null)
                {
                    ActivityReceived?.Invoke(activity);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                await _delayAfterFailure(cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                await _delayAfterFailure(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                try
                {
                    _recordUnexpectedFailure(exception);
                }
                catch
                {
                    // A diagnostic write failure must not stop notification delivery.
                }

                await _delayAfterFailure(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<ActivityEvent?> ReceiveNextAsync(CancellationToken cancellationToken)
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
        return string.IsNullOrWhiteSpace(line)
            ? null
            : JsonSerializer.Deserialize<ActivityEvent>(line);
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
