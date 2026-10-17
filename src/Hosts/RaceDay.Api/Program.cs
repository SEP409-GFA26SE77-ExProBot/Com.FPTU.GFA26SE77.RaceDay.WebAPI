using MediatR;
using Microsoft.OpenApi;
using RaceDay.Modules.AuditLog.Api;
using RaceDay.Modules.AuditLog.Application.EventHandlers;
using RaceDay.Modules.Event.Api;
using RaceDay.Modules.Finance.Api;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Iam.Api;
using RaceDay.Modules.Iam.Application.Auth.Register;
using RaceDay.Modules.Event.Application.Events;
using RaceDay.Modules.Map.Api;
using RaceDay.Modules.Map.Application.Courses;
using RaceDay.Modules.Device.Api;
using RaceDay.Modules.Device.Application.Auth;
using RaceDay.Modules.Payment.Api;
using RaceDay.Modules.Payment.Application;
using RaceDay.Shared.Infrastructure;
using RaceDay.Shared.Web;
using RaceDay.Shared.Web.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);
var allowedCorsOrigins = GetAllowedCorsOrigins(builder.Configuration["ALLOWS_CORS"]);

// Một lần AddMediatR cho mọi module — tránh miss INotificationHandler (finance ledger sau PayOS).
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(CreateRaceEventCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(IamAuditEventHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(CreateCheckoutCommand).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(GetCourseQuery).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(GetOrganizerWalletQuery).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(RequestDeviceCodeCommand).Assembly);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddSharedWeb(builder.Configuration);

builder.Services.AddIamModule(builder.Configuration);
builder.Services.AddEventModule(builder.Configuration);
builder.Services.AddAuditLogModule(builder.Configuration);
builder.Services.AddPaymentModule(builder.Configuration);
builder.Services.AddMapModule(builder.Configuration);
builder.Services.AddFinanceModule(builder.Configuration);
builder.Services.AddDeviceModule(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RaceDay API",
        Version = "v1",
        Description =
            """
            API nhóm theo **luồng nghiệp vụ** (xem Tags bên dưới). Không gọi lung tung — làm theo happy path.

            ### A. Auth (bắt buộc trước)
            1. `POST /api/v1/iam/auth/register` → OTP email
            2. `POST /api/v1/iam/auth/verify-email`
            3. `POST /api/v1/iam/auth/login` (username + password) → copy `accessToken` → nút **Authorize**
            - Seed admin (dev): `admin@raceday.vn` / `MatKhau@123`

            ### B. Organizer tạo giải (FE wizard 4 bước)
            1. Upload hồ sơ: `POST /api/v1/event/organizer/uploads`
            2. Tạo giải: `POST /api/v1/event/organizer/` (`submitForApproval=true`)
            3. (Tuỳ chọn) `.../submit`, `.../open-registration`

            ### C. Admin duyệt giải (chưa mở ĐK)
            1. `GET /api/v1/event/admin/pending`
            2. `POST .../approve` → status **approved** (mặc định **không** mở ĐK)
            3. `POST /api/v1/event/organizer/{id}/open-registration` → **registration-open**

            ### D. Runner đăng ký + PayOS (registration-open)
            1. `POST /api/v1/event/{eventId}/registrations` → `pending_payment`
            2. `POST /api/v1/payment/checkout` → mở `checkoutUrl` (mock hoặc PayOS)
            3. PAID → `pending_approval` → organizer approve → `confirmed` + bib + `qrPayload`
            4. `GET /api/v1/event/registrations/mine`

            ### E. Organizer map / course (sau khi có event + distances)
            1. `GET/PUT /api/v1/map/events/{eventId}/distances/{distanceId}/course`
            2. FE: tắt mock `NEXT_PUBLIC_RACE_MAP_USE_MOCK=false` — OSRM/simulator phía FE; geocode qua `GET /api/v1/map/geocode`

            ### F. Ví / giải ngân / hoàn tiền
            1. Paid → escrow ledger theo giải (`/api/v1/finance/organizer/...`)
            2. Organizer xin rút → Admin `.../finance/admin/payouts/{id}/approve`
            3. Runner hoàn: `POST /api/v1/finance/runner/refunds` (số tiền = payment.Amount × % policy)
            4. Organizer reject đăng ký đã trả → hoàn 100% tự động

            ### G. Device (Wear Device Flow + SOS)
            1. Watch: `POST /api/v1/device/auth/code` → QR
            2. Runner phone: `GET/POST /api/v1/device/link/{userCode}`
            3. Watch poll: `POST /api/v1/device/auth/token` → JWT scope `sos:create`
            4. Watch: `POST /api/v1/device/sos` (+ locations)
            Docs: `docs/Device_Wear_SOS_API_Flows.md`

            Docs: `docs/RaceDay_API_Guide.docx` ← sync từ `docs/RaceDay_API_Flows.md` + `docs/api-samples/`
            """
    });

    options.DocumentFilter<FlowTagsDocumentFilter>();

    options.TagActionsBy(api =>
        api.ActionDescriptor.EndpointMetadata
            .OfType<TagsAttribute>()
            .SelectMany(t => t.Tags)
            .DefaultIfEmpty(ApiTags.Host)
            .ToList());
    options.DocInclusionPredicate((_, _) => true);
    options.OrderActionsBy(api =>
    {
        var tag = api.GroupName
            ?? api.ActionDescriptor.EndpointMetadata
                .OfType<TagsAttribute>()
                .SelectMany(t => t.Tags)
                .FirstOrDefault()
            ?? ApiTags.Host;
        return $"{ApiTags.SortKey(tag)}:{tag}:{api.HttpMethod}:{api.RelativePath}";
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT từ POST /api/v1/iam/auth/login — dán accessToken vào đây (chỉ token, không cần chữ Bearer)."
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
        options.DocumentTitle = "RaceDay API — theo luồng nghiệp vụ";
        options.DocExpansion(DocExpansion.List);
        options.DefaultModelsExpandDepth(-1);
        options.DisplayRequestDuration();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseSharedWeb();
app.UseCors("AllowFrontend");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RaceDay.Api" }))
    .WithTags(ApiTags.Host)
    .WithSummary("Health check host");

app.MapIamEndpoints();
app.MapEventEndpoints();
app.MapAuditLogEndpoints();
app.MapPaymentEndpoints();
app.MapMapEndpoints();
app.MapFinanceEndpoints();
app.MapDeviceEndpoints();

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

static string[] GetAllowedCorsOrigins(string? configuredOrigins)
{
    const string defaultOrigins = "http://localhost:3000,http://localhost:3001,http://localhost:51356";

    return (string.IsNullOrWhiteSpace(configuredOrigins) ? defaultOrigins : configuredOrigins)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public partial class Program;
