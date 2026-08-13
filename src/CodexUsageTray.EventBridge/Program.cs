using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray.EventBridge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !string.Equals(args[0], "--hook", StringComparison.Ordinal))
        {
            return 0;
        }

        var input = await Console.In.ReadToEndAsync();
        var eventName = TryGetEventName(input);
        try
        {
            var activity = ActivityEventParser.ParseHook(input, DateTimeOffset.Now);
            var terminal = TerminalContextResolver.Resolve();
            activity = activity.WithTerminal(terminal.ProcessId, terminal.WindowHandle, terminal.Title);
            await DeliverAsync(activity);
        }
        catch
        {
            // An alerting failure must never change the Codex operation or approval decision.
        }
        finally
        {
            if (string.Equals(eventName, "Stop", StringComparison.Ordinal))
            {
                Console.Out.Write("{}");
            }
        }

        return 0;
    }

    private static string? TryGetEventName(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            return document.RootElement.TryGetProperty("hook_event_name", out var property)
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task DeliverAsync(ActivityEvent activity)
    {
        var payload = JsonSerializer.Serialize(activity);
        var startAttempted = false;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    ActivityPipeNames.PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                await pipe.ConnectAsync(timeout.Token);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(payload);
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
            {
                if (!startAttempted)
                {
                    startAttempted = true;
                    _ = TryStartTray();
                }

                if (attempt < 4)
                {
                    await Task.Delay(150);
                }
            }
        }
    }

    private static bool TryStartTray()
    {
        try
        {
            var directory = AppContext.BaseDirectory;
            var trayPath = Path.Combine(directory, "CodexUsageTray.exe");
            if (!File.Exists(trayPath))
            {
                return false;
            }

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = trayPath,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
