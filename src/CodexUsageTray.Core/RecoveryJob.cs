using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageTray.Core;

public sealed record RecoveryJob(
    string JobId,
    string Model,
    string Prompt,
    int MaxAttempts,
    int TimeoutSeconds)
{
    private const int MaxIdentityLength = 128;
    private const int MaxPromptLength = 200_000;

    public static RecoveryJob Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            var job = JsonSerializer.Deserialize<RecoveryJob>(json, JsonOptions())
                ?? throw new InvalidDataException("Recovery job JSON is empty.");
            job.Validate();
            return job;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Recovery job JSON is invalid.", exception);
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(JobId) || JobId.Length > MaxIdentityLength)
        {
            throw new InvalidDataException("jobId must be non-empty and at most 128 characters.");
        }
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > MaxIdentityLength)
        {
            throw new InvalidDataException("model must be non-empty and at most 128 characters.");
        }
        if (string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > MaxPromptLength)
        {
            throw new InvalidDataException("prompt must be non-empty and bounded.");
        }
        if (MaxAttempts is < 1 or > 5)
        {
            throw new InvalidDataException("maxAttempts must be between 1 and 5.");
        }
        if (TimeoutSeconds is < 10 or > 3600)
        {
            throw new InvalidDataException("timeoutSeconds must be between 10 and 3600.");
        }
    }

    internal static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
}

public enum RecoveryExecutionStatus
{
    Pending,
    Running,
    Completed,
    FailedTransient,
    ReconcileRequired,
    FailedTerminal
}

public sealed record RecoveryExecutionState(
    RecoveryJob Job,
    RecoveryExecutionStatus Status,
    int Attempt,
    string? ResponseId,
    string OutputText,
    string? LastError,
    string? ClientRequestId,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Job);
        Job.Validate();
        if (Attempt < 0 || Attempt > Job.MaxAttempts)
        {
            throw new InvalidDataException("Recovery state attempt is outside the job attempt ceiling.");
        }
        if (OutputText.Length > 2_000_000)
        {
            throw new InvalidDataException("Recovery output is too large for the local checkpoint.");
        }
        if (!string.IsNullOrWhiteSpace(ClientRequestId) &&
            !Guid.TryParse(ClientRequestId, out _))
        {
            throw new InvalidDataException("clientRequestId must be a GUID when present.");
        }
    }
}
