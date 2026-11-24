namespace RaceDay.Modules.RaceMonitoring.Application.TimingReads;

/// <summary>
/// Ánh xạ demo reader → checkpoint và tag → registration.
/// Reader vẫn nằm trong cấu hình. EPC mới nằm ở race_monitoring.rfid_tag_assignments;
/// Tags là mapping Phase 1 khi chưa có dòng DB.
/// </summary>
public sealed class RfidGatewayOptions
{
    public const string SectionName = "RfidGateway";

    public string ServiceKey { get; set; } = "";

    public List<RfidReaderBinding> Readers { get; set; } = [];

    public List<RfidTagBinding> Tags { get; set; } = [];
}

public sealed class RfidReaderBinding
{
    public string ReaderId { get; set; } = "";

    public Guid EventId { get; set; }

    public Guid CheckpointId { get; set; }
}

public sealed class RfidTagBinding
{
    public Guid EventId { get; set; }

    public string TagScheme { get; set; } = "";

    public string TagId { get; set; } = "";

    public Guid RegistrationId { get; set; }
}
