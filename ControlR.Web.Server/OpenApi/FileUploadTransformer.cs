using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControlR.Web.Server.OpenApi;

/// <summary>
/// Gives both upload operations the multipart request body their actions cannot describe, because the
/// actions read the form themselves rather than binding it as a parameter.
/// </summary>
public class FileUploadTransformer : IOpenApiDocumentTransformer
{
  private static readonly string[] _uploadPaths =
  [
    $"{HttpConstants.Internal.DeviceFileSystemEndpoint}/upload/{{deviceId}}",
    $"{HttpConstants.V1.DeviceFileSystemEndpoint}/upload/{{deviceId}}",
  ];

  public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
  {
    foreach (var pathName in _uploadPaths)
    {
      ApplyUploadRequestBody(document, pathName);
    }

    return Task.CompletedTask;
  }

  private static void ApplyUploadRequestBody(OpenApiDocument document, string pathName)
  {
    if (!document.Paths.TryGetValue(pathName, out var uploadPath))
    {
      return;
    }

    if (uploadPath.Operations is not { } operations)
    {
      return;
    }

    foreach (var operation in operations.Values)
    {
      operation.RequestBody = new OpenApiRequestBody
      {
        Content = new Dictionary<string, OpenApiMediaType>
        {
          ["multipart/form-data"] = new OpenApiMediaType
          {
            Schema = new OpenApiSchema
            {
              Type = JsonSchemaType.Object,
              Properties = new Dictionary<string, IOpenApiSchema>
              {
                ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                ["targetSaveDirectory"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["overwrite"] = new OpenApiSchema { Type = JsonSchemaType.Boolean }
              },
              Required = new HashSet<string> { "file", "targetSaveDirectory" }
            }
          }
        }
      };
    }
  }
}
