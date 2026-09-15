using RaceDay.Modules.AuditLog.Api;
using RaceDay.Modules.Iam.Api;
using RaceDay.Modules.Payment.Api;
using RaceDay.Shared.Infrastructure;
using RaceDay.Shared.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddSharedWeb();

builder.Services.AddIamModule(builder.Configuration);
builder.Services.AddAuditLogModule(builder.Configuration);
builder.Services.AddPaymentModule(builder.Configuration);

builder.Services.AddAuthentication()
    .AddJwtBearer(); // Configure Jwt:Authority / SigningKey in appsettings for production
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseSharedWeb();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RaceDay.Api" }));

app.MapIamEndpoints();
app.MapAuditLogEndpoints();
app.MapPaymentEndpoints();

app.Run();

public partial class Program;
