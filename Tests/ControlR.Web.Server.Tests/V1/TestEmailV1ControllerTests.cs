using ControlR.Web.Server.Api.V1;
using ControlR.Web.Server.Data;
using ControlR.Web.Server.Services;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests.V1;

/// <summary>
/// The test-email endpoint echoes the status the sender reported. The sender owns the disabled and
/// unconfigured cases, so the action does not pre-empt them with a status of its own.
/// </summary>
public class TestEmailV1ControllerTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task SendTestEmail_WhenEmailSendingIsDisabled_ReturnsConflictProblemDetails()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(
      _testOutput,
      extraConfiguration: new Dictionary<string, string?>
      {
        ["AppOptions:DisableEmailSending"] = "true"
      });

    using var scope = testApp.CreateScope();
    var services = scope.ServiceProvider;
    var (controller, _, _) = await scope.CreateControllerWithTestData<TestEmailController>(
      userEmail: "test-email-disabled@test.local");

    var result = await controller.SendTestEmail(
      services.GetRequiredService<AppDb>(),
      services.GetRequiredService<IControlrEmailSender>());

    // The title is asserted by value rather than through the shared constant so a change to the
    // vocabulary has to be made here too.
    ProblemDetailsAsserts.AssertProblem(result, StatusCodes.Status409Conflict, "Conflict.");
  }
}
