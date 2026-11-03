using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Infrastructure.Storage;

public sealed class PhotoContentFetcher(
    HttpClient http,
    IConfiguration configuration) : IPhotoContentFetcher
{
    public async Task<PhotoContentBytes> FetchAsync(
        string fileUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            throw new DomainException("Ảnh không có URL.");
        }

        var absolute = ResolveAbsoluteUrl(fileUrl.Trim());
        using var response = await http.GetAsync(
            absolute,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DomainException(
                $"Không tải được ảnh từ storage ({(int)response.StatusCode}).");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new DomainException("File ảnh trống.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? "application/octet-stream";
        return new PhotoContentBytes(bytes, contentType);
    }

    private string ResolveAbsoluteUrl(string fileUrl)
    {
        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            return abs.ToString();
        }

        var publicBase = configuration["Storage:PublicBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicBase))
        {
            throw new DomainException("Storage:PublicBaseUrl chưa cấu hình — không resolve URL ảnh.");
        }

        var path = fileUrl.StartsWith('/') ? fileUrl : "/" + fileUrl;
        return publicBase + path;
    }
}
