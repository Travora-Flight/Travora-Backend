using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Travora.Application.Interfaces.External.FileStorage;
using Travora.Shared.Settings;

namespace Travora.Infrastructure.ExternalServices.FileStorage.Cloudinary;

public class CloudinaryService : ICloudinaryService
{
    private readonly CloudinaryDotNet.Cloudinary _cloudinary;

    public CloudinaryService(CloudinarySettings settings)
    {
        var account = new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret);
        _cloudinary = new CloudinaryDotNet.Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string folder)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        
        if (extension == ".txt" || extension == ".csv")
        {
            var rawParams = new RawUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true
            };

            var rawResult = await _cloudinary.UploadAsync(rawParams);

            if (rawResult.Error != null)
                throw new InvalidOperationException($"Cloudinary upload failed: {rawResult.Error.Message}");

            return rawResult.SecureUrl.ToString();
        }
        else
        {
            var imageParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var imageResult = await _cloudinary.UploadAsync(imageParams);

            if (imageResult.Error != null)
                throw new InvalidOperationException($"Cloudinary upload failed: {imageResult.Error.Message}");

            return imageResult.SecureUrl.ToString();
        }
    }

    public async Task<bool> DeleteFileAsync(string publicId)
    {
        var deleteParams = new DeletionParams(publicId);
        var result = await _cloudinary.DestroyAsync(deleteParams);
        return result.Result == "ok";
    }

    public string ExtractPublicId(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        // Cloudinary URL format: https://res.cloudinary.com/{cloud}/image/upload/v{version}/{public_id}.{ext}
        var uri = new Uri(url);
        var path = uri.AbsolutePath;

        // Remove /image/upload/v{version}/ prefix
        var uploadIndex = path.IndexOf("/upload/", StringComparison.Ordinal);
        if (uploadIndex < 0) return string.Empty;

        var afterUpload = path[(uploadIndex + 8)..]; // skip "/upload/"
        // Skip version if present (v12345678/)
        if (afterUpload.StartsWith('v') && afterUpload.Contains('/'))
        {
            afterUpload = afterUpload[(afterUpload.IndexOf('/') + 1)..];
        }

        // Remove file extension
        var lastDot = afterUpload.LastIndexOf('.');
        return lastDot > 0 ? afterUpload[..lastDot] : afterUpload;
    }
}
