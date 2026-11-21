using Starter.Contracts.Account;

namespace Starter.Web.Infrastructure.Api;

/// <summary>Account information from the API. Part of the starter base, independent of the example features.</summary>
public sealed class AccountApi(ApiClient api)
{
    public Task<ApiResult<MeResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<MeResponse>("api/me", cancellationToken);
}
