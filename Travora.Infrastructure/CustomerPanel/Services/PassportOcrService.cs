using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class PassportOcrService : IPassportOcrService
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;

    public PassportOcrService(IConfiguration configuration)
    {
        _pythonPath = configuration["Python:ExecutablePath"] ?? "python";
        _scriptPath = configuration["Python:ScriptPath"] ?? "Scripts/passport_ocr.py";
    }

    public async Task<PassportOcrResult> ExtractPassportDataAsync(string imagePath)
    {
        var scriptFullPath = Path.GetFullPath(_scriptPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{scriptFullPath}\" \"{imagePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,  
                StandardErrorEncoding = System.Text.Encoding.UTF8    
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Console.WriteLine($"OCR OUTPUT: {output}");
        Console.WriteLine($"OCR ERROR: {error}");
        Console.WriteLine($"EXIT CODE: {process.ExitCode}");
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return new PassportOcrResult
            {
                ValidScore = 0,
                Error = string.IsNullOrWhiteSpace(error) ? "OCR process failed" : error
            };
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            var result = JsonSerializer.Deserialize<PassportOcrResult>(output, options);
            return result ?? new PassportOcrResult { ValidScore = 0, Error = "Failed to parse OCR result" };
        }
        catch (JsonException ex)
        {
            return new PassportOcrResult { ValidScore = 0, Error = $"JSON parse error: {ex.Message}" };
        }
    }
}
