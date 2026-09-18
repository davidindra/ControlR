using System.Net;
using System.Net.Http.Json;
using ControlR.Web.Server.Authn;
using ControlR.Web.Server.Authz.Permissions;
using ControlR.Web.Server.Data.Entities;
using ControlR.Web.Server.Services;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.DeviceFileSystem;

namespace ControlR.Web.Server.Tests.V1;

/// <summary>
/// Reads the V1 error contract off the wire. The published document says every failure answers
/// application/problem+json with a title from the shared table, and V1ProblemDetailsContractTests
/// checks that the document says so. This covers what a document cannot: the media type and title a
/// caller actually receives from a controller's Problem() call. The media type is not declared by the
/// result object (ControllerBase.Problem() leaves ObjectResult.ContentTypes empty) so only the wire
/// can show that the response is labeled application/problem+json.
/// </summary>
public class V1ProblemDetailsWireFormatTests(ITestOutputHelper testOutput)
{
  [Fact]
  public async Task ControllerProblemResult_AnswersProblemJsonWithTableTitle()
  {
    using var testServer = await TestWebServerBuilder.CreateTestServer(testOutput);
    var tenant = await testServer.Services.CreateTestTenant();
    var user = await testServer.Services.CreateTestUser(tenant.Id, $"wire-prob-{Guid.NewGuid():N}@t.local");
    using var client = await CreateAuthenticatedClientAsync(testServer, user);

    // An unknown device never reaches an agent, so this answer comes straight from the controller's
    // Problem() call rather than from the shared helper.
    using var response = await client.PostAsJsonAsync(
      $"{HttpConstants.V1.DeviceFileSystemEndpoint}/contents?tenantId={tenant.Id}",
      new DeviceDirectoryContentsRequestDto(Guid.NewGuid(), "/parent"),
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
      TestContext.Current.CancellationToken);
    Assert.Equal(StatusCodes.Status404NotFound, problem?.Status);
    Assert.Equal("Not found.", problem?.Title);
  }

  private static async Task<HttpClient> CreateAuthenticatedClientAsync(
    TestWebServer testServer,
    AppUser user)
  {
    var patManager = testServer.Services.GetRequiredService<IPersonalAccessTokenManager>();
    var patResult = await patManager.CreateToken(
      new InternalDtos.CreatePersonalAccessTokenRequestDto(
        "V1 wire format PAT",
        PersonalAccessTokenPermissionMode.InheritOwner),
      user.Id,
      new PrincipalDescriptor(PrincipalType.User, user.Id, user.TenantId, "test"));
    Assert.True(patResult.IsSuccess, patResult.Reason);

    var client = testServer.Factory.CreateClient();
    client.DefaultRequestHeaders.Add(
      PersonalAccessTokenAuthenticationSchemeOptions.DefaultHeaderName,
      patResult.Value.PlainTextToken);
    return client;
  }
}
