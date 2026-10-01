using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Medicines;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanMedicineCatalogService(
    LanApiRequestFactory requests) : IMedicineCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MedicineListItem>> SearchAsync(
        MedicineSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query.Add($"search={Uri.EscapeDataString(filter.Search)}");

        if (!string.IsNullOrWhiteSpace(filter.CategoryId))
            query.Add($"categoryId={Uri.EscapeDataString(filter.CategoryId)}");

        if (filter.IsActive is not null)
            query.Add($"status={(filter.IsActive.Value ? "active" : "inactive")}");

        query.Add($"take={Math.Clamp(filter.Take, 1, 1000)}");

        var uri = "medicines?" + string.Join("&", query);
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Get,
            uri,
            cancellationToken);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<List<MedicineListItem>>(
                       JsonOptions,
                       cancellationToken)
                   ?? [];
        }
    }

    public async Task<MedicineEditorModel?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Get,
            $"medicines/{Uri.EscapeDataString(id)}",
            cancellationToken);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<MedicineEditorModel>(
                JsonOptions,
                cancellationToken);
        }
    }

    public async Task<MedicineReferenceData> GetReferenceDataAsync(
        CancellationToken cancellationToken = default)
    {
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Get,
            "medicines/references",
            cancellationToken);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<MedicineReferenceData>(
                       JsonOptions,
                       cancellationToken)
                   ?? new MedicineReferenceData([], []);
        }
    }

    public async Task<string> CreateAsync(
        SaveMedicineRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Post,
            "medicines",
            cancellationToken);

        request.Content = JsonContent.Create(requestModel, options: JsonOptions);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
            var created = await response.Content.ReadFromJsonAsync<CreateResponse>(
                JsonOptions,
                cancellationToken);

            return created?.Id
                ?? throw new InvalidOperationException("The pharmacy server returned an empty medicine ID.");
        }
    }

    public async Task UpdateAsync(
        string id,
        SaveMedicineRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Put,
            $"medicines/{Uri.EscapeDataString(id)}",
            cancellationToken);

        request.Content = JsonContent.Create(requestModel, options: JsonOptions);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public Task<string> CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        CreateReferenceAsync(
            "medicine-categories",
            new { name },
            cancellationToken);

    public Task<string> CreateManufacturerAsync(
        string name,
        string? country,
        CancellationToken cancellationToken = default) =>
        CreateReferenceAsync(
            "manufacturers",
            new { name, country },
            cancellationToken);

    private async Task<string> CreateReferenceAsync(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var (client, request) = await requests.CreateAsync(
            HttpMethod.Post,
            path,
            cancellationToken);
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
            var created = await response.Content.ReadFromJsonAsync<CreateResponse>(
                JsonOptions,
                cancellationToken);

            return created?.Id
                ?? throw new InvalidOperationException("The pharmacy server returned an empty reference ID.");
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(
                JsonOptions,
                cancellationToken);
            message = error?.Message;
        }
        catch (JsonException)
        {
        }

        message ??= $"Pharmacy server request failed with HTTP {(int)response.StatusCode}.";
        throw new InvalidOperationException(message);
    }

    private sealed record CreateResponse(string Id);
    private sealed record ApiError(string? Message);
}
