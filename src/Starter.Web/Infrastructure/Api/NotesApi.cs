using Starter.Contracts;
using Starter.Contracts.Account;
using Starter.Contracts.Dashboard;
using Starter.Contracts.Notes;

namespace Starter.Web.Infrastructure.Api;

/// <summary>
/// Typed methods for the API endpoints used by the UI. To call a new endpoint add a method here (or a new
/// class like this one per feature) - authentication and error handling are already done by <see cref="ApiClient"/>.
/// </summary>
public sealed class NotesApi(ApiClient api)
{
    public Task<ApiResult<MeResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<MeResponse>("api/me", cancellationToken);

    public Task<ApiResult<DashboardResponse>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<DashboardResponse>("api/dashboard", cancellationToken);

    public Task<ApiResult<PagedResponse<NoteSummary>>> GetNotesAsync(bool archived, int page, CancellationToken cancellationToken = default) =>
        api.GetAsync<PagedResponse<NoteSummary>>($"api/notes?archived={archived}&page={page}&pageSize={NoteLimits.DefaultPageSize}", cancellationToken);

    public Task<ApiResult<NoteDetails>> GetNoteAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.GetAsync<NoteDetails>($"api/notes/{id}", cancellationToken);

    public Task<ApiResult<NoteDetails>> CreateNoteAsync(CreateNoteRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<NoteDetails>("api/notes", request, cancellationToken);

    public Task<ApiResult<NoteDetails>> UpdateNoteAsync(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken = default) =>
        api.PutAsync<NoteDetails>($"api/notes/{id}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteNoteAsync(Guid id, CancellationToken cancellationToken = default) =>
        api.DeleteAsync($"api/notes/{id}", cancellationToken);
}
