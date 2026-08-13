using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed class BrowserActivityActivator
{
    private readonly Func<string, string, bool> _sendCommand;

    public BrowserActivityActivator()
        : this(SendCommand)
    {
    }

    internal BrowserActivityActivator(Func<string, string, bool> sendCommand)
    {
        _sendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
    }

    public bool TryActivate(ActivityEvent activity)
    {
        try
        {
            var pipeName = ActivityPipeNames.GetBrowserCommandPipeName(
                activity.BrowserConnectionId ?? string.Empty);
            var command = BrowserActivationCommand.FromActivity(activity);
            return _sendCommand(pipeName, JsonSerializer.Serialize(command));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool SendCommand(string pipeName, string payload)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.None);
            pipe.Connect(timeout: 400);
            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };
            writer.WriteLine(payload);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            return false;
        }
    }
}
