using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RaceDay.Shared.Web.OpenApi;

/// <summary>Injects ordered tag metadata (descriptions) into the OpenAPI document.</summary>
public sealed class FlowTagsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        _ = context;
        if (swaggerDoc.Tags is null)
        {
            swaggerDoc.Tags = new HashSet<OpenApiTag>();
        }

        foreach (var (name, description) in ApiTags.All)
        {
            var existing = swaggerDoc.Tags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.Ordinal));
            if (existing is null)
            {
                swaggerDoc.Tags.Add(new OpenApiTag
                {
                    Name = name,
                    Description = description
                });
            }
            else if (string.IsNullOrWhiteSpace(existing.Description))
            {
                existing.Description = description;
            }
        }
    }
}
