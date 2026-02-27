namespace Travora.Application.Interfaces.External.FileStorage;

public interface ICloudinaryService
{
    /// <summary>
    /// رفع ملف على Cloudinary وإرجاع الـ URL
    /// </summary>
    /// <param name="fileStream">محتوى الملف</param>
    /// <param name="fileName">اسم الملف الأصلي</param>
    /// <param name="folder">المجلد في Cloudinary (مثل: travora/employees/profiles)</param>
    /// <returns>الـ URL الآمن للملف</returns>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string folder);

    /// <summary>
    /// حذف ملف من Cloudinary
    /// </summary>
    /// <param name="publicId">الـ Public ID بتاع الملف</param>
    Task<bool> DeleteFileAsync(string publicId);

    /// <summary>
    /// استخراج الـ Public ID من الـ URL
    /// </summary>
    string ExtractPublicId(string url);
}
