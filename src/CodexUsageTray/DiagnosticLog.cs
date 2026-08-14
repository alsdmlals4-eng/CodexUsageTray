using System.Diagnostics;
using System.Text;
using CodexUsageTray.Core;

namespace CodexUsageTray;

internal static class DiagnosticLog
{
    private const int MaximumLogCharacters = 128 * 1024;

    internal static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageTray");

    internal static string FilePath => Path.Combine(DirectoryPath, "diagnostics.log");

    internal static void Append(Exception exception, string? appServerDetails) =>
        Write(FilePath, exception, appServerDetails);

    internal static void AppendMobilePush(Exception exception) =>
        WriteMobilePush(FilePath, exception);

    internal static void Write(string path, Exception exception, string? appServerDetails)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exception);

        var safeMessage = BoundedDiagnosticBuffer.SanitizeForLog(exception.Message);
        var safeDetails = string.IsNullOrWhiteSpace(appServerDetails)
            ? "(App Server stderr 없음)"
            : BoundedDiagnosticBuffer.SanitizeForLog(appServerDetails.Trim());
        var entry = $"[{DateTimeOffset.Now:O}] {exception.GetType().Name}: {safeMessage}{Environment.NewLine}" +
            $"App Server stderr:{Environment.NewLine}{safeDetails}{Environment.NewLine}{Environment.NewLine}";
        AppendEntry(path, entry);
    }

    internal static void WriteMobilePush(string path, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exception);

        var status = exception is HttpRequestException httpException && httpException.StatusCode is { } statusCode
            ? $" · HTTP {(int)statusCode}"
            : string.Empty;
        var entry = $"[{DateTimeOffset.Now:O}] MobilePush {exception.GetType().Name}{status}: delivery failed." +
            $"{Environment.NewLine}{Environment.NewLine}";
        AppendEntry(path, entry);
    }

    internal static void OpenDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{DirectoryPath}\"",
            UseShellExecute = true
        });
    }

    private static void AppendEntry(string path, string entry)
    {
        var directory = Path.GetDirectoryName(path) ??
            throw new ArgumentException("Diagnostic log path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var combined = existing + entry;
        if (combined.Length > MaximumLogCharacters)
        {
            combined = combined[^MaximumLogCharacters..];
        }

        File.WriteAllText(path, combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
