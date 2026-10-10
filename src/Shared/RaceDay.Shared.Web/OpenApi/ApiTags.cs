namespace RaceDay.Shared.Web.OpenApi;

/// <summary>Swagger tag names ordered by business flow (prefix sorts UI).</summary>
public static class ApiTags
{
    public const string IamAuth = "1. Auth — Đăng ký / Login";
    public const string IamAdmin = "2. IAM — Admin users";
    public const string EventPublic = "3. Event — Public (xem giải)";
    public const string EventOrganizer = "4. Event — Organizer (tạo & quản lý)";
    public const string EventRunner = "5. Event — Runner đăng ký";
    public const string EventAdmin = "6. Event — Admin duyệt";
    public const string AuditLog = "7. AuditLog";
    public const string Payment = "8. Payment";
    public const string Host = "9. Host";
    public const string RaceMonitoring = "10. RaceMonitoring";

    public static IReadOnlyList<(string Name, string Description)> All { get; } =
    [
        (IamAuth, "Luồng: register → verify-email (OTP) → login → dán JWT vào Authorize. Seed admin: admin@raceday.vn"),
        (IamAdmin, "Quản lý user (Bearer Admin). Prefix /api/v1/iam/admin"),
        (EventPublic, "Danh sách / chi tiết giải đã public (Approved / RegistrationOpen). Không cần JWT."),
        (EventOrganizer, "Luồng tạo giải FE wizard: uploads hồ sơ → POST create (submitForApproval) → submit / open-registration / quản lý đăng ký."),
        (EventRunner, "VĐV: đăng ký → pending_payment → mine/detail. Cần JWT. Thanh toán ở nhóm Payment."),
        (EventAdmin, "Duyệt / từ chối hồ sơ giải. Bearer Admin. Prefix /api/v1/event/admin"),
        (AuditLog, "Xem audit gần đây (Admin)."),
        (Payment, "PayOS checkout + webhook/mock. Luồng: create registration → checkout → PAID → pending_approval."),
        (Host, "Health / hạ tầng host."),
        (RaceMonitoring, "Giám sát giải (scaffold). Prefix /api/v1/race-monitoring")
    ];

    public static string SortKey(string tag) => tag switch
    {
        IamAuth => "1",
        IamAdmin => "2",
        EventPublic => "3",
        EventOrganizer => "4",
        EventRunner => "5",
        EventAdmin => "6",
        AuditLog => "7",
        Payment => "8",
        Host => "9",
        RaceMonitoring => "10",
        _ => "99"
    };
}
