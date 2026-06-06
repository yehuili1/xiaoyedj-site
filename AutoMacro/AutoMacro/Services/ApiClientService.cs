using System.Net.Http;
using System.Text;

namespace AutoMacro.Services;

public class ApiClientService : IApiClientService, IDisposable
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly IRunLogger _logger;

    public ApiClientService(IRunLogger logger)
    {
        _logger = logger;
    }

    public async Task<ApiRequestResult> SendAsync(
        string method,
        string url,
        string? body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Failed("网址不能为空。");

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return Failed("网址格式不正确。");

        ApiRequestResult? lastResult = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastResult = await SendOnceAsync(method, uri, body, cancellationToken);

            if (lastResult.Success || !ShouldRetry(lastResult.StatusCode) || attempt == MaxAttempts)
                return lastResult;

            _logger.Warn("API", $"请求失败，准备重试 {attempt + 1}/{MaxAttempts}: {uri}, status={lastResult.StatusCode}");
            await Task.Delay(500 * attempt, cancellationToken);
        }

        return lastResult ?? Failed("请求失败。");
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ApiRequestResult> SendOnceAsync(
        string method,
        Uri uri,
        string? body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(NormalizeMethod(method)), uri);
            if (request.Method != HttpMethod.Get && !string.IsNullOrEmpty(body))
                request.Content = new StringContent(body, Encoding.UTF8, GuessContentType(body));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.Info("API", $"{request.Method} {uri} -> {(int)response.StatusCode}, length={responseBody.Length}");

            return new ApiRequestResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                responseBody,
                response.IsSuccessStatusCode ? string.Empty : response.ReasonPhrase ?? "请求失败");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("API", $"请求失败: {method} {uri}", ex);
            return Failed(ex.Message);
        }
    }

    private static string NormalizeMethod(string method)
    {
        var normalized = method.Trim().ToUpperInvariant();
        return normalized is "POST" or "PUT" or "PATCH" or "DELETE" ? normalized : "GET";
    }

    private static string GuessContentType(string body)
    {
        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[')
            ? "application/json"
            : "text/plain";
    }

    private static ApiRequestResult Failed(string message) =>
        new(false, 0, string.Empty, message);

    private static bool ShouldRetry(int statusCode) =>
        statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;
}
