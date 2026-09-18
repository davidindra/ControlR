using ControlR.Web.Server.Constants;
using ControlR.Web.Server.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Extensions;

/// <summary>
/// Extension methods for converting <see cref="HttpResult"/> to ActionResult.
/// </summary>
public static class HttpResultExtensions
{
  /// <summary>
  /// Converts an HttpResult to an ActionResult based on the error code.
  /// </summary>
  public static ActionResult ToActionResult(this HttpResult result)
  {
    if (result.IsSuccess)
    {
      return new NoContentResult();
    }

    return CreateProblemResult(result);
  }

  /// <summary>
  /// Converts a successful typed HTTP result to an action result while preserving its value.
  /// </summary>
  public static ActionResult<T> ToActionResult<T>(this HttpResult<T> result)
  {
    if (result.IsSuccess)
    {
      return new OkObjectResult(result.Value);
    }

    return CreateProblemResult(result.ToHttpResult());
  }

  /// <summary>
  /// Converts a successful typed HTTP result to an action result after mapping its value.
  /// </summary>
  public static ActionResult<TDto> ToActionResult<T, TDto>(this HttpResult<T> result, Func<T, TDto> mapper)
  {
    if (result.IsSuccess)
    {
      return new OkObjectResult(mapper(result.Value));
    }

    return CreateProblemResult(result.ToHttpResult());
  }

  private static ObjectResult CreateProblemResult(HttpResult result)
  {
    var statusCode = result.ErrorCode switch
    {
      HttpResultErrorCode.BadRequest => StatusCodes.Status400BadRequest,
      HttpResultErrorCode.Conflict => StatusCodes.Status409Conflict,
      HttpResultErrorCode.Forbidden => StatusCodes.Status403Forbidden,
      HttpResultErrorCode.NotFound => StatusCodes.Status404NotFound,
      HttpResultErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
      HttpResultErrorCode.ValidationFailed => StatusCodes.Status400BadRequest,
      HttpResultErrorCode.NotImplemented => StatusCodes.Status501NotImplemented,
      HttpResultErrorCode.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
      _ => StatusCodes.Status500InternalServerError
    };

    // A validation failure carries its own title. The manager that reported it knows what went wrong,
    // and a caller should not have to read the detail to tell a rejected value from a malformed request.
    var title = result.ErrorCode == HttpResultErrorCode.ValidationFailed
      ? V1ProblemTitles.ValidationFailed
      : V1ProblemTitles.ForStatusCode(statusCode);

    var problem = new ProblemDetails
    {
      Status = statusCode,
      Title = title,
      Detail = result.Reason,
      Type = V1ProblemTitles.ProblemType,
    };

    if (result.Extensions is { Count: > 0 })
    {
      foreach (var kvp in result.Extensions)
      {
        problem.Extensions[kvp.Key] = kvp.Value;
      }
    }

    return new ObjectResult(problem)
    {
      StatusCode = statusCode,
    };
  }
}
