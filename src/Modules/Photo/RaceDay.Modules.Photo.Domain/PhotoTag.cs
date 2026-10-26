using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Domain;

public sealed class PhotoTag : Entity
{
    public Guid PhotoId { get; private set; }
    public string BibNormalized { get; private set; } = string.Empty;
    public double Confidence { get; private set; }
    public string? BboxJson { get; private set; }
    public PhotoTagSource Source { get; private set; }

    private PhotoTag()
    {
    }

    public static PhotoTag Create(
        Guid photoId,
        string bibNormalized,
        double confidence,
        PhotoTagSource source,
        string? bboxJson = null)
    {
        var bib = (bibNormalized ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(bib))
        {
            throw new DomainException("BIB tag không được để trống.");
        }

        if (confidence is < 0 or > 1)
        {
            throw new DomainException("Confidence phải trong [0,1].");
        }

        return new PhotoTag
        {
            PhotoId = photoId,
            BibNormalized = bib,
            Confidence = confidence,
            Source = source,
            BboxJson = bboxJson
        };
    }

    public void ReplaceBib(string bibNormalized, double confidence = 1.0)
    {
        var bib = (bibNormalized ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(bib))
        {
            throw new DomainException("BIB tag không được để trống.");
        }

        BibNormalized = bib;
        Confidence = confidence;
        Source = PhotoTagSource.Manual;
    }
}
