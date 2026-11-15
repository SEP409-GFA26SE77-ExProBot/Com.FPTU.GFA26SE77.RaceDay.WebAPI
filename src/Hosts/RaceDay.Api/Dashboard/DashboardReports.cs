using RaceDay.Modules.AuditLog.Application.AuditEntries;
using RaceDay.Modules.Event.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Dashboard;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Iam.Application.Dashboard;
using RaceDay.Shared.Application.Dashboard;

namespace RaceDay.Api.Dashboard;

public static class DashboardReports
{
    private const string Current = "Số liệu hiện tại khi xuất, không lọc theo kỳ biểu đồ. Các nguồn được đọc lần lượt, không phải snapshot giao dịch chung.";
    private const string Cash = "Trong kỳ biểu đồ, theo ngày thu/hoàn thành công. Thu trừ hoàn chưa trừ phí; không phải lợi nhuận hay số dư có thể rút.";
    private static readonly (string Label, string[] Statuses)[] EventGroups = [
        ("Đang chuẩn bị", ["draft", "approved"]), ("Chờ duyệt", ["pending"]),
        ("Đang mở đăng ký", ["registration-open"]), ("Đã đóng đăng ký", ["closed"]),
        ("Đã hoàn thành", ["completed"]), ("Cần chú ý", ["rejected", "suspended", "cancelled"])];
    private static readonly (string Label, string[] Statuses)[] RegistrationGroups = [
        ("Chờ thanh toán", ["pending_payment"]), ("Chờ duyệt", ["pending_approval"]),
        ("Đăng ký đã xác nhận", ["confirmed", "finished"]), ("Hủy / từ chối", ["cancelled", "rejected"])];

    public static DashboardReport Admin(AdminDashboardDto data, IReadOnlyList<AuditEntryDto> audit)
        => new("Dashboard Admin", "Phạm vi: tài khoản toàn hệ thống", data.From, data.To, DateTime.UtcNow, [
            new("Tài khoản", Current + " Riêng tài khoản mới được đếm trong kỳ biểu đồ.", ["Chỉ số", "Số lượng"], [
                ["Tổng tài khoản", data.TotalUsers], ["Đã kích hoạt", data.ActiveUsers],
                ["Chờ xác thực email", data.PendingUsers], ["Đã khóa", data.LockedUsers],
                ["Tài khoản mới trong kỳ biểu đồ", data.NewUsersInPeriod]]),
            new("Vai trò", Current, ["Vai trò", "Số tài khoản"],
                data.UsersByRole.Select(x => new object[] { Role(x.Role), x.Count }).ToArray()),
            new("Tài khoản mới", "Trong kỳ biểu đồ, theo ngày tạo tài khoản (UTC+7).", ["Ngày", "Tài khoản mới"],
                data.NewUsersByDay.Select(x => new object[] { Day(x.Date), x.Count }).ToArray()),
            new("Hoạt động gần đây", "6 bản ghi mới nhất tại thời điểm xuất; không lọc theo kỳ biểu đồ.",
                ["Thời gian UTC+7", "Hành động", "Tài nguyên", "Người thực hiện"],
                audit.Select(x => new object[] { x.OccurredOnUtc.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss"),
                    x.Action, $"{x.ResourceType} · {x.ResourceId}", x.ActorId?.ToString() ?? "Hệ thống" }).ToArray())]);

    public static DashboardReport Organizer(OrganizerDashboardDto data, OrganizerWalletDto wallet, RevenueTrendDto revenue)
    {
        var sections = new List<DashboardReportSection> {
            new("Tổng quan", Current, ["Chỉ số", "Số lượng"], [
                ["Giải của tôi (mọi trạng thái)", data.TotalEvents],
                ["Giải đang mở đăng ký", data.EventsByStatus.Where(x => x.Status == "registration-open").Sum(x => x.Count)],
                ["Đăng ký chờ duyệt", data.PendingRegistrations], ["Khiếu nại chờ xử lý", data.PendingComplaints]]),
            Grouped("Nhóm trạng thái giải", data.EventsByStatus, EventGroups),
            Statuses("Chi tiết trạng thái giải", data.EventsByStatus),
            new("Việc cần xử lý", Current + " Chỉ gồm đăng ký và khiếu nại Event đang chờ.",
                ["Giải", "Trạng thái", "Đăng ký chờ", "Khiếu nại chờ"],
                data.EventsWithPendingTasks.Select(x => new object[] { x.Name, Status(x.Status), x.PendingRegistrations, x.PendingComplaints }).ToArray()),
            new("Giải sắp diễn ra", "Tối đa 5 giải đã duyệt, mở hoặc đóng đăng ký, ngày tổ chức từ hôm nay (UTC+7).",
                ["Giải", "Ngày tổ chức", "Trạng thái"],
                data.UpcomingEvents.Select(x => new object[] { x.Name, Day(x.EventDate), Status(x.Status) }).ToArray()),
            new("Ví ban tổ chức", Current, ["Chỉ số", "Số tiền", "Tiền tệ"], [
                ["Có thể rút", wallet.AvailableBalance, wallet.Currency], ["Đang giữ ký quỹ", wallet.PendingEscrow, wallet.Currency],
                ["Đã rút", wallet.WithdrawnTotal, wallet.Currency]])
        };
        AddRevenue(sections, revenue);
        return new("Dashboard Organizer", "Phạm vi: các giải do tài khoản hiện tại sở hữu", revenue.From, revenue.To, DateTime.UtcNow, sections);
    }

    public static DashboardReport Event(EventDashboardDto data, RegistrationTrendDto trend, RevenueTrendDto revenue,
        EventLedgerDto finance, Guid? distanceId)
    {
        var selected = distanceId.HasValue ? trend.Distances.Single(x => x.DistanceId == distanceId) : null;
        var sections = new List<DashboardReportSection> {
            new("Tổng quan giải", Current, ["Chỉ số", "Số lượng"], [
                ["Tổng đơn đăng ký (mọi trạng thái)", data.TotalRegistrations],
                ["Đăng ký đã xác nhận (Confirmed + Finished)", data.CheckInSummary.Eligible],
                ["Đã check-in", data.CheckInSummary.CheckedIn], ["Khiếu nại chờ xử lý", data.PendingComplaints]]),
            Grouped("Nhóm đăng ký", data.RegistrationsByStatus, RegistrationGroups),
            Statuses("Chi tiết đăng ký", data.RegistrationsByStatus),
            Statuses("Tình hình thi đấu", data.RaceByStatus),
            new("Sức chứa theo cự ly", Current + " Chỗ đang giữ có thể gồm đơn chưa thanh toán.",
                ["Cự ly", "Sức chứa", "Chỗ đang giữ", "Đăng ký đã xác nhận"],
                data.Distances.Select(x => new object[] { x.Name, x.Capacity > 0 ? (object)x.Capacity : "Không giới hạn",
                    x.ReservedSlots, x.ConfirmedRegistrations }).ToArray()),
            new("Chuẩn bị giải", Current + " Các số này không kết luận giải đã đủ điều kiện vận hành.", ["Hạng mục", "Hiện trạng"], [
                ["Cự ly đã cấu hình BIB", $"{data.Preparation.DistancesWithBib} / {data.Preparation.TotalDistances}"],
                ["Cự ly có đường chạy", $"{data.Preparation.CoursesReady} / {data.Preparation.TotalDistances}"],
                ["Cự ly có checkpoint", data.Preparation.CoursesWithCheckpoints],
                ["Nhân sự đã phân trạm / đang hoạt động", $"{data.Preparation.AssignedStaff} / {data.Preparation.ActiveStaff}"],
                ["Tài liệu bắt buộc còn thiếu", data.Preparation.MissingDocumentKeys.Count == 0 ? "Không thiếu" : string.Join(", ", data.Preparation.MissingDocumentKeys.Select(Document))]]),
            new("Tài chính giải", Current, ["Chỉ số", "Giá trị", "Tiền tệ"], [
                ["Tiền vé theo sổ tài chính", finance.GrossRevenue, finance.Currency],
                ["Phí nền tảng", finance.PlatformFeeAmount, finance.Currency], ["Đã hoàn tiền", finance.RefundedTotal, finance.Currency],
                ["Có thể rút", finance.AvailableToWithdraw, finance.Currency]]),
            new("Đăng ký mới", "Trong kỳ biểu đồ, theo ngày tạo đơn (UTC+7), gồm đơn hủy/chưa thanh toán. Cự ly: " + (selected?.Name ?? "Tất cả cự ly"),
                ["Ngày", "Số đơn mới"], (selected?.Days ?? trend.Days).Select(x => new object[] { Day(x.Date), x.Count }).ToArray())
        };
        AddRevenue(sections, revenue);
        return new("Dashboard giải", $"Giải: {finance.EventName} | ID: {data.EventId}\nCự ly chỉ lọc biểu đồ đăng ký: {selected?.Name ?? "Tất cả cự ly"}",
            trend.From, trend.To, DateTime.UtcNow, sections);
    }

    private static DashboardReportSection Grouped(string title, IReadOnlyList<DashboardCount> items,
        (string Label, string[] Statuses)[] groups)
        => new(title, Current + " Các nhóm bằng 0 được ẩn; xem sheet chi tiết để biết mọi trạng thái.", ["Nhóm", "Số lượng"],
            groups.Select(g => new object[] { g.Label, items.Where(x => g.Statuses.Contains(x.Status)).Sum(x => x.Count) })
                .Where(x => (int)x[1] > 0).ToArray());

    private static DashboardReportSection Statuses(string title, IReadOnlyList<DashboardCount> items)
        => new(title, Current + (title == "Tình hình thi đấu" ? " Chỉ tính đăng ký Confirmed hoặc Finished." : ""),
            ["Trạng thái", "Số lượng"], items.Select(x => new object[] { Status(x.Status), x.Count }).ToArray());

    private static void AddRevenue(List<DashboardReportSection> sections, RevenueTrendDto revenue)
    {
        sections.Add(new("Dòng tiền trong kỳ", Cash, ["Tiền tệ", "Đã thu", "Đã hoàn", "Thu trừ hoàn"],
            revenue.Series.Select(x => new object[] { x.Currency, x.TotalCollected, x.TotalRefunded, x.NetCashFlow }).ToArray()));
        sections.Add(new("Dòng tiền theo ngày", Cash, ["Ngày", "Tiền tệ", "Đã thu", "Đã hoàn", "Thu trừ hoàn"],
            revenue.Series.SelectMany(s => s.Days.Select(d => new object[] { Day(d.Date), s.Currency, d.Collected, d.Refunded, d.NetCashFlow })).ToArray()));
    }

    private static string Day(DateOnly day) => day.ToString("dd/MM/yyyy");
    private static string Role(string role) => role.ToUpperInvariant() switch {
        "ADMIN" => "Quản trị viên", "MANAGER" => "Quản lý vận hành", "ORGANIZER" => "Ban tổ chức",
        "RUNNER" => "Vận động viên", "STAFF" => "Nhân viên", "USER" => "Người dùng", _ => role };
    private static string Document(string key) => key switch {
        "official-letter" => "Công văn tổ chức", "security" => "Phương án an ninh", _ => key };
    private static string Status(string status) => status switch {
        "draft" => "Bản nháp", "pending" or "pending_approval" => "Chờ duyệt", "approved" => "Đã duyệt",
        "rejected" => "Từ chối", "registration-open" => "Đang mở đăng ký", "closed" => "Đã đóng đăng ký",
        "completed" or "finished" => "Đã hoàn thành", "cancelled" => "Đã hủy", "suspended" => "Tạm đình chỉ",
        "pending_payment" => "Chờ thanh toán", "confirmed" => "Đã xác nhận", "not_started" => "Chưa xuất phát",
        "running" => "Đang chạy", "dns" => "Không xuất phát (DNS)", "dnf" => "Không hoàn thành (DNF)",
        "dq" => "Bị loại (DQ)", _ => status };
}
