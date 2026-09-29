using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BusinessOS.Pharmacy.Licensing;

public sealed class LicenseActivationClient : ILicenseActivationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LicenseApiOptions _options;

    public LicenseActivationClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LicenseApiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public Task<LicenseActivationEnvelope> ActivateAsync(
        LicenseActivationRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(_options.ActivationPath, request, null, cancellationToken);

    public Task<LicenseActivationEnvelope> RefreshAsync(
        string leaseToken,
        LicenseRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseToken))
        {
            throw new ArgumentException("A signed desktop lease is required.", nameof(leaseToken));
        }

        return SendAsync(_options.RefreshPath, request, leaseToken, cancellationToken);
    }

    private async Task<LicenseActivationEnvelope> SendAsync(
        string path,
        object payload,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };

        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

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
                var envelope = JsonSerializer.Deserialize<LicenseActivationEnvelope>(
                    responseText,
                    JsonOptions);

                return envelope ?? throw new LicenseApiException(
                    "The BusinessOS licensing server returned an empty response.",
                    isRetryable: true,
                    statusCode: response.StatusCode);
            }
            catch (JsonException exception)
            {
                throw new LicenseApiException(
                    "The BusinessOS licensing server returned an invalid response.",
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
                "The BusinessOS licensing server did not respond in time.",
                isRetryable: true,
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LicenseApiException(
                "The BusinessOS licensing server could not be reached.",
                isRetryable: true,
                statusCode: exception.StatusCode,
                innerException: exception);
        }
    }

    private static LicenseApiException CreateApiException(
        HttpStatusCode statusCode,
        string responseText)
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
            // Preserve the HTTP status even when the server/proxy body is not JSON.
        }

        message ??= errors.Values.SelectMany(value => value).FirstOrDefault();
        message ??= $"Licensing request failed with status {(int)statusCode}.";

        var retryable =
            statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500;

        return new LicenseApiException(
            message,
            retryable,
            statusCode,
            errors);
    }
}
