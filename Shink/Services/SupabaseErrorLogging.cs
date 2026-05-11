using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed record AppErrorLogEntry(
    DateTimeOffset OccurredAt,
    string Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionText,
    string? RequestMethod,
    string? RequestPath,
    string? TraceIdentifier,
    string? UserEmail,
    string EnvironmentName,
    string MachineName);

public sealed class AppErrorLogQueue
{
    private readonly Channel<AppErrorLogEntry> _channel = Channel.CreateBounded<AppErrorLogEntry>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelReader<AppErrorLogEntry> Reader => _channel.Reader;

    public void Enqueue(AppErrorLogEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }
}

public sealed class SupabaseErrorLoggingProvider(
    AppErrorLogQueue queue,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : ILoggerProvider
{
    private readonly AppErrorLogQueue _queue = queue;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IHostEnvironment _environment = environment;

    public ILogger CreateLogger(string categoryName) =>
        new SupabaseErrorLogger(categoryName, _queue, _httpContextAccessor, _environment);

    public void Dispose()
    {
    }

    private sealed class SupabaseErrorLogger(
        string categoryName,
        AppErrorLogQueue queue,
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment environment) : ILogger
    {
        private const int MaxMessageLength = 4000;
        private const int MaxExceptionLength = 12000;
        private const int MaxPathLength = 2048;
        private const int MaxEmailLength = 320;

        private readonly string _categoryName = categoryName;
        private readonly AppErrorLogQueue _queue = queue;
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
        private readonly IHostEnvironment _environment = environment;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = Truncate(formatter(state, exception), MaxMessageLength);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            var request = httpContext?.Request;
            var userEmail = httpContext?.User.FindFirst(ClaimTypes.Email)?.Value
                ?? httpContext?.User.Identity?.Name;

            _queue.Enqueue(new AppErrorLogEntry(
                OccurredAt: DateTimeOffset.UtcNow,
                Level: logLevel.ToString(),
                Category: _categoryName,
                EventId: eventId.Id,
                EventName: Truncate(eventId.Name, MaxMessageLength),
                Message: string.IsNullOrWhiteSpace(message) ? exception?.Message ?? "Application error" : message,
                ExceptionText: Truncate(exception?.ToString(), MaxExceptionLength),
                RequestMethod: request?.Method,
                RequestPath: Truncate(request?.Path.Value, MaxPathLength),
                TraceIdentifier: httpContext?.TraceIdentifier,
                UserEmail: Truncate(userEmail, MaxEmailLength),
                EnvironmentName: _environment.EnvironmentName,
                MachineName: Environment.MachineName));
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

public sealed class SupabaseErrorLogWorker(
    AppErrorLogQueue queue,
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseOptions> supabaseOptions) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppErrorLogQueue _queue = queue;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly SupabaseOptions _options = supabaseOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PersistAsync(entry, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Logging must never become part of the user-facing failure path.
            }
        }
    }

    private async Task PersistAsync(AppErrorLogEntry entry, CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri) ||
            string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return;
        }

        var payload = new[]
        {
            new
            {
                occurred_at = entry.OccurredAt,
                level = entry.Level,
                category = entry.Category,
                event_id = entry.EventId == 0 ? (int?)null : entry.EventId,
                event_name = entry.EventName,
                message = entry.Message,
                exception_text = entry.ExceptionText,
                request_method = entry.RequestMethod,
                request_path = entry.RequestPath,
                trace_identifier = entry.TraceIdentifier,
                user_email = entry.UserEmail,
                environment_name = entry.EnvironmentName,
                machine_name = entry.MachineName,
                metadata = new { source = "aspnet-logger" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "rest/v1/app_error_logs"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", _options.SecretKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        using var response = await _httpClientFactory
            .CreateClient("supabase-error-logs")
            .SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }
}
