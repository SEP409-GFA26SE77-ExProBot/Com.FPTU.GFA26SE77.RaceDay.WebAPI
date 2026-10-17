namespace RaceDay.Shared.Application.Abstractions;

public interface IFileStorage
{
    /// <summary>Stores a file and returns a public/relative URL plus the stored file name.</summary>
    Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);
}

public sealed record StoredFile(string FileName, string FileUrl, string ContentType, long SizeBytes);
