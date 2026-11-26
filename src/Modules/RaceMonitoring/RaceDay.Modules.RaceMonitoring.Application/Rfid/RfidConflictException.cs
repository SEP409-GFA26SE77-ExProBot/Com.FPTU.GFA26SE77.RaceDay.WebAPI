namespace RaceDay.Modules.RaceMonitoring.Application.Rfid;

/// <summary>Xung đột gán EPC hoặc yêu cầu đọc còn hoạt động. API trả 409.</summary>
public sealed class RfidConflictException(string message) : Exception(message);
