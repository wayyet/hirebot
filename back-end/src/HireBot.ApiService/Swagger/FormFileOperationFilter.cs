using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HireBot.ApiService.Swagger;

public sealed class FormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasFormFile = context.ApiDescription.ParameterDescriptions.Any(param =>
            param.Type == typeof(IFormFile) ||
            param.Type == typeof(IFormFileCollection) ||
            (param.Type?.IsGenericType == true &&
             param.Type.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
             param.Type.GetGenericArguments()[0] == typeof(IFormFile)));

        if (!hasFormFile)
        {
            return;
        }

        var requestBody = operation.RequestBody as OpenApiRequestBody;
        if (requestBody is null)
        {
            requestBody = new OpenApiRequestBody();
            operation.RequestBody = requestBody;
        }

        requestBody.Content ??= new Dictionary<string, OpenApiMediaType>();
        requestBody.Content.Clear();
        requestBody.Content["multipart/form-data"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                },
                Required = new HashSet<string> { "file" }
            }
        };
    }
}

