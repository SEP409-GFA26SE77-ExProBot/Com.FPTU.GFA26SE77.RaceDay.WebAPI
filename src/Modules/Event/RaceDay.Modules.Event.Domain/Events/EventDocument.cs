using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Events;

public sealed class EventDocument : Entity
{
    public Guid EventId { get; private set; }
    public string DocumentKey { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? FileName { get; private set; }
    public string? FileUrl { get; private set; }
    public bool Required { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }

    private EventDocument()
    {
    }

    public static EventDocument Create(
        Guid eventId,
        string documentKey,
        string title,
        string? fileName,
        string? fileUrl,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            throw new DomainException("DocumentKey không được để trống.");
        }

        return new EventDocument
        {
            EventId = eventId,
            DocumentKey = documentKey.Trim(),
            Title = string.IsNullOrWhiteSpace(title) ? documentKey.Trim() : title.Trim(),
            FileName = fileName?.Trim(),
            FileUrl = fileUrl?.Trim(),
            Required = required,
            UploadedAtUtc = DateTime.UtcNow
        };
    }
}
