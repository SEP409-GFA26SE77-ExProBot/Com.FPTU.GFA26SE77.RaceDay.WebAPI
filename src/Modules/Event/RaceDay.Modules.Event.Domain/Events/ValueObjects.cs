using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Events;

public sealed class GeoPoint
{
    public double Lat { get; private set; }
    public double Lng { get; private set; }

    private GeoPoint()
    {
    }

    public GeoPoint(double lat, double lng)
    {
        Lat = lat;
        Lng = lng;
    }
}

public sealed class NamedGeoPoint
{
    public string Name { get; private set; } = string.Empty;
    public double Lat { get; private set; }
    public double Lng { get; private set; }

    private NamedGeoPoint()
    {
    }

    public NamedGeoPoint(string name, double lat, double lng)
    {
        Name = name?.Trim() ?? string.Empty;
        Lat = lat;
        Lng = lng;
    }
}

public sealed class OrganizerInfo
{
    public string Name { get; private set; } = string.Empty;
    public string? LegalEntity { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Website { get; private set; }
    public bool Verified { get; private set; }
    public string? AccountHolder { get; private set; }
    public string? AccountNumber { get; private set; }
    public string? BankName { get; private set; }
    public string? BankBranch { get; private set; }
    public string? SwiftCode { get; private set; }
    public string? TransferNote { get; private set; }

    private OrganizerInfo()
    {
    }

    public static OrganizerInfo Create(
        string name,
        string email,
        string phone,
        string? legalEntity = null,
        string? website = null,
        bool verified = false,
        string? accountHolder = null,
        string? accountNumber = null,
        string? bankName = null,
        string? bankBranch = null,
        string? swiftCode = null,
        string? transferNote = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tên ban tổ chức không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email ban tổ chức không được để trống.");
        }

        return new OrganizerInfo
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone?.Trim() ?? string.Empty,
            LegalEntity = legalEntity?.Trim(),
            Website = website?.Trim(),
            Verified = verified,
            AccountHolder = accountHolder?.Trim(),
            AccountNumber = accountNumber?.Trim(),
            BankName = bankName?.Trim(),
            BankBranch = bankBranch?.Trim(),
            SwiftCode = swiftCode?.Trim(),
            TransferNote = transferNote?.Trim()
        };
    }

    public void MarkVerified() => Verified = true;
}

public sealed class WeatherInfo
{
    public string Temp { get; private set; } = string.Empty;
    public string Condition { get; private set; } = string.Empty;
    public string? Humidity { get; private set; }

    private WeatherInfo()
    {
    }

    public WeatherInfo(string temp, string condition, string? humidity = null)
    {
        Temp = temp?.Trim() ?? string.Empty;
        Condition = condition?.Trim() ?? string.Empty;
        Humidity = humidity?.Trim();
    }
}

public sealed class PermitDocument
{
    public string? FileUrl { get; private set; }
    public string? FileName { get; private set; }
    public string? PermitNumber { get; private set; }
    public string? IssuingAuthority { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? ValidUntil { get; private set; }

    private PermitDocument()
    {
    }

    public static PermitDocument Create(
        string? fileUrl,
        string? fileName,
        string? permitNumber,
        string? issuingAuthority,
        DateOnly? issueDate,
        DateOnly? validUntil)
        => new()
        {
            FileUrl = fileUrl?.Trim(),
            FileName = fileName?.Trim(),
            PermitNumber = permitNumber?.Trim(),
            IssuingAuthority = issuingAuthority?.Trim(),
            IssueDate = issueDate,
            ValidUntil = validUntil
        };
}
