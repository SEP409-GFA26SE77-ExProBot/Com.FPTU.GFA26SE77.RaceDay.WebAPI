using Microsoft.OpenApi;
using RaceDay.Modules.AuditLog.Api;
using RaceDay.Modules.Iam.Api;
using RaceDay.Modules.Payment.Api;
using RaceDay.Shared.Infrastructure;
using RaceDay.Shared.Web;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://localhost:3001")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddSharedWeb(builder.Configuration);

builder.Services.AddIamModule(builder.Configuration);
builder.Services.AddAuditLogModule(builder.Configuration);
builder.Services.AddPaymentModule(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RaceDay API",
        Version = "v1",
        Description = "RaceDay modular monolith — endpoints grouped by module tags"
    });

    // Stable Swagger UI order (matches Map*Endpoints composition).
    options.TagActionsBy(api =>
        api.ActionDescriptor.EndpointMetadata
            .OfType<TagsAttribute>()
            .SelectMany(t => t.Tags)
            .DefaultIfEmpty("Host")
            .ToList());
    options.DocInclusionPredicate((_, _) => true);
    options.OrderActionsBy(api =>
    {
        var tag = api.GroupName
            ?? api.ActionDescriptor.EndpointMetadata
                .OfType<TagsAttribute>()
                .SelectMany(t => t.Tags)
                .FirstOrDefault()
            ?? "Host";
        var order = tag switch
        {
            "IAM" => "1",
            "AuditLog" => "2",
            "Payment" => "3",
            _ => "9"
        };
        return $"{order}:{tag}:{api.HttpMethod}:{api.RelativePath}";
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT từ POST /api/v1/iam/auth/login — dán accessToken vào đây."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RaceDay API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseSharedWeb();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RaceDay.Api" }))
    .WithTags("Host");

app.MapIamEndpoints();
app.MapAuditLogEndpoints();
app.MapPaymentEndpoints();

app.Run();

static void LoadDotEnv()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate))
        {
            DotNetEnv.Env.Load(candidate);
            return;
        }

        dir = dir.Parent;
    }
}

public partial class Program;
