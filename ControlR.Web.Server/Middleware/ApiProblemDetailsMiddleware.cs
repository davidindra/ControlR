using Microsoft.AspNetCore.WebUtilities;

namespace ControlR.Web.Server.Middleware;

/// <summary>
/// Gives an /api response that would otherwise carry no body a ProblemDetails document. The pipeline
/// produces these for errors that never reach a controller, such as an unmatched route or a request
/// that arrives without credentials.
/// </summary>
public class ApiProblemDetailsMiddleware(RequestDelegate next)
{
  private readonly RequestDelegate _next = next;

  public async Task Invoke(HttpContext context, IProblemDetailsService problemDetailsService)
  {
    await _next(context);

    if (!ShouldWriteProblemDetails(context))
    {
      return;
    }

    var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
    {
      HttpContext = context
    });

    // No registered writer accepts the request's Accept header, so send something readable rather
    // than an empty body.
    if (!written)
    {
      await WritePlainTextFallback(context.Response);
    }
  }

  private static bool ShouldWriteProblemDetails(HttpContext context)
  {
    var response = context.Response;
    return context.Request.Path.StartsWithSegments("/api") &&
      !response.HasStarted &&
      response.StatusCode is >= 400 and < 600 &&
      !response.ContentLength.HasValue &&
      string.IsNullOrEmpty(response.ContentType);
  }

  private static async Task WritePlainTextFallback(HttpResponse response)
  {
    var reasonPhrase = ReasonPhrases.GetReasonPhrase(response.StatusCode);
    response.ContentType = "text/plain";
    await response.WriteAsync(
      string.IsNullOrWhiteSpace(reasonPhrase)
        ? $"Status Code: {response.StatusCode}"
        : $"Status Code: {response.StatusCode}; {reasonPhrase}");
  }
}
