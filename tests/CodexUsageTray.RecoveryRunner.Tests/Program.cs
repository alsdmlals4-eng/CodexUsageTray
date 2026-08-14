using System.Net;
using System.Text;
using CodexUsageTray.Core;
using CodexUsageTray.RecoveryRunner;

namespace CodexUsageTray.RecoveryRunner.Tests;

internal static class Program
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 15, 4, 0, 0, TimeSpan.FromHours(9));

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("completed checkpoint makes zero API requests", TestCompletedShortCircuit),
            ("explicit 429 responses retry within the bounded ceiling", TestRetryableHttp),
            ("terminal 400 response does not retry", TestTerminalHttp),
            ("ambiguous network failure requires reconciliation without retry", TestAmbiguousNetworkFailure),
            ("responses output text and id are persisted", TestSuccessfulResponseParsing),
            ("missing API key fails before transport", TestMissingApiKey)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task TestCompletedShortCircuit()
    {
        var handler = new SequenceHandler();
        var engine = CreateEngine(handler);
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Completed, attempt: 1, responseId: "resp_done");

        var result = await engine.ExecuteAsync(fixture.Path, "test-key", CancellationToken.None);

        Equal(RecoveryExecutionStatus.Completed, result.Status, "completed status");
        Equal(0, handler.RequestCount, "completed checkpoints must never issue another request");
    }

    private static async Task TestRetryableHttp()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"slow down\"}}"),
            Response(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"slow down again\"}}"),
            Response(HttpStatusCode.OK, SuccessJson("resp_ok", "finished")));
        var delays = new List<TimeSpan>();
        var engine = CreateEngine(handler, delay: (value, _) =>
        {
            delays.Add(value);
            return Task.CompletedTask;
        });
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Pending);

        var result = await engine.ExecuteAsync(fixture.Path, "test-key", CancellationToken.None);

        Equal(RecoveryExecutionStatus.Completed, result.Status, "429 sequence eventually completes");
        Equal(3, result.Attempt, "three attempts persisted");
        Equal(3, handler.RequestCount, "three HTTP requests");
        SequenceEqual(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10) }, delays, "retry delays");
    }

    private static async Task TestTerminalHttp()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad input\"}}"));
        var engine = CreateEngine(handler);
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Pending);

        var result = await engine.ExecuteAsync(fixture.Path, "test-key", CancellationToken.None);

        Equal(RecoveryExecutionStatus.FailedTerminal, result.Status, "400 is terminal");
        Equal(1, handler.RequestCount, "terminal HTTP failure is not retried");
        Contains(result.LastError ?? string.Empty, "400", "terminal error records status");
    }

    private static async Task TestAmbiguousNetworkFailure()
    {
        var handler = new SequenceHandler(new HttpRequestException("connection reset after send"));
        var engine = CreateEngine(handler);
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Pending);

        var result = await engine.ExecuteAsync(fixture.Path, "test-key", CancellationToken.None);

        Equal(RecoveryExecutionStatus.ReconcileRequired, result.Status,
            "ambiguous network failure must not blind-retry the POST");
        Equal(1, handler.RequestCount, "ambiguous failure makes one request only");
        True(!string.IsNullOrWhiteSpace(result.ClientRequestId), "request identity is persisted");
    }

    private static async Task TestSuccessfulResponseParsing()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.OK, SuccessJson("resp_123", "hello recovery")));
        var engine = CreateEngine(handler);
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Pending);

        var result = await engine.ExecuteAsync(fixture.Path, "test-key", CancellationToken.None);

        Equal(RecoveryExecutionStatus.Completed, result.Status, "success state");
        Equal("resp_123", result.ResponseId, "response id");
        Equal("hello recovery", result.OutputText, "output text");
        Equal(1, handler.RequestCount, "single success request");
        var request = handler.Requests.Single();
        Equal("https://api.openai.com/v1/responses", request.Uri, "Responses endpoint");
        Equal("Bearer test-key", request.Authorization, "bearer credential");
        Contains(request.Body, "\"model\":\"gpt-5\"", "request model");
        Contains(request.Body, "\"input\":\"continue\"", "request input");
        Contains(request.Body, "\"store\":false", "runner must not rely on server-side response storage");
    }

    private static async Task TestMissingApiKey()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.OK, SuccessJson("unused", "unused")));
        var engine = CreateEngine(handler);
        using var fixture = StateFixture.Create(RecoveryExecutionStatus.Pending);

        var result = await engine.ExecuteAsync(fixture.Path, "", CancellationToken.None);

        Equal(RecoveryExecutionStatus.FailedTerminal, result.Status, "missing key terminal status");
        Equal(0, handler.RequestCount, "missing key must fail before transport");
        Contains(result.LastError ?? string.Empty, "OPENAI_API_KEY", "credential error explains required variable");
    }

    private static RecoveryRunnerEngine CreateEngine(
        SequenceHandler handler,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = new ResponsesRecoveryClient(http);
        return new RecoveryRunnerEngine(
            client,
            delay ?? ((_, _) => Task.CompletedTask),
            () => ObservedAt);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string SuccessJson(string id, string text) => $$"""
    {
      "id": "{{id}}",
      "status": "completed",
      "output": [
        {
          "type": "message",
          "content": [
            { "type": "output_text", "text": "{{text}}" }
          ]
        }
      ]
    }
    """;

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void Contains(string value, string fragment, string message)
    {
        if (!value.Contains(fragment, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message}: '{fragment}' not found in '{value}'");
        }
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{message}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses;

        public SequenceHandler(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public int RequestCount { get; private set; }
        public List<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Requests.Add(new RequestSnapshot(
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected HTTP request.");
            }

            var next = _responses.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            return (HttpResponseMessage)next;
        }
    }

    private sealed record RequestSnapshot(string Uri, string Authorization, string Body);

    private sealed class StateFixture : IDisposable
    {
        private readonly string _directory;

        private StateFixture(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static StateFixture Create(
            RecoveryExecutionStatus status,
            int attempt = 0,
            string? responseId = null)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodexUsageTray-Runner-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "state.json");
            var state = new RecoveryExecutionState(
                new RecoveryJob("job-1", "gpt-5", "continue", 3, 60),
                status,
                attempt,
                responseId,
                responseId is null ? string.Empty : "already done",
                null,
                null,
                ObservedAt);
            RecoveryStateStore.SaveAtomic(path, state);
            return new StateFixture(directory, path);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
