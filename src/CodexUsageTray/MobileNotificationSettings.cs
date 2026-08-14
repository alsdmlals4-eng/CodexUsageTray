using System.Text.Json;

namespace CodexUsageTray;

internal sealed record MobileNotificationSettings(bool Enabled, string Topic)
{
    public static MobileNotificationSettings Disabled { get; } = new(false, string.Empty);
}

internal sealed class MobileNotificationSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public MobileNotificationSettingsStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : Path.GetFullPath(path);
    }

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsageTrayData",
        "mobile-notifications.json");

    public MobileNotificationSettings Load()
    {
        if (!File.Exists(_path))
        {
            return MobileNotificationSettings.Disabled;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<MobileNotificationSettings>(json, SerializerOptions);
            if (settings is null)
            {
                return MobileNotificationSettings.Disabled;
            }

            return settings with { Topic = settings.Topic?.Trim() ?? string.Empty };
        }
        catch (JsonException)
        {
            return MobileNotificationSettings.Disabled;
        }
        catch (IOException)
        {
            return MobileNotificationSettings.Disabled;
        }
        catch (UnauthorizedAccessException)
        {
            return MobileNotificationSettings.Disabled;
        }
    }

    public void Save(MobileNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path) ??
            throw new InvalidOperationException("Mobile notification settings path must include a directory.");
        Directory.CreateDirectory(directory);
        var normalized = settings with { Topic = settings.Topic?.Trim() ?? string.Empty };
        var json = JsonSerializer.Serialize(normalized, SerializerOptions);
        File.WriteAllText(_path, json);
    }
}
