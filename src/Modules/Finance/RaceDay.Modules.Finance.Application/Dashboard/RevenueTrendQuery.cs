using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Dashboard;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Dashboard;

public sealed record RevenueDayDto(DateOnly Date, decimal Collected, decimal Refunded)
{
    public decimal NetCashFlow => Collected - Refunded;
}
public sealed record CurrencyRevenueTrendDto(string Currency, decimal TotalCollected, decimal TotalRefunded,
    IReadOnlyList<RevenueDayDto> Days)
{
    public decimal NetCashFlow => TotalCollected - TotalRefunded;
}
public sealed record RevenueTrendDto(Guid? EventId, DateOnly From, DateOnly To, string TimeZone,
    IReadOnlyList<CurrencyRevenueTrendDto> Series, DateTime GeneratedAtUtc);
public sealed record GetRevenueTrendQuery(Guid? EventId, DateOnly? From, DateOnly? To) : IRequest<RevenueTrendDto>;

public sealed class GetRevenueTrendQueryHandler(IEventFinanceQuery events, IOrganizerRegistrationScopeQuery registrations,
    IPaymentRevenueQuery payments, ICurrentUserAccessor user) : IRequestHandler<GetRevenueTrendQuery, RevenueTrendDto>
{
    public async Task<RevenueTrendDto> Handle(GetRevenueTrendQuery request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new DomainException("Cần đăng nhập.");
        var range = DashboardDateRange.Create(request.From, request.To, DateTime.UtcNow);
        if (request.EventId.HasValue)
        {
            var race = await events.GetEventAsync(request.EventId.Value, ct);
            if (race is null || race.OrganizerUserId != userId)
                throw new DomainException("Không tìm thấy giải thuộc quyền quản lý của bạn.");
        }
        var ids = await registrations.ListIdsAsync(userId, request.EventId, ct);
        var rows = await payments.ReadAsync(ids, range.FromUtc, range.ToUtcExclusive, ct);
        var currencies = rows.Select(x => x.Currency).Distinct().OrderBy(x => x).ToArray();
        if (currencies.Length == 0) currencies = ["VND"];
        var series = currencies.Select(currency =>
        {
            var daily = rows.Where(x => x.Currency == currency).GroupBy(x => x.Date)
                .ToDictionary(g => g.Key, g => new RevenueDayDto(g.Key, g.Sum(x => x.Collected), g.Sum(x => x.Refunded)));
            var days = range.Days().Select(d => daily.GetValueOrDefault(d) ?? new RevenueDayDto(d, 0, 0)).ToArray();
            return new CurrencyRevenueTrendDto(currency, days.Sum(x => x.Collected), days.Sum(x => x.Refunded), days);
        }).ToArray();
        return new(request.EventId, range.From, range.To, DashboardDateRange.TimeZone, series, DateTime.UtcNow);
    }
}
