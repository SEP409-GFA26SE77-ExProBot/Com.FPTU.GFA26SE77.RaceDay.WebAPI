namespace RaceDay.Shared.Web.OpenApi;

/// <summary>
/// Swagger tag names. Prefix <c>01.</c>…<c>15.</c> (zero-padded) so UI lexicographic
/// order matches business flow — bare <c>1.</c> then <c>10.</c> sorts wrong.
/// </summary>
public static class ApiTags
{
    public const string IamAuth = "01. Auth — Đăng ký / Login";
    public const string IamAdmin = "02. IAM — Admin users";
    public const string EventPublic = "03. Event — Public (xem giải)";
    public const string EventOrganizer = "04. Event — Organizer (tạo & quản lý)";
    public const string EventRunner = "05. Event — Runner đăng ký";
    public const string EventManager = "06. Event — Manager duyệt";
    /// <summary>Legacy alias — prefer <see cref="EventManager"/>.</summary>
    public const string EventAdmin = EventManager;
    public const string AuditLog = "07. AuditLog";
    public const string Payment = "08. Payment";
    public const string Map = "09. Map — Course / route";
    public const string Finance = "10. Finance — Ví / giải ngân / hoàn";
    public const string Device = "11. Device — Wear link + SOS";
    public const string Photo = "12. Photo — Upload / OCR / search";
    public const string Host = "13. Host";
    public const string RaceMonitoring = "14. RaceMonitoring";
    public const string Simulation = "15. Simulation — Demo giả lập";

    public static IReadOnlyList<(string Name, string Description)> All { get; } =
    [
        (IamAuth, "Luồng: register → verify-email (OTP) → login → dán JWT vào Authorize. Seed: admin@ / manager@ — MatKhau@123"),
        (IamAdmin, "Quản lý user (Bearer Admin). Prefix /api/v1/iam/admin"),
        (EventPublic, "Danh sách / chi tiết giải đã public (Approved / RegistrationOpen). Không cần JWT."),
        (EventOrganizer, "Luồng tạo giải FE wizard: uploads hồ sơ → POST create (submitForApproval) → submit / open-registration / quản lý đăng ký."),
        (EventRunner, "VĐV: đăng ký → pending_payment → mine/detail. Cần JWT. Thanh toán ở nhóm Payment."),
        (EventManager, "Duyệt / từ chối hồ sơ giải + catalog gói. Bearer Manager (ManagerOnly). Prefix /api/v1/event/manager — không còn /event/admin."),
        (AuditLog, "Xem audit gần đây (Admin). Postgres schema audit — append-only."),
        (Payment, "PayOS checkout + webhook/mock. Luồng: create registration → checkout → PAID → pending_approval."),
        (Map, "Course map theo (eventId, distanceId): GET/PUT /api/v1/map/events/.../course; GET /geocode. Organizer/Admin/Manager đọc được (kể cả draft); PUT chỉ organizer/Admin."),
        (Finance, "Escrow theo giải. Manager (hoặc Admin): /finance/manager/payouts + system-error-refunds. Không còn /finance/admin/*."),
        (Device, "Wear Device Flow (RFC 8628): auth/code + auth/token; runner link/approve; SOS scope sos:create; organizer ack/resolve."),
        (Photo, "Photographer bulk-upload + OCR async (SEP_Service_Photo); runner search theo BIB; dispute. Auth: Active staff Photographer của event (JWT role không đủ)."),
        (Host, "Health / hạ tầng host."),
        (RaceMonitoring, "Phản hồi khẩn cấp theo giải. Prefix /api/v1/race-monitoring. JWT Admin | Organizer | Coordinator và quyền trên giải."),
        (Simulation, "Demo giả lập. Chỉ map khi Simulation__Enabled=true. Prefix /api/v1/simulation. Chủ giải hoặc Coordinator Active.")
    ];

    public static string SortKey(string tag) => tag switch
    {
        IamAuth => "01",
        IamAdmin => "02",
        EventPublic => "03",
        EventOrganizer => "04",
        EventRunner => "05",
        EventManager => "06",
        AuditLog => "07",
        Payment => "08",
        Map => "09",
        Finance => "10",
        Device => "11",
        Photo => "12",
        Host => "13",
        RaceMonitoring => "14",
        Simulation => "15",
        _ => "99"
    };
}
