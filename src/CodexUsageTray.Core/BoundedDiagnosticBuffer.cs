using System.Text.RegularExpressions;

namespace CodexUsageTray.Core;

public sealed class BoundedDiagnosticBuffer
{
    private static readonly Regex BearerCredential = new(
        "(?i)(Bearer\\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex JsonCredential = new(
        "(?i)(\\\"(?:accessToken|refreshToken|apiKey|authorization)\\\"\\s*:\\s*\\\")[^\\\"]*(\\\")",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly int _maxCharacters;
    private int _characterCount;

    public BoundedDiagnosticBuffer(int maxCharacters = 8 * 1024)
    {
        if (maxCharacters < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        _maxCharacters = maxCharacters;
    }

    public void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var sanitized = SanitizeForLog(line.Trim());
        if (sanitized.Length > _maxCharacters)
        {
            sanitized = sanitized[^_maxCharacters..];
        }

        lock (_gate)
        {
            var addedCharacters = sanitized.Length + (_lines.Count == 0 ? 0 : 1);
            while (_lines.Count > 0 && _characterCount + addedCharacters > _maxCharacters)
            {
                var removed = _lines.Dequeue();
                _characterCount -= removed.Length;
                if (_lines.Count > 0)
                {
                    _characterCount--;
                }
                addedCharacters = sanitized.Length + (_lines.Count == 0 ? 0 : 1);
            }

            if (_lines.Count > 0)
            {
                _characterCount++;
            }
            _lines.Enqueue(sanitized);
            _characterCount += sanitized.Length;
        }
    }

    public string Snapshot()
    {
        lock (_gate)
        {
            return string.Join('\n', _lines);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _characterCount = 0;
        }
    }

    public static string SanitizeForLog(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }
}
