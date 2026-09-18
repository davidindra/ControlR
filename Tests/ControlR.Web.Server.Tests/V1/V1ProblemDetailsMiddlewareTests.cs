using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace ControlR.Web.Server.Tests.V1;

/// <summary>
/// Errors the pipeline produces before a controller runs. A request that matches no route, or that
/// reaches an endpoint without credentials, never gets to an action, so there is no controller left
/// to answer it. <c>ApiProblemDetailsMiddleware</c> puts a body on those responses, and these tests
/// hold it to the same RFC 9457 shape and the same title table the controllers use, so one status has
/// one title no matter which component answered.
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
    await AssertProblemBodyAsync(response, StatusCodes.Status401Unauthorized, "Unauthorized.");
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
    await AssertProblemBodyAsync(response, StatusCodes.Status404NotFound, "Not found.");
  }

  [Fact]
  public async Task UnmatchedV1Route_WhenAcceptRejectsJson_ReturnsPlainTextFallback()
  {
    using var testServer = await TestWebServerBuilder.CreateTestServer(testOutput);
    using var httpClient = await testServer.GetHttpClient();

    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/no-such-route");
    request.Headers.Accept.ParseAdd("text/html");

    var response = await httpClient.SendAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

    var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    testOutput.WriteLine($"Fallback body: {body}");

    Assert.Contains("404", body, StringComparison.Ordinal);
  }

  /// <summary>
  /// Asserts the response is a problem+json document whose own status and table title agree with what
  /// the status line says. The title is asserted by value rather than through the shared constants so
  /// a change to the vocabulary has to be made here too.
  /// </summary>
  private async Task AssertProblemBodyAsync(
    HttpResponseMessage response,
    int expectedStatus,
    string expectedTitle)
  {
    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    testOutput.WriteLine($"Problem body: {body}");

    var problem = await response.Content.ReadFromJsonAsync<ProblemWireDto>(
      TestContext.Current.CancellationToken);

    Assert.NotNull(problem);
    Assert.Equal(expectedStatus, problem.Status);
    Assert.Equal(expectedTitle, problem.Title);
    Assert.Equal("about:blank", problem.Type);
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

    [JsonPropertyName("type")]
    public string? Type { get; init; }
  }
}
