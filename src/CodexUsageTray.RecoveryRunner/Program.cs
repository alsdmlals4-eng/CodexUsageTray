using CodexUsageTray.Core;

namespace CodexUsageTray.RecoveryRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var command = ParseArguments(args);
            var statePath = command.StatePath;
            if (command.Mode == "run")
            {
                if (File.Exists(statePath))
                {
                    Console.Error.WriteLine("Recovery state already exists. Use 'resume --state <path>' instead of overwriting it.");
                    return 2;
                }

                var job = RecoveryJob.Parse(File.ReadAllText(command.JobPath!));
                var initial = new RecoveryExecutionState(
                    job,
                    RecoveryExecutionStatus.Pending,
                    Attempt: 0,
                    ResponseId: null,
                    OutputText: string.Empty,
                    LastError: null,
                    ClientRequestId: null,
                    UpdatedAt: DateTimeOffset.UtcNow);
                RecoveryStateStore.SaveAtomic(statePath, initial);
            }

            var state = RecoveryStateStore.Load(statePath);
            if (state.Status == RecoveryExecutionStatus.Completed)
            {
                PrintResult(state);
                return 0;
            }

            using var httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var engine = new RecoveryRunnerEngine(new ResponsesRecoveryClient(httpClient));
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            var result = await engine.ExecuteAsync(statePath, apiKey, CancellationToken.None);
            PrintResult(result);
            return result.Status switch
            {
                RecoveryExecutionStatus.Completed => 0,
                RecoveryExecutionStatus.ReconcileRequired => 3,
                _ => 1
            };
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static RunnerCommand ParseArguments(string[] args)
    {
        if (args.Length < 3 || args[0] is not ("run" or "resume"))
        {
            throw new ArgumentException(
                "Usage: RecoveryRunner run --job <job.json> [--state <state.json>] | resume --state <state.json>");
        }

        string? jobPath = null;
        string? statePath = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("RecoveryRunner option is missing a value.");
            }

            switch (args[index])
            {
                case "--job":
                    jobPath = Path.GetFullPath(args[index + 1]);
                    break;
                case "--state":
                    statePath = Path.GetFullPath(args[index + 1]);
                    break;
                default:
                    throw new ArgumentException($"Unknown RecoveryRunner option: {args[index]}");
            }
        }

        if (args[0] == "run")
        {
            if (string.IsNullOrWhiteSpace(jobPath))
            {
                throw new ArgumentException("run requires --job <job.json>.");
            }
            statePath ??= jobPath + ".state.json";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(jobPath))
            {
                throw new ArgumentException("resume accepts --state only.");
            }
            if (string.IsNullOrWhiteSpace(statePath))
            {
                throw new ArgumentException("resume requires --state <state.json>.");
            }
        }

        return new RunnerCommand(args[0], jobPath, statePath!);
    }

    private static void PrintResult(RecoveryExecutionState state)
    {
        Console.WriteLine($"status={state.Status}");
        Console.WriteLine($"job_id={state.Job.JobId}");
        Console.WriteLine($"attempt={state.Attempt}");
        if (!string.IsNullOrWhiteSpace(state.ResponseId))
        {
            Console.WriteLine($"response_id={state.ResponseId}");
        }
        if (state.Status == RecoveryExecutionStatus.Completed && !string.IsNullOrEmpty(state.OutputText))
        {
            Console.WriteLine(state.OutputText);
        }
        else if (!string.IsNullOrWhiteSpace(state.LastError))
        {
            Console.Error.WriteLine(state.LastError);
        }
    }

    private sealed record RunnerCommand(string Mode, string? JobPath, string StatePath);
}
