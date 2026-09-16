using ControlR.Libraries.Api.Contracts.Dtos;
using System.Text.Json;

namespace ControlR.ApiClient;

internal static class HttpExtensions
{
  private static readonly JsonSerializerOptions _jsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public static async Task EnsureSuccessStatusCodeWithDetails(this HttpResponseMessage response)
  {
    if (!response.IsSuccessStatusCode)
    {
      var exception = await TryGetEnrichedException(response);
      if (exception is not null)
      {
        throw exception;
      }

      // Fall back to the default behavior if we couldn't enrich the exception.
      response.EnsureSuccessStatusCode();
    }
  }

  private static string EnrichErrorMessage(string rawContent, ProblemDetailsDto? problemDetails)
  {
    if (problemDetails is { } pd)
    {
      var bestMessage = GetBestMessage(pd);
      if (pd.Status.HasValue)
      {
        return $"[Status: {pd.Status}] {bestMessage}";
      }

      return bestMessage;
    }

    // Not a ProblemDetails response, so use the raw content as the error message, unescaping JSON
    // string quotes if present. No /api/v1 endpoint produces this any more - every V1 failure ships a
    // ProblemDetails body - but the deprecated /api/* endpoints this client still calls do answer some
    // failures with a bare JSON string, which is what this path unwraps.
    var trimmed = rawContent.Trim();
    if (trimmed.Length > 2 && trimmed[0] == '"' && trimmed[^1] == '"')
    {
      try
      {
        return JsonSerializer.Deserialize<string>(trimmed) ?? trimmed;
      }
      catch
      {
        return trimmed[1..^1];
      }
    }

    return string.IsNullOrWhiteSpace(trimmed) ? "An unexpected error occurred." : trimmed;
  }

  private static string GetBestMessage(ProblemDetailsDto problemDetails)
  {
    return problemDetails.Detail ?? problemDetails.Title ?? "An error occurred.";
  }


  private static async Task<HttpRequestException?> TryGetEnrichedException(HttpResponseMessage response)
  {
    try
    {
      var rawContent = await response.Content.ReadAsStringAsync();

      ProblemDetailsDto? problemDetails = null;
      try
      {
        problemDetails = JsonSerializer.Deserialize<ProblemDetailsDto>(rawContent, _jsonOptions);
      }
      catch
      {
        // The body is not a JSON object - an HTML error page, a plain-text status page, or one of
        // the bare JSON strings the deprecated /api/* endpoints return for some failures. A
        // /api/v1 failure is always a ProblemDetails object and lands in the try. Either way,
        // EnrichErrorMessage makes something readable out of the raw content.
      }

      var enrichedMessage = EnrichErrorMessage(rawContent, problemDetails);
      return new HttpRequestException(enrichedMessage, null, response.StatusCode);
    }
    catch
    {
      return null;
    }
  }
}
