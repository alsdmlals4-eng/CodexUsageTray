using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageTray.Core;

public static class RecoveryStateStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static RecoveryExecutionState Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<RecoveryExecutionState>(json, Options)
                ?? throw new InvalidDataException("Recovery state JSON is empty.");
            state.Validate();
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Recovery state JSON is invalid.", exception);
        }
    }

    public static void SaveAtomic(string path, RecoveryExecutionState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Recovery state path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(state, Options);
        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Cleanup failure must not overwrite the primary save outcome.
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = RecoveryJob.JsonOptions();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
