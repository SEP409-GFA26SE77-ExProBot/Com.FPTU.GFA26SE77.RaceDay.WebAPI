using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Events;

public sealed class EventCheckpoint : Entity
{
    public Guid DistanceId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public double Km { get; private set; }
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public CheckpointType Type { get; private set; }
    public string? Cutoff { get; private set; }
    public int? Elevation { get; private set; }
    public int SortOrder { get; private set; }

    private EventCheckpoint()
    {
    }

    public static EventCheckpoint Create(
        Guid distanceId,
        string externalId,
        string name,
        double km,
        double lat,
        double lng,
        CheckpointType type,
        string? cutoff,
        int? elevation,
        int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tên checkpoint không được để trống.");
        }

        return new EventCheckpoint
        {
            DistanceId = distanceId,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? Guid.NewGuid().ToString("N")[..12] : externalId.Trim(),
            Name = name.Trim(),
            Km = km,
            Lat = lat,
            Lng = lng,
            Type = type,
            Cutoff = cutoff?.Trim(),
            Elevation = elevation,
            SortOrder = sortOrder
        };
    }
}

public sealed class EventDistance : Entity
{
    private readonly List<EventCheckpoint> _checkpoints = [];

    public Guid EventId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public double DistanceKm { get; private set; }
    public double ElevationGainM { get; private set; }
    public double MaxElevationM { get; private set; }
    public double MinElevationM { get; private set; }
    public int Capacity { get; private set; }
    public int RegisteredCount { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string Color { get; private set; } = "#FC4C02";
    /// <summary>Giờ xuất phát cự ly (ngày giải).</summary>
    public TimeOnly? StartAt { get; private set; }
    /// <summary>Giờ kết thúc / cutoff cự ly (ngày giải).</summary>
    public TimeOnly? EndAt { get; private set; }
    public NamedGeoPoint StartLocation { get; private set; } = new("Start", 0, 0);
    public NamedGeoPoint FinishLocation { get; private set; } = new("Finish", 0, 0);
    /// <summary>JSON arrays: osrmWaypoints, leafletPolyline, geoJson — FE map payload.</summary>
    public string RouteGeometryJson { get; private set; } = "{}";

    public IReadOnlyCollection<EventCheckpoint> Checkpoints => _checkpoints.AsReadOnly();

    private EventDistance()
    {
    }

    public static EventDistance Create(
        Guid eventId,
        string type,
        string name,
        double distanceKm,
        double elevationGainM,
        double maxElevationM,
        double minElevationM,
        int capacity,
        decimal price,
        string? currency,
        string? color,
        NamedGeoPoint start,
        NamedGeoPoint finish,
        string? routeGeometryJson,
        IReadOnlyList<(string ExternalId, string Name, double Km, double Lat, double Lng, CheckpointType Type, string? Cutoff, int? Elevation)>? checkpoints = null,
        TimeOnly? startAt = null,
        TimeOnly? endAt = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new DomainException("Loại cự ly (type) không được để trống.");
        }

        if (distanceKm <= 0)
        {
            throw new DomainException("Quãng đường phải lớn hơn 0.");
        }

        if (capacity < 0)
        {
            throw new DomainException("Sức chứa cự ly không hợp lệ.");
        }

        var distance = new EventDistance
        {
            EventId = eventId,
            Type = type.Trim(),
            Name = string.IsNullOrWhiteSpace(name) ? type.Trim() : name.Trim(),
            DistanceKm = distanceKm,
            ElevationGainM = elevationGainM,
            MaxElevationM = maxElevationM,
            MinElevationM = minElevationM,
            Capacity = capacity,
            RegisteredCount = 0,
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            Color = string.IsNullOrWhiteSpace(color) ? "#FC4C02" : color.Trim(),
            StartAt = startAt,
            EndAt = endAt,
            StartLocation = start,
            FinishLocation = finish,
            RouteGeometryJson = string.IsNullOrWhiteSpace(routeGeometryJson) ? "{}" : routeGeometryJson
        };

        if (checkpoints is not null)
        {
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var c = checkpoints[i];
                distance._checkpoints.Add(EventCheckpoint.Create(
                    distance.Id,
                    c.ExternalId,
                    c.Name,
                    c.Km,
                    c.Lat,
                    c.Lng,
                    c.Type,
                    c.Cutoff,
                    c.Elevation,
                    i));
            }
        }

        return distance;
    }

    /// <summary>Cập nhật thuộc tính cự ly — giữ Id + RegisteredCount (map/staff/reg không gãy).</summary>
    public void UpdateDetails(
        string type,
        string name,
        double distanceKm,
        double elevationGainM,
        double maxElevationM,
        double minElevationM,
        int capacity,
        decimal price,
        string? currency,
        string? color,
        NamedGeoPoint start,
        NamedGeoPoint finish,
        TimeOnly? startAt,
        TimeOnly? endAt)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new DomainException("Loại cự ly (type) không được để trống.");
        }

        if (distanceKm <= 0)
        {
            throw new DomainException("Quãng đường phải lớn hơn 0.");
        }

        if (capacity < 0)
        {
            throw new DomainException("Sức chứa cự ly không hợp lệ.");
        }

        if (capacity < RegisteredCount)
        {
            throw new DomainException(
                $"Sức chứa ({capacity}) không được nhỏ hơn số VĐV đã đăng ký ({RegisteredCount}).");
        }

        if (startAt.HasValue && endAt.HasValue && endAt.Value < startAt.Value)
        {
            throw new DomainException("Giờ kết thúc cự ly phải ≥ giờ xuất phát.");
        }

        Type = type.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? type.Trim() : name.Trim();
        DistanceKm = distanceKm;
        ElevationGainM = elevationGainM;
        MaxElevationM = maxElevationM;
        MinElevationM = minElevationM;
        Capacity = capacity;
        Price = price;
        Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant();
        Color = string.IsNullOrWhiteSpace(color) ? "#FC4C02" : color.Trim();
        StartAt = startAt;
        EndAt = endAt;
        StartLocation = start;
        FinishLocation = finish;
    }

    public void IncrementRegistered()
    {
        if (RegisteredCount >= Capacity && Capacity > 0)
        {
            throw new DomainException($"Cự ly {Type} đã hết slot.");
        }

        RegisteredCount++;
    }

    public void DecrementRegistered()
    {
        if (RegisteredCount > 0)
        {
            RegisteredCount--;
        }
    }

    public bool HasAvailableSlot => Capacity <= 0 || RegisteredCount < Capacity;
}
