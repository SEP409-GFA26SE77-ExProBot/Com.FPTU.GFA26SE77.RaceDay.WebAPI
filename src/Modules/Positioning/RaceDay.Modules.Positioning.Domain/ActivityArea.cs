namespace RaceDay.Modules.Positioning.Domain;

public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public static class ActivityArea
{
    public const double CheckpointRadiusMeters = 500;
    public const double CourseRadiusMeters = 100;
    private const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceToCheckpoint(GeoPoint position, GeoPoint checkpoint)
    {
        var latitude = Radians(checkpoint.Latitude - position.Latitude);
        var longitude = Radians(checkpoint.Longitude - position.Longitude);
        var haversine = Math.Pow(Math.Sin(latitude / 2), 2)
            + Math.Cos(Radians(position.Latitude)) * Math.Cos(Radians(checkpoint.Latitude))
            * Math.Pow(Math.Sin(longitude / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
    }

    public static double DistanceToCourse(GeoPoint position, IReadOnlyList<GeoPoint> course)
    {
        var nearest = double.PositiveInfinity;
        var longitudeScale = EarthRadiusMeters * Math.Cos(Radians(position.Latitude));
        for (var i = 1; i < course.Count; i++)
        {
            var start = course[i - 1];
            var end = course[i];
            if (!start.IsValid || !end.IsValid) continue;

            var x = Radians(Math.IEEERemainder(start.Longitude - position.Longitude, 360)) * longitudeScale;
            var y = Radians(start.Latitude - position.Latitude) * EarthRadiusMeters;
            var dx = Radians(Math.IEEERemainder(end.Longitude - start.Longitude, 360)) * longitudeScale;
            var dy = Radians(end.Latitude - start.Latitude) * EarthRadiusMeters;
            var lengthSquared = dx * dx + dy * dy;
            var fraction = lengthSquared == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / lengthSquared, 0, 1);
            var closestX = x + fraction * dx;
            var closestY = y + fraction * dy;
            nearest = Math.Min(nearest, Math.Sqrt(closestX * closestX + closestY * closestY));
        }
        return nearest;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
