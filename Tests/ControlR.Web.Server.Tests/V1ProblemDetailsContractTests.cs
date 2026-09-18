using System.Text.Json;

namespace ControlR.Web.Server.Tests;

/// <summary>
/// Ratchet for the V1 error contract. Every V1 failure answers with an RFC 9457 ProblemDetails body,
/// including the ones the pipeline produces before a controller runs, so the shipped OpenAPI document
/// has to say so: each declared 4xx/5xx response names application/problem+json and the ProblemDetails
/// schema. A bare application/json (or no content at all) on an error response is the document
/// promising a shape the endpoint does not send.
/// </summary>
public class V1ProblemDetailsContractTests
{
  private const string ProblemDetailsReference = "#/components/schemas/ProblemDetails";

  private const string ProblemMediaType = "application/problem+json";

  private static readonly string[] _httpVerbs =
    ["get", "put", "post", "delete", "patch", "head", "options", "trace"];

  [Fact]
  public void V1ErrorResponses_DeclareProblemJsonWithProblemDetails()
  {
    var documentPath = Path.Combine(
      FindRepositoryRoot(),
      "ControlR.Web.Server",
      "ControlR.Web.Server_v1.json");

    Assert.True(File.Exists(documentPath), $"Missing committed OpenAPI document: {documentPath}");

    var (checkedResponses, offenders) = ReadV1ErrorResponses(documentPath);

    // Guards against the test passing because it found nothing to check. A renamed document, or the
    // V1 group moving off the /api/v1 prefix, would otherwise read as success.
    Assert.True(
      checkedResponses >= 100,
      $"Expected the V1 document to declare many error responses but found {checkedResponses}. " +
      "Either the document was not regenerated or the paths no longer start with /api/v1.");

    Assert.True(
      offenders.Length == 0,
      $"{checkedResponses} V1 error responses declared; {offenders.Length} do not name " +
      $"{ProblemMediaType} with the ProblemDetails schema: {string.Join(" | ", offenders)}");
  }

  private static bool DeclaresProblemDetails(JsonElement response)
  {
    if (!response.TryGetProperty("content", out var content) ||
        !content.TryGetProperty(ProblemMediaType, out var mediaType) ||
        !mediaType.TryGetProperty("schema", out var schema))
    {
      return false;
    }

    return schema.TryGetProperty("$ref", out var reference) &&
      reference.GetString() == ProblemDetailsReference;
  }

  private static string FindRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "ControlR.slnx")))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate repository root containing ControlR.slnx.");
  }

  private static bool IsErrorResponse(string statusCode)
  {
    return statusCode.Length == 3 &&
      int.TryParse(statusCode, out var code) &&
      code >= 400 &&
      code <= 599;
  }

  /// <summary>
  /// Walks every /api/v1 operation's 4xx/5xx responses and reports the ones whose content does not
  /// name the problem media type with a ProblemDetails schema reference.
  /// </summary>
  private static (int CheckedResponses, string[] Offenders) ReadV1ErrorResponses(string documentPath)
  {
    using var document = JsonDocument.Parse(File.ReadAllText(documentPath));
    var checkedResponses = 0;
    var offenders = new List<string>();

    if (!document.RootElement.TryGetProperty("paths", out var paths))
    {
      return (checkedResponses, offenders.ToArray());
    }

    foreach (var pathProperty in paths.EnumerateObject())
    {
      if (!pathProperty.Name.StartsWith("/api/v1", StringComparison.Ordinal))
      {
        continue;
      }

      foreach (var verbProperty in pathProperty.Value.EnumerateObject())
      {
        if (!_httpVerbs.Contains(verbProperty.Name, StringComparer.OrdinalIgnoreCase))
        {
          continue;
        }

        if (!verbProperty.Value.TryGetProperty("responses", out var responses))
        {
          continue;
        }

        foreach (var response in responses.EnumerateObject())
        {
          if (!IsErrorResponse(response.Name))
          {
            continue;
          }

          checkedResponses++;

          if (!DeclaresProblemDetails(response.Value))
          {
            offenders.Add(
              $"{verbProperty.Name.ToUpperInvariant()} {pathProperty.Name} -> {response.Name}");
          }
        }
      }
    }

    return (checkedResponses, offenders.ToArray());
  }
}
