using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Starter.Observability;

namespace Starter.Web.Services;

/// <summary>
/// UI events of one browser tab (category Frontend: off by default, switch on while investigating).
/// Use it for UI actions worth seeing in the logs: <c>await Frontend.LogAsync("ui.note_saved")</c>.
/// API calls are already logged by ApiClient, so do not log them again here.
/// </summary>
public sealed class FrontendActivity(AuthenticationStateProvider authenticationStateProvider, ILogger<FrontendActivity> logger)
{
    public async Task LogAsync(string action, IReadOnlyDictionary<string, object?>? details = null, EventOutcome outcome = EventOutcome.Success)
    {
        string? userId = null;
        try
        {
            userId = (await authenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirst("sub")?.Value;
        }
        catch (InvalidOperationException)
        {
            // Authentication state is not available yet (very early in the circuit); log without a user.
        }

        logger.LogActivity(LogCategories.Frontend, action, outcome, details, userId);
    }
}

/// <summary>Logs when a tab connects, loses and regains its connection, and closes (category Frontend).</summary>
internal sealed class CircuitActivityHandler(FrontendActivity frontend) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        frontend.LogAsync("ui.circuit_opened", CircuitDetails(circuit));

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) =>
        frontend.LogAsync("ui.connection_lost", CircuitDetails(circuit), EventOutcome.Failure);

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) =>
        frontend.LogAsync("ui.connection_restored", CircuitDetails(circuit));

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        frontend.LogAsync("ui.circuit_closed", CircuitDetails(circuit));

    private static Dictionary<string, object?> CircuitDetails(Circuit circuit) =>
        new() { ["CircuitId"] = ActivityLogExtensions.HashForLogs(circuit.Id) };
}
