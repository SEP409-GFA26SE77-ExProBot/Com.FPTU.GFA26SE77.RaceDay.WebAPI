using FluentValidation;
using MediatR;
using RaceDay.Modules.Device.Application.Abstractions;
using RaceDay.Modules.Device.Contracts.Events;
using RaceDay.Modules.Device.Domain.Auth;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Device.Application.Auth;

public sealed record DeviceCodeRequestDto(Guid DeviceId);

public sealed record DeviceCodeResponseDto(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    string VerificationUrlComplete,
    int ExpiresIn,
    int Interval);

public sealed record RequestDeviceCodeCommand(Guid DeviceId) : IRequest<DeviceCodeResponseDto>;

public sealed class RequestDeviceCodeCommandValidator : AbstractValidator<RequestDeviceCodeCommand>
{
    public RequestDeviceCodeCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
    }
}

public sealed class RequestDeviceCodeCommandHandler(
    IDeviceAuthRequestRepository requests,
    IDeviceUnitOfWork unitOfWork,
    IDeviceFrontendOptions frontend) : IRequestHandler<RequestDeviceCodeCommand, DeviceCodeResponseDto>
{
    public async Task<DeviceCodeResponseDto> Handle(
        RequestDeviceCodeCommand request,
        CancellationToken cancellationToken)
    {
        var deviceCode = DeviceCodeCrypto.GenerateDeviceCode();
        string userCode;
        DeviceAuthRequest? clash;
        do
        {
            userCode = DeviceCodeCrypto.GenerateUserCode();
            clash = await requests.GetActiveByUserCodeAsync(userCode, cancellationToken);
        } while (clash is not null);

        var entity = DeviceAuthRequest.Create(
            request.DeviceId.ToString(),
            DeviceCodeCrypto.Hash(deviceCode),
            userCode,
            DateTime.UtcNow.AddMinutes(5));

        await requests.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var baseUrl = frontend.VerificationBaseUrl.TrimEnd('/');
        var verificationUrl = $"{baseUrl}/device";
        return new DeviceCodeResponseDto(
            deviceCode,
            userCode,
            verificationUrl,
            $"{verificationUrl}?code={userCode}",
            ExpiresIn: 300,
            Interval: 3);
    }
}

public sealed record DeviceTokenRequestDto(string DeviceCode, string? GrantType);

public sealed record DeviceTokenResponseDto(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    string Scope);

public sealed record PollDeviceTokenCommand(string DeviceCode) : IRequest<DeviceTokenResponseDto>;

public sealed class PollDeviceTokenCommandValidator : AbstractValidator<PollDeviceTokenCommand>
{
    public PollDeviceTokenCommandValidator()
    {
        RuleFor(x => x.DeviceCode).NotEmpty();
    }
}

/// <summary>RFC 8628-style errors — ExceptionHandling maps DomainException to 400 { error }.</summary>
public sealed class DeviceAuthPendingException() : DomainException("authorization_pending");
public sealed class DeviceAuthExpiredException() : DomainException("expired_token");
public sealed class DeviceAuthInvalidGrantException() : DomainException("invalid_grant");

public sealed class PollDeviceTokenCommandHandler(
    IDeviceAuthRequestRepository requests,
    ILinkedDeviceRepository linkedDevices,
    IDeviceUnitOfWork unitOfWork,
    IWearTokenService tokens,
    IPublisher publisher) : IRequestHandler<PollDeviceTokenCommand, DeviceTokenResponseDto>
{
    public async Task<DeviceTokenResponseDto> Handle(
        PollDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        var hash = DeviceCodeCrypto.Hash(request.DeviceCode.Trim());
        var auth = await requests.GetByDeviceCodeHashAsync(hash, cancellationToken);
        if (auth is null || auth.Consumed)
        {
            throw new DeviceAuthInvalidGrantException();
        }

        if (auth.IsExpired(DateTime.UtcNow))
        {
            throw new DeviceAuthExpiredException();
        }

        if (auth.ApprovedAtUtc is null || auth.RunnerUserId is null)
        {
            throw new DeviceAuthPendingException();
        }

        auth.MarkConsumed();
        requests.Update(auth);

        var refreshPlain = DeviceCodeCrypto.GenerateRefreshToken();
        var linked = LinkedDevice.Create(
            auth.RunnerUserId.Value,
            auth.ClientDeviceId,
            name: null,
            DeviceCodeCrypto.Hash(refreshPlain));
        await linkedDevices.AddAsync(linked, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var pair = tokens.Issue(linked);

        await publisher.Publish(
            new IntegrationEventOccurred(
                new WearDeviceLinkedIntegrationEvent(linked.Id, linked.RunnerUserId, linked.ClientDeviceId)),
            cancellationToken);

        return new DeviceTokenResponseDto(
            pair.AccessToken,
            refreshPlain,
            "Bearer",
            pair.ExpiresInSeconds,
            pair.Scope);
    }
}

public sealed record LinkPreviewDto(string DeviceId, string UserCode, DateTime ExpiresAtUtc, string DisplayName);

public sealed record GetLinkPreviewQuery(string UserCode) : IRequest<LinkPreviewDto>;

public sealed class GetLinkPreviewQueryHandler(
    IDeviceAuthRequestRepository requests) : IRequestHandler<GetLinkPreviewQuery, LinkPreviewDto>
{
    public async Task<LinkPreviewDto> Handle(GetLinkPreviewQuery request, CancellationToken cancellationToken)
    {
        var code = DeviceAuthRequest.NormalizeUserCode(request.UserCode);
        var auth = await requests.GetActiveByUserCodeAsync(code, cancellationToken);
        if (auth is null || auth.IsExpired(DateTime.UtcNow) || auth.Consumed || auth.ApprovedAtUtc is not null)
        {
            throw new DomainException("Không tìm thấy mã liên kết hợp lệ.");
        }

        if (auth.FailedAttempts >= 5)
        {
            throw new DomainException("Quá số lần thử — thử lại sau.");
        }

        return new LinkPreviewDto(auth.ClientDeviceId, auth.UserCode, auth.ExpiresAtUtc, "Wear OS watch");
    }
}

public sealed record ApproveLinkCommand(string UserCode, string? DeviceName) : IRequest;

public sealed class ApproveLinkCommandHandler(
    IDeviceAuthRequestRepository requests,
    IDeviceUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<ApproveLinkCommand>
{
    public async Task Handle(ApproveLinkCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var code = DeviceAuthRequest.NormalizeUserCode(request.UserCode);
        var auth = await requests.GetActiveByUserCodeAsync(code, cancellationToken);
        if (auth is null || auth.IsExpired(DateTime.UtcNow) || auth.Consumed)
        {
            // bump attempts on wrong codes is harder without a row — skip
            throw new DomainException("Không tìm thấy mã liên kết hợp lệ.");
        }

        if (auth.FailedAttempts >= 5)
        {
            throw new DomainException("Quá số lần thử — thử lại sau.");
        }

        if (auth.ApprovedAtUtc is not null)
        {
            throw new DomainException("Mã đã được xác nhận.");
        }

        auth.Approve(userId);
        requests.Update(auth);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record LinkedDeviceDto(
    Guid Id,
    string DeviceId,
    string Name,
    DateTime LinkedAtUtc,
    DateTime? RevokedAtUtc);

public sealed record ListMyLinkedDevicesQuery : IRequest<IReadOnlyList<LinkedDeviceDto>>;

public sealed class ListMyLinkedDevicesQueryHandler(
    ILinkedDeviceRepository linkedDevices,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyLinkedDevicesQuery, IReadOnlyList<LinkedDeviceDto>>
{
    public async Task<IReadOnlyList<LinkedDeviceDto>> Handle(
        ListMyLinkedDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var list = await linkedDevices.ListByRunnerAsync(userId, cancellationToken);
        return list
            .Where(x => !x.IsRevoked)
            .Select(x => new LinkedDeviceDto(x.Id, x.ClientDeviceId, x.Name, x.LinkedAtUtc, x.RevokedAtUtc))
            .ToList();
    }
}

public sealed record RevokeLinkedDeviceCommand(Guid LinkedDeviceId) : IRequest;

public sealed class RevokeLinkedDeviceCommandHandler(
    ILinkedDeviceRepository linkedDevices,
    IDeviceUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RevokeLinkedDeviceCommand>
{
    public async Task Handle(RevokeLinkedDeviceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var device = await linkedDevices.GetByIdAsync(request.LinkedDeviceId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy thiết bị.");

        if (!currentUser.IsAdmin && device.RunnerUserId != userId)
        {
            throw new DomainException("Không có quyền thu hồi thiết bị này.");
        }

        device.Revoke();
        linkedDevices.Update(device);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
