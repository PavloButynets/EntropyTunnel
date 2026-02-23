using System.Text;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Stages;

public sealed class LocalForwarder : IPipelineStage
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalForwarder> _logger;

    public LocalForwarder(IHttpClientFactory httpClientFactory, ILogger<LocalForwarder> logger)
    {
        _httpClient = httpClientFactory.CreateClient("tunnel");
        _logger = logger;
    }

    public async Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(new HttpMethod(context.Method), context.TargetUrl);

            // Request body (attach first so content headers have a target)
            if (context.RequestBody is not null && context.RequestBody.Length > 0)
                request.Content = new StreamContent(context.RequestBody);

            // Request headers - no per-name filter lists
            // request.Headers.TryAddWithoutValidation returns false for content-specific
            // headers (Content-Type, Content-Encoding, Content-Length…); the fallback
            // then sets them on request.Content.Headers where they belong.
            // Truly restricted headers that HttpClient never forwards (Connection, TE…)
            // are silently dropped by the framework — no explicit list needed.
            if (context.RequestHeaders is not null)
            {
                foreach (var (key, value) in context.RequestHeaders)
                {
                    if (!request.Headers.TryAddWithoutValidation(key, value)
                        && request.Content is not null)
                    {
                        request.Content.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            context.StatusCode = (int)response.StatusCode;
            context.ContentType = response.Content.Headers.ContentType?.ToString()
                                   ?? "application/octet-stream";
            context.ResponseStream = await response.Content.ReadAsStreamAsync(ct);

            // Response headers
            // Merge both header collections in one pass.
            // Content-Type is the only exclusion: it is carried in context.ContentType
            // so that mock/chaos responses also receive a valid Content-Type without
            // going through this path.
            context.ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;
                context.ResponseHeaders[header.Key] = header.Value.ToArray();
            }

            _logger.LogInformation("[FWD] {Method} {Url} → {Status}",
                context.Method, context.TargetUrl, context.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("[FWD] Cannot reach {Url}: {Msg}", context.TargetUrl, ex.Message);
            context.StatusCode = 502;
            context.ContentType = "text/plain";
            context.ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes($"Bad Gateway: {ex.Message}"));
            context.ResponseHeaders = new Dictionary<string, string[]>();
        }
    }
}
