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
    private static readonly JsonSerializerOptions NativeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        return 0;
    }

    private static async Task<int> RunNativeMessagingAsync()
    {
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        using var shutdown = new CancellationTokenSource();
        using var outputGate = new SemaphoreSlim(1, 1);
        var connectionId = Guid.NewGuid().ToString("D");
        var commandServer = RunBrowserCommandServerAsync(
            connectionId,
            output,
            outputGate,
            shutdown.Token);
        try
        {
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
                        DateTimeOffset.Now).WithBrowserConnection(connectionId);
                    delivered = await DeliverAsync(activity);
                }
                catch
                {
                    // Invalid browser input must not crash the host or reach the tray app.
                }

                await WriteNativeMessageAsync(output, new { ok = delivered }, outputGate);
            }
        }
        finally
        {
            shutdown.Cancel();
            try
            {
                await commandServer;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static async Task RunBrowserCommandServerAsync(
        string connectionId,
        Stream nativeOutput,
        SemaphoreSlim outputGate,
        CancellationToken cancellationToken)
    {
        var pipeName = ActivityPipeNames.GetBrowserCommandPipeName(connectionId);
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);
            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var input = await reader.ReadLineAsync(cancellationToken);
            if (input is not null && BrowserActivationCommand.TryParse(input, out var command))
            {
                await WriteNativeMessageAsync(nativeOutput, command, outputGate, cancellationToken);
            }
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

    private static async Task WriteNativeMessageAsync<T>(
        Stream output,
        T value,
        SemaphoreSlim outputGate,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, NativeJsonOptions);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await outputGate.WaitAsync(cancellationToken);
        try
        {
            await output.WriteAsync(length, cancellationToken);
            await output.WriteAsync(payload, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            outputGate.Release();
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
