using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Tests.Helpers;

/// <summary>
/// Assertions for V1 error bodies. Every V1 failure answers with an RFC 9457 ProblemDetails
/// payload, so these assert the shipped contract (status code plus a ProblemDetails value whose
/// own Status matches) instead of the <c>BadRequestObjectResult</c> / <c>NotFoundObjectResult</c>
/// shortcut classes that used to carry bare strings.
/// </summary>
internal static class ProblemDetailsAsserts
{
  internal static ProblemDetails AssertProblem(
    object? result,
    int statusCode,
    string? expectedTitle = null,
    string? expectedDetail = null)
  {
    var objectResult = Assert.IsType<ObjectResult>(result);
    Assert.Equal(statusCode, objectResult.StatusCode);

    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(statusCode, problem.Status);

    if (expectedTitle is not null)
    {
      Assert.Equal(expectedTitle, problem.Title);
    }

    if (expectedDetail is not null)
    {
      Assert.Equal(expectedDetail, problem.Detail);
    }

    return problem;
  }
}
