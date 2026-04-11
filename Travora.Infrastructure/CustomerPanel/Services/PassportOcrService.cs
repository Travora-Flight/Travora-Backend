using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Travora.Application.DTOs.Customer.Auth;
using Travora.Application.Interfaces.Services.Customer;

namespace Travora.Infrastructure.CustomerPanel.Services;

public class PassportOcrService : IPassportOcrService
{
    private readonly string? _ocrApiUrl;
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private readonly IHttpClientFactory _httpClientFactory;

    public PassportOcrService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _ocrApiUrl = configuration["Python:OcrApiUrl"];
        _pythonPath = configuration["Python:ExecutablePath"] ?? "python";
        _scriptPath = configuration["Python:ScriptPath"] ?? "Scripts/passport_ocr.py";
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PassportOcrResult> ExtractPassportDataAsync(string imagePath)
    {
        if (!string.IsNullOrEmpty(_ocrApiUrl))
        {
            Console.WriteLine($"[OCR] 🌐 Calling Remote API: {_ocrApiUrl}");
            return await CallOcrApiAsync(imagePath);
        }

        Console.WriteLine("[OCR] 🏠 Running Local Python Script");
        return await RunLocalScriptAsync(imagePath);
    }

    private async Task<PassportOcrResult> CallOcrApiAsync(string imagePath)
    {
        var client = _httpClientFactory.CreateClient();
        await using var stream = File.OpenRead(imagePath);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(stream), "image", Path.GetFileName(imagePath));

        var response = await client.PostAsync(_ocrApiUrl, form);
        var json = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        return JsonSerializer.Deserialize<PassportOcrResult>(json, options)
            ?? new PassportOcrResult { ValidScore = 0, Error = "Failed to parse" };
    }

    private async Task<PassportOcrResult> RunLocalScriptAsync(string imagePath)
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
