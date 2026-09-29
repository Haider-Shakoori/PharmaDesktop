using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace BusinessOS.Pharmacy.UnitTests.Licensing;

public sealed class LicenseActivationClientTests
{
    [Fact]
    public async Task ActivateAsync_SendsWindowsMetadata_AndParsesEnvelope()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SuccessJson());
        var client = CreateClient(handler);

        var result = await client.ActivateAsync(new LicenseActivationRequest(
            "PHM-SECRET",
            "11111111-1111-4111-8111-111111111111",
            "Counter PC",
            "1.0.0",
            "Dell OptiPlex",
            "Windows 11",
            "26100"));

        Assert.Equal("/api/v1/license/activate", handler.RequestUri?.AbsolutePath);
        Assert.Null(handler.Authorization);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("windows", body.RootElement.GetProperty("platform").GetString());
        Assert.Equal("Windows 11", body.RootElement.GetProperty("os_version").GetString());
        Assert.Equal("26100", body.RootElement.GetProperty("build_number").GetString());
        Assert.Equal("activation-1", result.Data.ActivationId);
        Assert.Equal("tenant-1", result.Data.Tenant.Id);
    }

    [Fact]
    public async Task RefreshAsync_UsesSignedLeaseAsBearer_WithoutLicenseKey()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, SuccessJson());
        var client = CreateClient(handler);

        await client.RefreshAsync(
            "v1.payload.signature",
            new LicenseRefreshRequest(
                "11111111-1111-4111-8111-111111111111",
                "Counter PC",
                "1.0.1",
                OsVersion: "Windows 11"));

        Assert.Equal("/api/v1/desktop/license/refresh", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("v1.payload.signature", handler.Authorization?.Parameter);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.TryGetProperty("license_key", out _));
        Assert.Equal(
            "11111111-1111-4111-8111-111111111111",
            body.RootElement.GetProperty("device_id").GetString());
    }

    [Fact]
    public async Task ValidationFailure_IsNotRetryable_AndPreservesFieldErrors()
    {
        const string json = """
            {
              "message":"The given data was invalid.",
              "errors":{"device_id":["The Windows PC limit for this subscription has been reached."]}
            }
            """;
        var client = CreateClient(new CapturingHandler(
            HttpStatusCode.UnprocessableEntity,
            json));

        var exception = await Assert.ThrowsAsync<LicenseApiException>(() =>
            client.ActivateAsync(new LicenseActivationRequest(
                "PHM-SECRET",
                "22222222-2222-4222-8222-222222222222",
                null,
                "1.0.0")));

        Assert.False(exception.IsRetryable);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Contains("device_id", exception.ValidationErrors.Keys);
    }

    [Fact]
    public async Task ServerFailure_IsRetryable()
    {
        var client = CreateClient(new CapturingHandler(
            HttpStatusCode.ServiceUnavailable,
            """{"message":"Service temporarily unavailable."}"""));

        var exception = await Assert.ThrowsAsync<LicenseApiException>(() =>
            client.ActivateAsync(new LicenseActivationRequest(
                "PHM-SECRET",
                "33333333-3333-4333-8333-333333333333",
                null,
                "1.0.0")));

        Assert.True(exception.IsRetryable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    private static LicenseActivationClient CreateClient(CapturingHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pharmacy.businessos.af"),
            Timeout = TimeSpan.FromSeconds(15),
        };

        return new LicenseActivationClient(
            new StubHttpClientFactory(httpClient),
            Options.Create(new LicenseApiOptions()));
    }

    private static string SuccessJson() => """
        {
          "data":{
            "activation_id":"activation-1",
            "lease_token":"v1.payload.signature",
            "lease_expires_at":"2026-10-06T00:00:00+00:00",
            "subscription_health":"healthy",
            "tenant":{
              "id":"tenant-1",
              "name":"Kabul Pharmacy",
              "slug":"kabul",
              "cloud_base_url":"https://kabul.pharmacy.businessos.af",
              "timezone":"Asia/Kabul",
              "currency":"AFN",
              "locale":"en"
            },
            "plan":{
              "code":"STANDARD",
              "max_android_devices":1,
              "max_windows_devices":1,
              "offline_grace_days":7,
              "features":["advanced_reports"]
            }
          },
          "server_time":"2026-09-29T19:00:00+00:00"
        }
        """;

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CapturingHandler(
        HttpStatusCode statusCode,
        string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
