using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace PortFlow.Shared.Attachment;

internal interface IObjectStorage
{
    Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken ct);

    Task DeleteAsync(string key);

    Task<Uri> PresignedGetAsync(string key, TimeSpan ttl);

    Task<bool> IsAvailableAsync(CancellationToken ct);
}

internal sealed class StorageException(string message, Exception inner) : Exception(message, inner);

internal sealed class MinioObjectStorage(IMinioClient client, string bucket, ILogger<MinioObjectStorage> log)
    : IObjectStorage
{
    private volatile bool _bucketReady;

    public async Task PutAsync(string key, Stream content, long size, string contentType, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        try
        {
            await client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(key)
                .WithStreamData(content)
                .WithObjectSize(size)
                .WithContentType(contentType), ct);
        }
        catch (Exception ex)
        {
            throw new StorageException($"Cannot store object {key}", ex);
        }
    }

    public async Task DeleteAsync(string key)
    {
        try
        {
            await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(key));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cannot delete orphan object {Key} – remove it manually", key);
        }
    }

    public async Task<Uri> PresignedGetAsync(string key, TimeSpan ttl)
    {
        try
        {
            var url = await client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(bucket)
                .WithObject(key)
                .WithExpiry((int)ttl.TotalSeconds));
            return new Uri(url);
        }
        catch (Exception ex)
        {
            throw new StorageException($"Cannot presign object {key}", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady)
        {
            return;
        }
        try
        {
            if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct))
            {
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
            }
            _bucketReady = true;
        }
        catch (Exception ex)
        {
            throw new StorageException($"Cannot prepare bucket {bucket}", ex);
        }
    }
}
