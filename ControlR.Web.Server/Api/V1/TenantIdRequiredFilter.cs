using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Rejects an empty tenant id before the action body runs. A <c>?tenantId=</c> that is absent or
/// unparseable binds to <see cref="Guid.Empty"/>, which is a malformed request rather than an
/// authorization decision, so it answers 400 instead of the 403 that naming another tenant earns.
/// Applied by <see cref="RequireTenantIdActionConvention"/> to the V1 actions that take one.
/// </summary>
public sealed class TenantIdRequiredFilter(string parameterName) : IActionFilter
{
  private readonly string _parameterName = parameterName;

  public void OnActionExecuted(ActionExecutedContext context)
  {
  }

  public void OnActionExecuting(ActionExecutingContext context)
  {
    if (!context.ActionArguments.TryGetValue(_parameterName, out var value) ||
        value is not Guid tenantId ||
        tenantId != Guid.Empty)
    {
      return;
    }

    // Built through the factory so the body carries the same traceId extension as every other
    // V1 error.
    var problem = context.HttpContext.RequestServices
      .GetRequiredService<ProblemDetailsFactory>()
      .CreateProblemDetails(
        context.HttpContext,
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid tenant ID.",
        detail: $"{_parameterName} must be a non-empty GUID.");

    context.Result = new BadRequestObjectResult(problem);
  }
}
