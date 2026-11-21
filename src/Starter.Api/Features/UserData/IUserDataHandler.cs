namespace Starter.Api.Features.UserData;

/// <summary>
/// GDPR support for one feature. Every feature that stores data belonging to a user implements this interface and
/// registers it in Program.cs (<c>AddScoped&lt;IUserDataHandler, ...&gt;</c>). The identity server then includes the
/// data in the user's export and deletes it when the account is deleted.
///
/// A test (UserDataCoverageTests) fails if an entity with an <c>OwnerId</c> is not listed in any handler's
/// <see cref="Entities"/>, so a new feature cannot forget it.
/// </summary>
public interface IUserDataHandler
{
    /// <summary>Section name in the export file, e.g. "notes".</summary>
    string Name { get; }

    /// <summary>The entity types this handler is responsible for.</summary>
    IReadOnlyCollection<Type> Entities { get; }

    /// <summary>Everything the user has stored in this feature, in a form that is useful to them (no internal ids they don't need).</summary>
    Task<object> ExportAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Deletes all of the user's data in this feature. Must be safe to call more than once.</summary>
    Task DeleteAsync(string userId, CancellationToken cancellationToken);
}
