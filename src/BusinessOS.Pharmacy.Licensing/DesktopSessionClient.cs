using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class DesktopSessionClient : IDesktopSessionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LicenseApiOptions _options;

    public DesktopSessionClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LicenseApiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public Task<DesktopSessionEnvelope> LoginAsync(
        string leaseToken,
        DesktopSessionLoginRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(_options.SessionLoginPath, leaseToken, request, cancellationToken);

    public Task<DesktopSessionEnvelope> RefreshAsync(
        string accessToken,
        DesktopSessionRefreshRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(_options.SessionRefreshPath, accessToken, request, cancellationToken);

    private async Task<DesktopSessionEnvelope> SendAsync(
        string path,
        string bearerToken,
        object payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new ArgumentException("A signed desktop token is required.", nameof(bearerToken));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        try
        {
            var client = _httpClientFactory.CreateClient(DependencyInjection.HttpClientName);
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseText);
            }

            try
            {
                return JsonSerializer.Deserialize<DesktopSessionEnvelope>(responseText, JsonOptions)
                    ?? throw new LicenseApiException(
                        "The BusinessOS authentication server returned an empty response.",
                        isRetryable: true,
                        statusCode: response.StatusCode);
            }
            catch (JsonException exception)
            {
                throw new LicenseApiException(
                    "The BusinessOS authentication server returned an invalid response.",
                    isRetryable: true,
                    statusCode: response.StatusCode,
                    innerException: exception);
            }
        }
        catch (LicenseApiException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LicenseApiException(
                "The BusinessOS authentication server did not respond in time.",
                isRetryable: true,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LicenseApiException(
                "The BusinessOS authentication server could not be reached.",
                isRetryable: true,
                statusCode: exception.StatusCode,
                innerException: exception);
        }
    }

    private static LicenseApiException CreateApiException(HttpStatusCode statusCode, string responseText)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string? message = null;

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            if (root.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    errors[property.Name] = property.Value
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToArray();
                }
            }
        }
        catch (JsonException)
        {
        }

        message ??= errors.Values.SelectMany(value => value).FirstOrDefault();
        message ??= $"Authentication request failed with status {(int)statusCode}.";

        var retryable =
            statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500;

        return new LicenseApiException(message, retryable, statusCode, errors);
    }
}
