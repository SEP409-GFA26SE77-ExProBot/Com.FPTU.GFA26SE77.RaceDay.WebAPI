using System.Text.RegularExpressions;

namespace RaceDay.Shared.Application.Validation;

/// <summary>Shared format / range checks for API inputs (phone, dates, geo, …).</summary>
public static partial class InputRules
{
    public const int MinRunnerAgeYears = 5;
    public const int MaxPersonAgeYears = 100;
    public const int MaxEventYearsAhead = 5;
    public const int MaxEventDaysPast = 30;

    public const int MinHeightCm = 100;
    public const int MaxHeightCm = 250;
    public const int MinWeightKg = 25;
    public const int MaxWeightKg = 250;

    /// <summary>VN mobile: 03/05/07/08/09 + 8 digits, or +84 then same.</summary>
    [GeneratedRegex(@"^(0|\+84)(3|5|7|8|9)\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex VietnamMobileRegex();

    /// <summary>CCCD/CMND: 9 or 12 digits.</summary>
    [GeneratedRegex(@"^\d{9}$|^\d{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex VietnamIdNumberRegex();

    [GeneratedRegex(@"^(A|B|AB|O)[+-]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BloodTypeRegex();

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var chars = phone.Trim().Where(c => c is not (' ' or '-' or '.' or '(' or ')')).ToArray();
        return new string(chars);
    }

    public static bool IsValidVietnamPhone(string? phone)
    {
        var normalized = NormalizePhone(phone);
        return normalized.Length > 0 && VietnamMobileRegex().IsMatch(normalized);
    }

    public static bool IsValidOptionalVietnamPhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) || IsValidVietnamPhone(phone);

    public static bool IsValidDateOfBirth(DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth is null)
        {
            return true;
        }

        var dob = dateOfBirth.Value;
        if (dob > today)
        {
            return false;
        }

        var oldest = today.AddYears(-MaxPersonAgeYears);
        var youngest = today.AddYears(-MinRunnerAgeYears);
        return dob >= oldest && dob <= youngest;
    }

    public static bool IsValidEventDate(DateOnly eventDate, DateOnly today)
    {
        var min = today.AddDays(-MaxEventDaysPast);
        var max = today.AddYears(MaxEventYearsAhead);
        return eventDate >= min && eventDate <= max;
    }

    public static bool IsValidLatitude(double lat) => lat is >= -90 and <= 90;

    public static bool IsValidLongitude(double lng) => lng is >= -180 and <= 180;

    public static bool IsValidOptionalIdNumber(string? idNumber)
        => string.IsNullOrWhiteSpace(idNumber) || VietnamIdNumberRegex().IsMatch(idNumber.Trim());

    public static bool IsValidOptionalBloodType(string? bloodType)
        => string.IsNullOrWhiteSpace(bloodType) || BloodTypeRegex().IsMatch(bloodType.Trim());

    public static bool IsValidOptionalGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
        {
            return true;
        }

        return gender.Trim().ToLowerInvariant() is "male" or "female" or "other"
            or "nam" or "nữ" or "nu";
    }

    public static bool IsValidOptionalHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        var value = url.Trim();
        if (value.StartsWith('/'))
        {
            return value.Length > 1 && value.Length <= 500;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && value.Length <= 500;
    }
}
