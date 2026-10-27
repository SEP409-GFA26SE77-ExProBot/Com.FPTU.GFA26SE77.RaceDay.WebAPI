using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Shared.Infrastructure.Storage;

/// <summary>Dev/default storage under {ContentRoot}/wwwroot/uploads — no Cloudflare needed.</summary>
public sealed class LocalFileStorage(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<LocalFileStorage> logger) : IFileStorage
{
    public async Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var safeFolder = SanitizeSegment(folder);
        var safeName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var root = configuration["Storage:LocalRoot"]
            ?? Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
        var dir = Path.Combine(root, safeFolder);
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, safeName);
        await using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        var size = new FileInfo(fullPath).Length;
        var publicBase = configuration["Storage:PublicBaseUrl"]?.TrimEnd('/')
            ?? string.Empty;
        var relative = $"/uploads/{safeFolder}/{safeName}";
        var url = string.IsNullOrWhiteSpace(publicBase) ? relative : $"{publicBase}{relative}";

        logger.LogInformation("Stored local file {Path} ({Bytes} bytes)", fullPath, size);
        return new StoredFile(Path.GetFileName(fileName), url, contentType, size);
    }

    private static string SanitizeSegment(string value)
    {
        var cleaned = string.Join("-", (value ?? "misc").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "misc" : cleaned.ToLowerInvariant();
    }
}

/// <summary>
/// Cloudflare R2 via S3-compatible API. Configure Storage:Provider=R2 + R2 credentials.
/// Reuses one <see cref="AmazonS3Client"/> (DI singleton) — tránh tạo client mỗi PutObject.
/// </summary>
public sealed class CloudflareR2FileStorage : IFileStorage, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CloudflareR2FileStorage> _logger;
    private readonly Lazy<IAmazonS3> _client;
    private readonly string _bucket;
    private readonly string _publicBase;

    public CloudflareR2FileStorage(
        IConfiguration configuration,
        ILogger<CloudflareR2FileStorage> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _bucket = configuration["Storage:R2:Bucket"]
            ?? throw new InvalidOperationException("Storage:R2:Bucket missing.");
        _publicBase = configuration["Storage:PublicBaseUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Storage:PublicBaseUrl missing for R2.");
        _client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private IAmazonS3 CreateClient()
    {
        var accountId = _configuration["Storage:R2:AccountId"]
            ?? throw new InvalidOperationException("Storage:R2:AccountId missing.");
        var accessKey = _configuration["Storage:R2:AccessKeyId"]
            ?? throw new InvalidOperationException("Storage:R2:AccessKeyId missing.");
        var secretKey = _configuration["Storage:R2:SecretAccessKey"]
            ?? throw new InvalidOperationException("Storage:R2:SecretAccessKey missing.");

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        };
        return new AmazonS3Client(accessKey, secretKey, config);
    }

    public async Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "misc" : folder.Trim().ToLowerInvariant();
        var key = $"{safeFolder}/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";

        var put = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true
        };
        try
        {
            await _client.Value.PutObjectAsync(put, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R2 upload failed for {Key}", key);
            throw new InvalidOperationException($"Upload R2 thất bại: {ex.Message}", ex);
        }

        var url = $"{_publicBase}/{key}";
        _logger.LogInformation("Uploaded R2 object {Key}", key);
        return new StoredFile(Path.GetFileName(fileName), url, contentType, content.CanSeek ? content.Length : 0);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
