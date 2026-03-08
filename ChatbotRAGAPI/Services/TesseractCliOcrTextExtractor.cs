using System.Diagnostics;
using System.Globalization;
using ChatbotRAGAPI.Options;
using ChatbotRAGAPI.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatbotRAGAPI.Services;

public sealed class TesseractCliOcrTextExtractor : IOcrTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".pdf"
    ];

    private readonly RagOptions _options;

    public TesseractCliOcrTextExtractor(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured => _options.Ocr.IsConfigured;

    public async Task<string?> ExtractAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!SupportedExtensions.Contains(extension))
        {
            return null;
        }

        var workingDirectory = string.IsNullOrWhiteSpace(_options.Ocr.TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "ChatbotRAGAPI", "ocr")
            : _options.Ocr.TempDirectory;

        Directory.CreateDirectory(workingDirectory);

        var inputPath = Path.Combine(workingDirectory, $"{Guid.NewGuid():N}{extension}");
        var outputBase = Path.Combine(workingDirectory, $"{Guid.NewGuid():N}");
        var outputPath = outputBase + ".txt";

        try
        {
            await using (var sourceStream = file.OpenReadStream())
            await using (var targetStream = File.Create(inputPath))
            {
                await sourceStream.CopyToAsync(targetStream, cancellationToken);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.Ocr.TimeoutSeconds));

            using var process = StartProcess(inputPath, outputBase);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(outputPath, timeoutCts.Token);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private Process StartProcess(string inputPath, string outputBase)
    {
        var executablePath = string.IsNullOrWhiteSpace(_options.Ocr.ExecutablePath)
            ? "tesseract"
            : _options.Ocr.ExecutablePath;

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add(outputBase);
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(_options.Ocr.Language);
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(_options.Ocr.PageSegmentationMode.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("txt");

        if (!string.IsNullOrWhiteSpace(_options.Ocr.DataPath))
        {
            startInfo.Environment["TESSDATA_PREFIX"] = _options.Ocr.DataPath;
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the configured OCR executable.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
