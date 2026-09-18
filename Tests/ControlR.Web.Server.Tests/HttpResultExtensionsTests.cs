using ControlR.Web.Server.Extensions;
using ControlR.Web.Server.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Tests;

public class HttpResultExtensionsTests
{
  [Fact]
  public void HttpResultT_Extensions_DefaultsToNull()
  {
    var result = HttpResult.Fail<int>(HttpResultErrorCode.NotFound, "reason");
    Assert.Null(result.Extensions);
  }

  [Fact]
  public void HttpResult_Extensions_DefaultsToNull()
  {
    var result = HttpResult.Fail(HttpResultErrorCode.NotFound, "reason");
    Assert.Null(result.Extensions);
  }

  [Fact]
  public void ToActionResultT_Error_ReturnsObjectResultWithProblemDetails()
  {
    var result = HttpResult.Fail<int>(HttpResultErrorCode.Conflict, "conflicted").ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(409, objectResult.StatusCode);

    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal("Conflict.", problem.Title);
    Assert.Equal("conflicted", problem.Detail);
  }

  [Fact]
  public void ToActionResultT_Success_ReturnsOkObjectResult()
  {
    var result = HttpResult.Ok(42).ToActionResult();

    var okResult = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Equal(42, okResult.Value);
  }

  [Fact]
  public void ToActionResultT_WithExtensions_ExtensionsForwarded()
  {
    var result = HttpResult.Fail<int>(
      HttpResultErrorCode.ValidationFailed,
      "Validation failed",
      new() { ["field"] = "email" }
    ).ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result.Result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.True(problem.Extensions.ContainsKey("field"));
    Assert.Equal("email", problem.Extensions["field"]);
  }

  [Theory]
  [InlineData(HttpResultErrorCode.NotFound, 404)]
  [InlineData(HttpResultErrorCode.Conflict, 409)]
  [InlineData(HttpResultErrorCode.BadRequest, 400)]
  [InlineData(HttpResultErrorCode.Unauthorized, 401)]
  [InlineData(HttpResultErrorCode.Forbidden, 403)]
  [InlineData(HttpResultErrorCode.ValidationFailed, 400)]
  [InlineData(HttpResultErrorCode.NotImplemented, 501)]
  [InlineData(HttpResultErrorCode.ServiceUnavailable, 503)]
  [InlineData(HttpResultErrorCode.InternalServerError, 500)]
  public void ToActionResult_EachErrorCode_ReturnsCorrectStatusCode(
    HttpResultErrorCode errorCode, int expectedStatusCode)
  {
    var result = HttpResult.Fail(errorCode, "test reason").ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result);
    Assert.Equal(expectedStatusCode, objectResult.StatusCode);

    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Equal(expectedStatusCode, problem.Status);
    Assert.Equal("test reason", problem.Detail);
  }

  [Theory]
  [InlineData(HttpResultErrorCode.BadRequest, "Invalid request.")]
  [InlineData(HttpResultErrorCode.Unauthorized, "Unauthorized.")]
  [InlineData(HttpResultErrorCode.Forbidden, "Forbidden.")]
  [InlineData(HttpResultErrorCode.NotFound, "Not found.")]
  [InlineData(HttpResultErrorCode.Conflict, "Conflict.")]
  [InlineData(HttpResultErrorCode.InternalServerError, "Internal server error.")]
  [InlineData(HttpResultErrorCode.NotImplemented, "Not implemented.")]
  [InlineData(HttpResultErrorCode.ServiceUnavailable, "Service unavailable.")]
  [InlineData(HttpResultErrorCode.ValidationFailed, "Validation failed.")]
  public void ToActionResult_EachErrorCode_ReturnsExpectedTitle(
    HttpResultErrorCode errorCode, string expectedTitle)
  {
    var result = HttpResult.Fail(errorCode, "test").ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

    // Most titles come from the table the controllers and the pipeline share, which keys them by status.
    // ValidationFailed is the exception: it knows something the status does not, so it names itself and
    // a caller can tell a rejected value from a malformed request without reading the detail.
    Assert.Equal(expectedTitle, problem.Title);
    Assert.Equal("about:blank", problem.Type);
  }

  [Fact]
  public void ToActionResult_ErrorResponse_ContainsExpectedProblemDetailsFields()
  {
    var result = HttpResult.Fail(HttpResultErrorCode.NotFound, "User not found").ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

    Assert.Equal(404, problem.Status);
    Assert.Equal("Not found.", problem.Title);
    Assert.Equal("User not found", problem.Detail);
    Assert.Equal("about:blank", problem.Type);
    Assert.Null(problem.Instance);
  }

  [Fact]
  public void ToActionResult_Success_ReturnsNoContent()
  {
    var result = HttpResult.Ok().ToActionResult();

    Assert.IsType<NoContentResult>(result);
  }

  [Fact]
  public void ToActionResult_WithExtensions_ExtensionsForwarded()
  {
    var result = HttpResult.Fail(
      HttpResultErrorCode.ValidationFailed,
      "Validation failed",
      new() { ["field"] = "email", ["code"] = "duplicate" }
    ).ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

    Assert.True(problem.Extensions.ContainsKey("field"));
    Assert.Equal("email", problem.Extensions["field"]);
    Assert.True(problem.Extensions.ContainsKey("code"));
    Assert.Equal("duplicate", problem.Extensions["code"]);
  }

  [Fact]
  public void ToActionResult_WithoutExtensions_InstanceIsNull()
  {
    var result = HttpResult.Fail(HttpResultErrorCode.BadRequest, "invalid").ToActionResult();

    var objectResult = Assert.IsType<ObjectResult>(result);
    var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
    Assert.Null(problem.Instance);
  }
}
