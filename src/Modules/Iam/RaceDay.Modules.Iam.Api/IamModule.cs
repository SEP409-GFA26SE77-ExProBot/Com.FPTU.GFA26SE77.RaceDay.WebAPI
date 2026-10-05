using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Iam.Application.Auth.ChangePassword;
using RaceDay.Modules.Iam.Application.Auth.ForgotPassword;
using RaceDay.Modules.Iam.Application.Auth.Login;
using RaceDay.Modules.Iam.Application.Auth.Register;
using RaceDay.Modules.Iam.Application.Auth.ResendOtp;
using RaceDay.Modules.Iam.Application.Auth.ResetPassword;
using RaceDay.Modules.Iam.Application.Auth.VerifyEmail;
using RaceDay.Modules.Iam.Application.Users.CreateUser;
using RaceDay.Modules.Iam.Application.Users.DeleteUser;
using RaceDay.Modules.Iam.Application.Users.GetUser;
using RaceDay.Modules.Iam.Application.Users.ListUsers;
using RaceDay.Modules.Iam.Application.Users.LockUser;
using RaceDay.Modules.Iam.Application.Users.UnlockUser;
using RaceDay.Modules.Iam.Application.Users.UpdateUser;
using RaceDay.Modules.Iam.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Iam.Api;

public static class IamModule
{
    public static IServiceCollection AddIamModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly));
        services.AddValidatorsFromAssembly(typeof(RegisterUserCommand).Assembly);
        services.AddIamInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapIamEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api/v1/iam")
            .WithTags(ApiTags.IamAuth)
            .RequireRateLimiting("public");

        publicApi.MapPost("/auth/register", async (RegisterUserCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/iam/users/{result.UserId}", result);
        })
        .WithSummary("1. Đăng ký tài khoản → gửi OTP email");

        publicApi.MapPost("/auth/verify-email", async (VerifyEmailCommand command, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(command, ct);
            return Results.Ok(new { message = "Email đã được xác thực. Bạn có thể đăng nhập." });
        })
        .WithSummary("2. Xác thực email bằng OTP");

        publicApi.MapPost("/auth/resend-otp", async (ResendEmailVerificationOtpCommand command, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(command, ct);
            return Results.Ok(new { message = "Đã gửi lại OTP xác thực email." });
        })
        .WithSummary("Gửi lại OTP xác thực email (chưa Active)");

        publicApi.MapPost("/auth/login", async (LoginCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return Results.Ok(result);
        })
        .WithSummary("3. Đăng nhập bằng tên đăng nhập → accessToken (dán vào Authorize)");

        publicApi.MapPost("/auth/forgot-password", async (ForgotPasswordCommand command, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(command, ct);
            return Results.Ok(new { message = "Nếu email tồn tại, OTP đặt lại mật khẩu đã được gửi." });
        })
        .WithSummary("Quên mật khẩu → gửi OTP");

        publicApi.MapPost("/auth/reset-password", async (ResetPasswordWithOtpCommand command, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(command, ct);
            return Results.Ok(new { message = "Đã đặt lại mật khẩu thành công." });
        })
        .WithSummary("Đặt lại mật khẩu bằng OTP");

        publicApi.MapPost("/auth/change-password", async (ChangePasswordCommand command, IMediator mediator, CancellationToken ct) =>
            {
                await mediator.Send(command, ct);
                return Results.Ok(new { message = "Đã đổi mật khẩu thành công." });
            })
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Đổi mật khẩu (cần JWT)");

        var adminApi = app.MapGroup("/api/v1/iam/admin")
            .WithTags(ApiTags.IamAdmin)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        adminApi.MapGet("/users", async (int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListUsersQuery(page ?? 1, pageSize ?? 20), ct);
            return Results.Ok(result);
        })
        .WithSummary("Danh sách users (phân trang)");

        adminApi.MapGet("/users/{userId:guid}", async (Guid userId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetUserByIdQuery(userId), ct);
            return Results.Ok(result);
        })
        .WithSummary("Chi tiết user");

        adminApi.MapPost("/users", async (CreateUserCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var userId = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/iam/admin/users/{userId}", new { userId });
        })
        .WithSummary("Tạo user (admin provision)");

        adminApi.MapPut("/users/{userId:guid}", async (Guid userId, UpdateUserRequest body, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new UpdateUserCommand(
                userId,
                body.Username,
                body.FullName,
                body.Phone,
                body.DateOfBirth,
                body.Gender,
                body.RoleId,
                body.Status,
                body.ProfileDescription,
                body.ContactEmail,
                body.ContactPhone), ct);
            return Results.NoContent();
        })
        .WithSummary("Cập nhật user");

        adminApi.MapPost("/users/{userId:guid}/lock", async (Guid userId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new LockUserCommand(userId), ct);
            return Results.NoContent();
        })
        .WithSummary("Khóa user");

        adminApi.MapPost("/users/{userId:guid}/unlock", async (Guid userId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new UnlockUserCommand(userId), ct);
            return Results.NoContent();
        })
        .WithSummary("Mở khóa user");

        adminApi.MapPost("/users/{userId:guid}/reset-password", async (Guid userId, AdminResetPasswordRequest body, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new AdminResetPasswordCommand(userId, body.NewPassword), ct);
            return Results.Ok(new { message = "Đã đặt lại mật khẩu thành công." });
        })
        .WithSummary("Admin đặt lại mật khẩu user");

        adminApi.MapDelete("/users/{userId:guid}", async (Guid userId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DeleteUserCommand(userId), ct);
            return Results.NoContent();
        })
        .WithSummary("Xóa user");

        adminApi.MapGet("/health", () => Results.Ok(new { module = "iam", status = "ok" }))
            .WithSummary("Health module IAM");

        return app;
    }
}

public sealed record AdminResetPasswordRequest(string NewPassword);
