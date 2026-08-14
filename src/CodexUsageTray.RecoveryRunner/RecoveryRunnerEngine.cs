using CodexUsageTray.Core;

namespace CodexUsageTray.RecoveryRunner;

public sealed class RecoveryRunnerEngine
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    private readonly ResponsesRecoveryClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;

    public RecoveryRunnerEngine(
        ResponsesRecoveryClient client,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<RecoveryExecutionState> ExecuteAsync(
        string statePath,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var state = RecoveryStateStore.Load(statePath);
        if (state.Status == RecoveryExecutionStatus.Completed)
        {
            return state;
        }

        if (state.Status == RecoveryExecutionStatus.ReconcileRequired)
        {
            return state;
        }

        if (state.Status == RecoveryExecutionStatus.Running)
        {
            var reconcile = state with
            {
                Status = RecoveryExecutionStatus.ReconcileRequired,
                LastError = "Previous request has no confirmed terminal response; reconcile before retrying.",
                UpdatedAt = _clock()
            };
            RecoveryStateStore.SaveAtomic(statePath, reconcile);
            return reconcile;
        }

        if (state.Status == RecoveryExecutionStatus.FailedTerminal)
        {
            return state;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var failed = state with
            {
                Status = RecoveryExecutionStatus.FailedTerminal,
                LastError = "OPENAI_API_KEY is required by RecoveryRunner.",
                UpdatedAt = _clock()
            };
            RecoveryStateStore.SaveAtomic(statePath, failed);
            return failed;
        }

        while (state.Attempt < state.Job.MaxAttempts)
        {
            var running = state with
            {
                Status = RecoveryExecutionStatus.Running,
                Attempt = state.Attempt + 1,
                ResponseId = null,
                OutputText = string.Empty,
                LastError = null,
                ClientRequestId = Guid.NewGuid().ToString("D"),
                UpdatedAt = _clock()
            };
            RecoveryStateStore.SaveAtomic(statePath, running);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(running.Job.TimeoutSeconds));
            try
            {
                var response = await _client.SendAsync(running.Job, apiKey, timeout.Token);
                if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    var incomplete = running with
                    {
                        Status = RecoveryExecutionStatus.FailedTerminal,
                        ResponseId = response.ResponseId,
                        OutputText = response.OutputText,
                        LastError = $"Responses API returned terminal status '{response.Status}' instead of completed.",
                        UpdatedAt = _clock()
                    };
                    RecoveryStateStore.SaveAtomic(statePath, incomplete);
                    return incomplete;
                }

                var completed = running with
                {
                    Status = RecoveryExecutionStatus.Completed,
                    ResponseId = response.ResponseId,
                    OutputText = response.OutputText,
                    LastError = null,
                    UpdatedAt = _clock()
                };
                RecoveryStateStore.SaveAtomic(statePath, completed);
                return completed;
            }
            catch (RecoveryHttpException exception)
            {
                if (!exception.Retryable)
                {
                    var terminal = running with
                    {
                        Status = RecoveryExecutionStatus.FailedTerminal,
                        LastError = exception.Message,
                        UpdatedAt = _clock()
                    };
                    RecoveryStateStore.SaveAtomic(statePath, terminal);
                    return terminal;
                }

                if (running.Attempt >= running.Job.MaxAttempts)
                {
                    var exhausted = running with
                    {
                        Status = RecoveryExecutionStatus.FailedTerminal,
                        LastError = $"{exception.Message} Retry ceiling exhausted.",
                        UpdatedAt = _clock()
                    };
                    RecoveryStateStore.SaveAtomic(statePath, exhausted);
                    return exhausted;
                }

                state = running with
                {
                    Status = RecoveryExecutionStatus.FailedTransient,
                    LastError = exception.Message,
                    UpdatedAt = _clock()
                };
                RecoveryStateStore.SaveAtomic(statePath, state);
                var delay = RetryDelays[Math.Min(running.Attempt - 1, RetryDelays.Length - 1)];
                await _delay(delay, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                var reconcile = running with
                {
                    Status = RecoveryExecutionStatus.ReconcileRequired,
                    LastError = $"Network result is ambiguous; automatic repost is blocked: {exception.GetType().Name}",
                    UpdatedAt = _clock()
                };
                RecoveryStateStore.SaveAtomic(statePath, reconcile);
                return reconcile;
            }
            catch (OperationCanceledException exception)
            {
                var reconcile = running with
                {
                    Status = RecoveryExecutionStatus.ReconcileRequired,
                    LastError = $"Request completion is unknown after cancellation/timeout; automatic repost is blocked: {exception.GetType().Name}",
                    UpdatedAt = _clock()
                };
                RecoveryStateStore.SaveAtomic(statePath, reconcile);
                return reconcile;
            }
            catch (InvalidDataException exception)
            {
                var terminal = running with
                {
                    Status = RecoveryExecutionStatus.FailedTerminal,
                    LastError = exception.Message,
                    UpdatedAt = _clock()
                };
                RecoveryStateStore.SaveAtomic(statePath, terminal);
                return terminal;
            }
        }

        var final = state with
        {
            Status = RecoveryExecutionStatus.FailedTerminal,
            LastError = "Recovery retry ceiling exhausted.",
            UpdatedAt = _clock()
        };
        RecoveryStateStore.SaveAtomic(statePath, final);
        return final;
    }
}
