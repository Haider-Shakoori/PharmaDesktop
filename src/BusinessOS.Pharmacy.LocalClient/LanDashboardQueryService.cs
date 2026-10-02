using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessOS.Pharmacy.Application.Abstractions.Dashboard;

namespace BusinessOS.Pharmacy.LocalClient;

public sealed class LanDashboardQueryService(
    LanApiRequestFactory requests) : ILocalDashboardQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DashboardSnapshot> GetSnapshotAsync(
        DashboardQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var date = options.BusinessDate.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        var uri =
            $"dashboard?businessDate={Uri.EscapeDataString(date)}" +
            $"&lowStockThreshold={options.LowStockThreshold}" +
            $"&nearExpiryDays={options.NearExpiryDays}" +
            $"&period={Uri.EscapeDataString(options.Period)}";

        var (client, request) = await requests.CreateAsync(
            HttpMethod.Get,
            uri,
            cancellationToken);

        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Dashboard request failed with HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<DashboardSnapshot>(
                       JsonOptions,
                       cancellationToken)
                   ?? throw new InvalidOperationException(
                       "The Main Pharmacy Server returned an empty dashboard response.");
        }
    }
}
