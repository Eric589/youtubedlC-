using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YouTubeDownloader
{
    public class VideoFormat
    {
        public string FormatId { get; set; }
        public string Extension { get; set; }
        public string Resolution { get; set; }
        public string Note { get; set; }
        public long? Filesize { get; set; }
        public string Vcodec { get; set; }
        public string Acodec { get; set; }
    }

    public class Program
    {
        // Change this URL to the YouTube video you want to download
        private const string YOUTUBE_URL = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

        // Change this path to where you want to store downloaded files
        private const string DOWNLOAD_DIRECTORY = "/Path/To/Dir";

        private static readonly Stopwatch downloadTimer = new Stopwatch();

        private static readonly Regex progressRegex = new Regex(
            @"\[download\]\s*(?<percent>\d+(?:[.,]\d+)?)%\s*(?:of\s*)?" +
            @"(?:(?<downloaded>\d+(?:[.,]\d+)?\s*(?:[KMGT]iB|[KMGT]B|B))" +
            @"(?:\s*/\s*(?<total>\d+(?:[.,]\d+)?\s*(?:[KMGT]iB|[KMGT]B|B)))" +
            @"|(?<total_only>\d+(?:[.,]\d+)?\s*(?:[KMGT]iB|[KMGT]B|B)))?" +
            @".*?(?:at\s*(?<speed>\d+(?:[.,]\d+)?\s*(?:[KMGT]iB|[KMGT]B)\/s))?" +
            @".*?(?:ETA\s+(?<eta>\d{1,2}:\d{2}(?::\d{2})?))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static async Task Main(string[] args)
        {
            Console.WriteLine("YouTube Video Downloader");
            Console.WriteLine("========================");
            Console.WriteLine($"URL: {YOUTUBE_URL}");
            Console.WriteLine($"Download Directory: {DOWNLOAD_DIRECTORY}");
            Console.WriteLine();

            try
            {
                // Create download directory if it doesn't exist
                if (!Directory.Exists(DOWNLOAD_DIRECTORY))
                {
                    Directory.CreateDirectory(DOWNLOAD_DIRECTORY);
                    Console.WriteLine($"Created download directory: {DOWNLOAD_DIRECTORY}");
                }

                // Check if yt-dlp is available
                if (!await IsYtDlpAvailable())
                {
                    Console.WriteLine("Error: yt-dlp is not installed or not found in PATH.");
                    Console.WriteLine("Please install yt-dlp first:");
                    Console.WriteLine("- Windows: pip install yt-dlp");
                    Console.WriteLine("- Or download from: https://github.com/yt-dlp/yt-dlp/releases");
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                    return;
                }

                // Get available formats
                Console.WriteLine("Fetching available video formats...");
                var formats = await GetAvailableFormats(YOUTUBE_URL);

                if (formats.Count == 0)
                {
                    Console.WriteLine("No formats found or error occurred.");
                    return;
                }

                // Display formats
                DisplayFormats(formats);

                // Get user selection
                int selectedIndex = GetUserSelection(formats.Count);
                var selectedFormat = formats[selectedIndex - 1];

                // Download the selected format
                Console.WriteLine($"\nDownloading format: {selectedFormat.FormatId} ({selectedFormat.Resolution})");
                Console.WriteLine($"Saving to: {DOWNLOAD_DIRECTORY}");
                await DownloadVideo(YOUTUBE_URL, selectedFormat.FormatId, DOWNLOAD_DIRECTORY);

                Console.WriteLine("Download completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static async Task<bool> IsYtDlpAvailable()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<List<VideoFormat>> GetAvailableFormats(string url)
        {
            var formats = new List<VideoFormat>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = $"--list-formats --dump-json \"{url}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"yt-dlp error: {error}");
                }

                // Parse the JSON output
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("formats", out var formatsArray))
                        {
                            foreach (var format in formatsArray.EnumerateArray())
                            {
                                var videoFormat = new VideoFormat
                                {
                                    FormatId = format.GetProperty("format_id").GetString(),
                                    Extension = format.TryGetProperty("ext", out var ext) ? ext.GetString() : "unknown",
                                    Resolution = format.TryGetProperty("resolution", out var res) ? res.GetString() :
                                        format.TryGetProperty("height", out var height) ? $"{height.GetInt32()}p" :
                                        "audio only",
                                    Note = format.TryGetProperty("format_note", out var note) ? note.GetString() : "",
                                    Filesize = format.TryGetProperty("filesize", out var size) &&
                                               size.ValueKind != JsonValueKind.Null
                                        ? size.GetInt64()
                                        : null,
                                    Vcodec = format.TryGetProperty("vcodec", out var vcodec)
                                        ? vcodec.GetString()
                                        : "none",
                                    Acodec = format.TryGetProperty("acodec", out var acodec)
                                        ? acodec.GetString()
                                        : "none"
                                };

                                // Only add formats that are either video+audio or audio-only
                                if (videoFormat.Vcodec != "none" || videoFormat.Acodec != "none")
                                {
                                    formats.Add(videoFormat);
                                }
                            }

                            break; // Only process the first JSON object
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip invalid JSON lines
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting formats: {ex.Message}");
            }

            return formats;
        }

        private static void DisplayFormats(List<VideoFormat> formats)
        {
            Console.WriteLine("\nAvailable formats:");
            Console.WriteLine("==================");
            Console.WriteLine(
                $"{"#",-3} {"ID",-8} {"Extension",-10} {"Resolution",-15} {"Size",-12} {"Video",-10} {"Audio",-10} {"Note",-20}");
            Console.WriteLine(new string('-', 100));

            for (int i = 0; i < formats.Count; i++)
            {
                var format = formats[i];
                string sizeStr = format.Filesize.HasValue ? FormatBytes(format.Filesize.Value) : "Unknown";

                string videoCodec = GetCodecDisplay(format.Vcodec);
                string audioCodec = GetCodecDisplay(format.Acodec);

                Console.WriteLine(
                    $"{i + 1,-3} {format.FormatId,-8} {format.Extension,-10} {format.Resolution,-15} {sizeStr,-12} {videoCodec,-10} {audioCodec,-10} {format.Note,-20}");
            }
        }

        private static string GetCodecDisplay(string codec)
        {
            if (string.IsNullOrEmpty(codec) || codec == "none")
                return "❌";

            // Show checkmark for available codecs
            return "✅";
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F1} {suffixes[suffixIndex]}";
        }

        private static int GetUserSelection(int maxOptions)
        {
            while (true)
            {
                Console.Write($"\nSelect format to download (1-{maxOptions}): ");
                string input = Console.ReadLine();

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= maxOptions)
                {
                    return selection;
                }

                Console.WriteLine("Invalid selection. Please try again.");
            }
        }

        private static async Task DownloadVideo(string url, string formatId, string downloadDirectory)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments =
                            $"-f {formatId} -o \"{Path.Combine(downloadDirectory, "%(title)s.%(ext)s")}\" --newline \"{url}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                // Merge stderr into stdout so we get everything
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true;

                process.Start();

                var progressRegex =
                    new Regex(
                        @"\[download\]\s+(\d+(?:\.\d+)?)%.*?(\d+(?:\.\d+)?(?:[KMGT]iB|[KMGT]B|B))/(\d+(?:\.\d+)?(?:[KMGT]iB|[KMGT]B|B)).*?(\d+(?:\.\d+)?(?:[KMGT]iB|[KMGT]B)/s)");
                DateTime lastUpdate = DateTime.MinValue;

                // Read both stdout & stderr together
                var reader = Task.Run(async () =>
                {
                    using var combined = Console.OpenStandardOutput();
                    using var outputReader = process.StandardOutput;
                    using var errorReader = process.StandardError;

                    while (!process.HasExited)
                    {
                        while (!outputReader.EndOfStream)
                        {
                            var line = await outputReader.ReadLineAsync();
                            HandleProgressLine(line, ref lastUpdate);
                        }

                        while (!errorReader.EndOfStream)
                        {
                            var line = await errorReader.ReadLineAsync();
                            HandleProgressLine(line, ref lastUpdate);
                        }

                        await Task.Delay(20);
                    }
                });

                await process.WaitForExitAsync();
                await reader;

                Console.WriteLine(); // Move to next line after bar

                if (process.ExitCode != 0)
                {
                    throw new Exception("Download failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download error: {ex.Message}");
                throw;
            }
        }

       private static void HandleProgressLine(string line, ref DateTime lastUpdate)
{
    if (string.IsNullOrEmpty(line)) return;

    var match = progressRegex.Match(line);
    if (match.Success)
    {
        if (!downloadTimer.IsRunning)
            downloadTimer.Start();

        if ((DateTime.Now - lastUpdate).TotalMilliseconds < 100)
            return;

        // Parse %
        var percentStr = match.Groups["percent"].Value.Replace(",", ".");
        double.TryParse(percentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double percentage);

        // Total size
        string total = null;
        if (match.Groups["total"].Success)
            total = match.Groups["total"].Value.Trim();
        else if (match.Groups["total_only"].Success)
            total = match.Groups["total_only"].Value.Trim();

        // Downloaded
        string downloaded;
        if (match.Groups["downloaded"].Success)
            downloaded = match.Groups["downloaded"].Value.Trim();
        else if (!string.IsNullOrEmpty(total) && percentage > 0)
            downloaded = CalculateDownloadedSize(percentage, total);
        else
            downloaded = "?";

        if (string.IsNullOrEmpty(total))
            total = "?";

        // Speed (leave empty if not provided)
        string speed = match.Groups["speed"].Success ? match.Groups["speed"].Value.Trim() : "";

        // ETA — calculate if not provided
        string eta;
        if (percentage > 0 && percentage < 100)
        {
            var elapsed = downloadTimer.Elapsed;
            double remainingSeconds = (elapsed.TotalSeconds / (percentage / 100.0)) - elapsed.TotalSeconds;
            eta = FormatTime(TimeSpan.FromSeconds(remainingSeconds));
        }
        else
        {
            eta = "00:00";
        }

        string elapsedStr = FormatTime(downloadTimer.Elapsed);

        ShowProgressBar(percentage, downloaded, total, speed, eta, elapsedStr);
        lastUpdate = DateTime.Now;
    }
    else
    {
        Console.WriteLine(line);
    }
}

private static void ShowProgressBar(double percentage, string downloaded, string total, string speed, string eta, string elapsed)
{
    int barWidth = 40;
    int completed = (int)(percentage / 100.0 * barWidth);
    if (completed < 0) completed = 0;
    if (completed > barWidth) completed = barWidth;

    string progressBar = "[" +
        new string('=', completed) +
        (completed < barWidth ? ">" : "") +
        new string('-', Math.Max(0, barWidth - completed - (completed < barWidth ? 1 : 0))) +
        "]";

    string etaPart = !string.IsNullOrEmpty(eta) ? $" ETA:{eta}" : "";
    string progressText = $"{progressBar} {percentage:F1}% ({downloaded}/{total}) {speed}{etaPart} Elapsed:{elapsed}".Trim();

    int consoleWidth = 80;
    try { consoleWidth = Console.WindowWidth; } catch { }

    if (progressText.Length > consoleWidth - 1)
        progressText = progressText.Substring(0, Math.Max(0, consoleWidth - 4)) + "...";

    Console.Write("\r" + progressText.PadRight(consoleWidth - 1));
}

private static string FormatTime(TimeSpan time)
{
    return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
}

private static string CalculateDownloadedSize(double percentage, string total)
{
    // Example total: "227.22MiB"
    var match = Regex.Match(total, @"(?<value>\d+(?:[.,]\d+)?)(?<unit>[KMGT]?i?B)", RegexOptions.IgnoreCase);
    if (!match.Success) return $"{percentage:F1}%";

    double.TryParse(match.Groups["value"].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double totalValue);
    string unit = match.Groups["unit"].Value;

    double downloadedValue = (percentage / 100.0) * totalValue;
    return $"{downloadedValue:F2}{unit}";
}
    }
}