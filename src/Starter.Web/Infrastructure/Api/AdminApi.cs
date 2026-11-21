using Starter.Contracts.Admin;

namespace Starter.Web.Infrastructure.Api;

/// <summary>Administrator endpoints of the API (role Admin is checked by the API, not only by the page).</summary>
public sealed class AdminApi(ApiClient api)
{
    public Task<ApiResult<AdminStatsResponse>> GetStatsAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminStatsResponse>("api/admin/stats", cancellationToken);
}
