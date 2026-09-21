using Microsoft.AspNetCore.Components;

namespace ControlR.Web.Server.Services;

/// <summary>
/// Builds absolute URLs for links that leave the application, such as the callback links embedded in
/// account emails.
/// </summary>
/// <remarks>
/// Emailed links must never be derived from the incoming request. The <c>Host</c> header is
/// attacker-controlled unless <c>AllowedHosts</c> is pinned or a reverse proxy overwrites it, and a
/// forged host turns a password-reset mail into a genuine token delivered to the attacker's origin.
/// Callers get the configured <see cref="AppOptions.PublicBaseUrl"/> when one is set, and only fall
/// back to the request when it is not.
/// </remarks>
public interface IPublicUrlProvider
{
  /// <summary>
  /// Whether an explicit public base URL is configured. When false, generated URLs fall back to the
  /// host of the incoming request.
  /// </summary>
  bool HasConfiguredBaseUrl { get; }

  /// <summary>
  /// Builds an absolute URL for a path relative to the application root.
  /// </summary>
  /// <param name="relativePath">The path relative to the application root, e.g. "Account/ResetPassword".</param>
  string GetAbsoluteUri(string relativePath);

  /// <summary>
  /// Builds an absolute URL for a path relative to the application root, with the supplied query
  /// parameters appended.
  /// </summary>
  /// <param name="relativePath">The path relative to the application root, e.g. "Account/ConfirmEmail".</param>
  /// <param name="queryParameters">The query parameters to append.</param>
  string GetAbsoluteUri(string relativePath, IReadOnlyDictionary<string, object?> queryParameters);
}

public class PublicUrlProvider(
  NavigationManager navigationManager,
  IOptionsMonitor<AppOptions> appOptions) : IPublicUrlProvider
{
  private readonly IOptionsMonitor<AppOptions> _appOptions = appOptions;
  private readonly NavigationManager _navigationManager = navigationManager;

  public bool HasConfiguredBaseUrl => !string.IsNullOrWhiteSpace(_appOptions.CurrentValue.PublicBaseUrl);

  public string GetAbsoluteUri(string relativePath)
  {
    var configuredBaseUrl = _appOptions.CurrentValue.PublicBaseUrl;
    if (string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
      return _navigationManager.ToAbsoluteUri(relativePath.TrimStart('/')).AbsoluteUri;
    }

    return $"{configuredBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
  }

  public string GetAbsoluteUri(string relativePath, IReadOnlyDictionary<string, object?> queryParameters)
  {
    return _navigationManager.GetUriWithQueryParameters(GetAbsoluteUri(relativePath), queryParameters);
  }
}
