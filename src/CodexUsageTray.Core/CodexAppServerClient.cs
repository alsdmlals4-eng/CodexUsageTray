using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CodexUsageTray.Core;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string _codexExecutable;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly BoundedDiagnosticBuffer _diagnostics = new();
    private Process? _process;
    private JsonRpcConnection? _connection;
    private Task? _stderrDrainTask;
    private bool _disposed;

    public CodexAppServerClient(string codexExecutable = "codex")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codexExecutable);
        _codexExecutable = codexExecutable;
    }

    public async Task<UsageSnapshot> ReadUsageAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var connection = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
                await EnsureChatGptAccountAsync(connection, cancellationToken).ConfigureAwait(false);
                var result = await connection
                    .SendRequestAsync("account/rateLimits/read", null, cancellationToken)
                    .ConfigureAwait(false);
                return RateLimitParser.Parse(result.GetRawText(), DateTimeOffset.Now);
            }
            catch (Exception exception) when (
                attempt == 0 &&
                exception is not OperationCanceledException &&
                IsTransportFailure(exception))
            {
                await ResetAsync().ConfigureAwait(false);
            }
        }

        throw new IOException("Codex App Server 연결을 복구하지 못했습니다.");
    }

    private static async Task EnsureChatGptAccountAsync(
        JsonRpcConnection connection,
        CancellationToken cancellationToken)
    {
        var result = await connection
            .SendRequestAsync("account/read", new { refreshToken = false }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
        {
            throw new CodexAuthenticationException("Codex에서 ChatGPT 로그인이 필요합니다.");
        }

        if (account.ValueKind == JsonValueKind.Object &&
            account.TryGetProperty("type", out var typeProperty) &&
            typeProperty.ValueKind == JsonValueKind.String &&
            string.Equals(typeProperty.GetString(), "apiKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new CodexAuthenticationException("API 키 로그인이 아닌 ChatGPT 로그인이 필요합니다.");
        }
    }

    private bool IsTransportFailure(Exception exception) =>
        exception is EndOfStreamException or ObjectDisposedException ||
        exception is IOException ||
        _process is { HasExited: true };

    private async Task<JsonRpcConnection> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null && _process is { HasExited: false })
            {
                return _connection;
            }

            await ResetUnsafeAsync().ConfigureAwait(false);
            var startInfo = CreateStartInfo(_codexExecutable);
            try
            {
                _process = Process.Start(startInfo) ??
                    throw new InvalidOperationException("Codex App Server 프로세스를 시작하지 못했습니다.");
            }
            catch (Win32Exception exception)
            {
                throw new CodexCliNotFoundException(_codexExecutable, exception);
            }

            _diagnostics.Clear();
            _stderrDrainTask = DrainStandardErrorAsync(_process.StandardError);
            _connection = new JsonRpcConnection(_process.StandardOutput, _process.StandardInput);
            await _connection.SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codex_usage_tray",
                        title = "Codex Usage Tray",
                        version = "1.1.1"
                    }
                },
                cancellationToken).ConfigureAwait(false);
            await _connection.SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        catch
        {
            await ResetUnsafeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public string GetDiagnosticSummary() => _diagnostics.Snapshot();

    private async Task DrainStandardErrorAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _diagnostics.Append(line);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static ProcessStartInfo CreateStartInfo(string requestedExecutable)
    {
        var resolved = ResolveCommand(requestedExecutable);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var extension = Path.GetExtension(resolved);
        if (OperatingSystem.IsWindows() &&
            (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"\"{resolved}\" app-server\"";
        }
        else if (OperatingSystem.IsWindows() && extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "powershell.exe";
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(resolved);
            startInfo.ArgumentList.Add("app-server");
        }
        else
        {
            startInfo.FileName = resolved;
            startInfo.ArgumentList.Add("app-server");
        }

        return startInfo;
    }

    private static string ResolveCommand(string requestedExecutable)
    {
        if (Path.IsPathRooted(requestedExecutable) ||
            requestedExecutable.Contains(Path.DirectorySeparatorChar) ||
            requestedExecutable.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(requestedExecutable)
                ? Path.GetFullPath(requestedExecutable)
                : throw new CodexCliNotFoundException(requestedExecutable);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty }
            : new[] { string.Empty };
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), requestedExecutable + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new CodexCliNotFoundException(requestedExecutable);
    }

    private async Task ResetAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetUnsafeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ResetUnsafeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        if (_stderrDrainTask is not null)
        {
            try
            {
                await _stderrDrainTask.ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _stderrDrainTask = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ResetAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }
}

public sealed class CodexCliNotFoundException : Exception
{
    public CodexCliNotFoundException(string executable, Exception? innerException = null)
        : base($"Codex CLI를 찾을 수 없습니다: {executable}", innerException)
    {
    }
}

public sealed class CodexAuthenticationException : Exception
{
    public CodexAuthenticationException(string message)
        : base(message)
    {
    }
}
