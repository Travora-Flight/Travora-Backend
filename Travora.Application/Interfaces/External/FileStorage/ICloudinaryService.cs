namespace Travora.Application.Interfaces.External.FileStorage;

public interface ICloudinaryService
{
    /// <summary>
    /// Upload file to Cloudinary and return URL
    /// </summary>
    /// <param name="fileStream">File content</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="folder">Folder in Cloudinary (e.g., travora/employees/profiles)</param>
    /// <returns>Secure URL of the file</returns>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string folder);

    /// <summary>
    /// Delete file from Cloudinary
    /// </summary>
    /// <param name="publicId">The file's Public ID</param>
    Task<bool> DeleteFileAsync(string publicId);

    /// <summary>
    /// Extract Public ID from URL
    /// </summary>
    string ExtractPublicId(string url);
}
