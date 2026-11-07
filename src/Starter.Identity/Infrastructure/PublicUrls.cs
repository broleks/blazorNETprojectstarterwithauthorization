namespace Starter.Identity.Infrastructure;

/// <summary>
/// Builds absolute links (for emails) and validates return URLs.
///
/// Email links are built from the configured public URL (<c>Authentication:Issuer</c>), not from the request's
/// Host header. Otherwise an attacker could send a password reset request with a forged Host header and receive
/// a reset link pointing to their own server ("password reset poisoning").
///
/// Return URLs are accepted only when they are local or point to an origin of a registered client
/// (taken from the client redirect URIs in configuration). Anything else is replaced with a safe default.
/// </summary>
public sealed class PublicUrls
{
    private readonly Uri? publicBaseUri;
    private readonly HashSet<string> allowedOrigins;

    public PublicUrls(IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"];
        publicBaseUri = string.IsNullOrWhiteSpace(issuer) ? null : new Uri(issuer, UriKind.Absolute);

        var settings = configuration.GetSection(OpenIddictSettings.SectionName).Get<OpenIddictSettings>() ?? new OpenIddictSettings();
        allowedOrigins = settings.Clients.Values
            .SelectMany(c => c.RedirectUris.Concat(c.PostLogoutRedirectUris))
            .Select(uri => new Uri(uri, UriKind.Absolute).GetLeftPart(UriPartial.Authority))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AppUrl = configuration["Authentication:DefaultReturnUrl"] is { Length: > 0 } configured && IsAllowed(configured)
            ? configured
            : null;
        DefaultReturnUrl = AppUrl ?? "/Account/Manage";
    }

    /// <summary>Home page of the main client application (Starter.Web), if configured.</summary>
    public string? AppUrl { get; }

    /// <summary>Where users go after signing in directly on the identity server (usually the web app).</summary>
    public string DefaultReturnUrl { get; }

    /// <summary>Origins of registered clients, used for return URL checks and the CSP form-action directive.</summary>
    public IReadOnlyCollection<string> ClientOrigins => allowedOrigins;

    public string Absolute(HttpRequest request, string pathAndQuery)
    {
        var baseUri = publicBaseUri ?? new Uri($"{request.Scheme}://{request.Host}{request.PathBase}/");
        return new Uri(baseUri, pathAndQuery.TrimStart('/')).ToString();
    }

    public bool IsAllowed(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // Local paths only: "/x" is fine, "//evil.example" and "/\evil.example" are protocol-relative tricks.
        if (url[0] == '/')
        {
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && allowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));
    }

    public string Normalize(string? url) => IsAllowed(url) ? url! : DefaultReturnUrl;
}
