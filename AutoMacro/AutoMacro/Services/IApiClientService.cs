namespace AutoMacro.Services;

public sealed record ApiRequestResult(
    bool Success,
    int StatusCode,
    string Body,
    string ErrorMessage);

public interface IApiClientService
{
    Task<ApiRequestResult> SendAsync(
        string method,
        string url,
        string? body,
        CancellationToken cancellationToken);
}
