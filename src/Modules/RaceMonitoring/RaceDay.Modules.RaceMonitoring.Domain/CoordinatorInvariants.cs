using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

internal static class CoordinatorInvariants
{
    public static void RequiredId(Guid id, string message)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException(message);
        }
    }

    public static void OptionalId(Guid? id, string message)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException(message);
        }
    }

    public static DateTime RequiredTimestamp(DateTime value, string message)
    {
        if (value == default)
        {
            throw new DomainException(message);
        }

        return value;
    }

    public static DateTime? OptionalTimestamp(DateTime? value, string message)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value == default)
        {
            throw new DomainException(message);
        }

        return value;
    }

    public static TEnum RequiredEnum<TEnum>(TEnum value, string message)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainException(message);
        }

        return value;
    }

    public static TEnum? OptionalEnum<TEnum>(TEnum? value, string message)
        where TEnum : struct, Enum
    {
        if (value is null)
        {
            return null;
        }

        if (!Enum.IsDefined(value.Value))
        {
            throw new DomainException(message);
        }

        return value;
    }

    public static string RequiredText(string? value, int maxLength, string emptyMessage, string lengthMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(emptyMessage);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException(lengthMessage);
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, int maxLength, string lengthMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException(lengthMessage);
        }

        return trimmed;
    }

    public static string RequiredLongText(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(emptyMessage);
        }

        return value.Trim();
    }

    public static string? OptionalLongText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
