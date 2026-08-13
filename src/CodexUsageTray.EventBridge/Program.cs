using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray.EventBridge;

internal static class Program
{
    private const int MaximumNativeMessageBytes = 64 * 1024;
    private const string AllowedExtensionOrigin = "chrome-extension://mgeacoaocoijccehjlolcedfbhbaifhl/";

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => string.Equals(
                argument,
                AllowedExtensionOrigin,
                StringComparison.OrdinalIgnoreCase)))
        {
            return await RunNativeMessagingAsync();
        }

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

    private static async Task<int> RunNativeMessagingAsync()
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        while (true)
        {
            var lengthBytes = new byte[sizeof(int)];
            var lengthRead = await ReadExactlyOrEofAsync(input, lengthBytes);
            if (lengthRead == 0)
            {
                return 0;
            }

            if (lengthRead != lengthBytes.Length)
            {
                return 1;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length <= 0 || length > MaximumNativeMessageBytes)
            {
                return 1;
            }

            var payloadBytes = new byte[length];
            if (await ReadExactlyOrEofAsync(input, payloadBytes) != length)
            {
                return 1;
            }

            var delivered = false;
            try
            {
                var activity = BrowserActivityEventParser.Parse(
                    Encoding.UTF8.GetString(payloadBytes),
                    DateTimeOffset.Now);
                delivered = await DeliverAsync(activity);
            }
            catch
            {
                // Invalid browser input must not crash the host or reach the tray app.
            }

            await WriteNativeResponseAsync(output, delivered);
        }
    }

    private static async Task<int> ReadExactlyOrEofAsync(Stream input, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static async Task WriteNativeResponseAsync(Stream output, bool delivered)
    {
        var payload = Encoding.UTF8.GetBytes(delivered ? "{\"ok\":true}" : "{\"ok\":false}");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await output.WriteAsync(length);
        await output.WriteAsync(payload);
        await output.FlushAsync();
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

    private static async Task<bool> DeliverAsync(ActivityEvent activity)
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
                return true;
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

        return false;
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
