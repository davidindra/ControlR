namespace ControlR.Web.Server.Constants;

/// <summary>
/// The titles used on V1 error bodies. A title names the kind of failure and is the same from request
/// to request, while the detail carries what varies. Browsers and API clients should read the status
/// code rather than the title.
/// </summary>
internal static class V1ProblemTitles
{
  internal const string Conflict = "Conflict.";
  internal const string InvalidRequest = "Invalid request.";
  internal const string NotFound = "Not found.";
}
