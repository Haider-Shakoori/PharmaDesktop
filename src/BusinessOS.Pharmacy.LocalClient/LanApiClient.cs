using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanApiClient(LanApiRequestFactory requests)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Get, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, ct))
        {
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new InvalidOperationException("The pharmacy server returned an empty response.");
        }
    }

    public async Task<T?> GetOptionalAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Get, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, ct))
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
    }

    public async Task PostAsync(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, ct))
            await EnsureSuccessAsync(response, ct);
    }

    public async Task PostAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using (request)
        using (var response = await client.SendAsync(request, ct))
            await EnsureSuccessAsync(response, ct);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using (request)
        using (var response = await client.SendAsync(request, ct))
        {
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException("The pharmacy server returned an empty response.");
        }
    }

    public async Task<TResponse> PostAsync<TResponse>(string path, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Post, path, ct);
        using (request)
        using (var response = await client.SendAsync(request, ct))
        {
            await EnsureSuccessAsync(response, ct);
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException("The pharmacy server returned an empty response.");
        }
    }

    public async Task PutAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        var (client, request) = await requests.CreateAsync(HttpMethod.Put, path, ct);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using (request)
        using (var response = await client.SendAsync(request, ct))
            await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct);
            message = error?.Message;
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException(message ?? $"Pharmacy server request failed with HTTP {(int)response.StatusCode}.");
    }

    private sealed record ApiError(string? Message);
}
