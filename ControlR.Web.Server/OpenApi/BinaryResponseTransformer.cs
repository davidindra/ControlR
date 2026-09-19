using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControlR.Web.Server.OpenApi;

/// <summary>
/// Gives the V1 operations that write a raw response body themselves the media type they return. A
/// streamed response has no DTO, and an attribute that declares no type also declares no content type.
/// </summary>
public class BinaryResponseTransformer : IOpenApiDocumentTransformer
{
  private static readonly Dictionary<string, string> _responseMediaTypes = new(StringComparer.Ordinal)
  {
    [$"{HttpConstants.V1.DesktopPreviewEndpoint}/{{deviceId}}/{{targetProcessId}}"] = "image/jpeg",
    [$"{HttpConstants.V1.DeviceFileSystemEndpoint}/download-archive/{{deviceId}}"] = "application/octet-stream",
    [$"{HttpConstants.V1.DeviceFileSystemEndpoint}/download/{{deviceId}}"] = "application/octet-stream",
    [$"{HttpConstants.V1.DeviceFileSystemEndpoint}/logs/{{deviceId}}/contents"] = "text/plain",
  };

  public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
  {
    foreach (var (pathName, mediaType) in _responseMediaTypes)
    {
      if (!document.Paths.TryGetValue(pathName, out var pathItem) ||
          pathItem.Operations is not { } operations)
      {
        continue;
      }

      foreach (var operation in operations.Values)
      {
        if (operation?.Responses is not { } responses ||
            !responses.TryGetValue(StatusCodes.Status200OK.ToString(), out var response) ||
            response is not OpenApiResponse binaryResponse)
        {
          continue;
        }

        // A media type with no schema: the action writes the bytes itself, so the contract states
        // what they are and nothing more.
        binaryResponse.Content ??= new Dictionary<string, OpenApiMediaType>();
        binaryResponse.Content[mediaType] = new OpenApiMediaType();
      }
    }

    return Task.CompletedTask;
  }
}
