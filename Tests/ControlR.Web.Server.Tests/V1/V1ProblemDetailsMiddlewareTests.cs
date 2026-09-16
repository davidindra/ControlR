using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace ControlR.Web.Server.Tests.V1;

/// <summary>
/// Errors the pipeline produces before a controller runs. A request that matches no route, or that
/// reaches an endpoint without credentials, never gets to an action, so there is no controller left
/// to answer it. The status-pages branch registered in Program.cs is what puts a body on those
/// responses; these tests hold it to the same RFC 9457 shape the controllers use.
/// </summary>
public class V1ProblemDetailsMiddlewareTests(ITestOutputHelper testOutput)
{
  [Fact]
  public async Task UnauthenticatedV1Route_Returns401ProblemDetails()
  {
    using var testServer = await TestWebServerBuilder.CreateTestServer(testOutput);
    using var httpClient = await testServer.GetHttpClient();

    var response = await httpClient.GetAsync(
      HttpConstants.V1.ServerStatsEndpoint,
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    await AssertProblemBodyAsync(response, StatusCodes.Status401Unauthorized);
  }

  [Fact]
  public async Task UnmatchedV1Route_Returns404ProblemDetails()
  {
    using var testServer = await TestWebServerBuilder.CreateTestServer(testOutput);
    using var httpClient = await testServer.GetHttpClient();

    var response = await httpClient.GetAsync(
      "/api/v1/no-such-route",
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    await AssertProblemBodyAsync(response, StatusCodes.Status404NotFound);
  }

  /// <summary>
  /// Asserts the response is a problem+json document whose own status agrees with the status line.
  /// </summary>
  private async Task AssertProblemBodyAsync(HttpResponseMessage response, int expectedStatus)
  {
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    testOutput.WriteLine($"Problem body: {body}");

    var problem = await response.Content.ReadFromJsonAsync<ProblemWireDto>(
      TestContext.Current.CancellationToken);

    Assert.NotNull(problem);
    Assert.Equal(expectedStatus, problem.Status);
    Assert.False(string.IsNullOrWhiteSpace(problem.Title));
  }

  /// <summary>
  /// The wire shape of an RFC 9457 body, narrowed to what these tests read.
  /// </summary>
  private sealed record ProblemWireDto
  {
    [JsonPropertyName("status")]
    public int? Status { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
  }
}
