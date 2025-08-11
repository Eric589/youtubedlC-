using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
        public bool IsVideoOnly => Vcodec != "none" && Acodec == "none";
        public bool IsAudioOnly => Vcodec == "none" && Acodec != "none";
    }

    public class Program
    {
        private static string youtubeUrl;
        private static string downloadDirectory = Environment.CurrentDirectory;
        private static string outputFilename;

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
            Console.WriteLine("YouTube Video Downloader with Auto-Merge");
            Console.WriteLine("========================================");

            try
            {
                if (!ParseArguments(args))
                {
                    ShowUsage();
                    return;
                }

                Console.WriteLine($"URL: {youtubeUrl}");
                Console.WriteLine($"Download Directory: {downloadDirectory}");
                Console.WriteLine($"Output Filename: {outputFilename ?? "Auto (from video title)"}");
                Console.WriteLine();

                // Create download directory if it doesn't exist
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                    Console.WriteLine($"Created download directory: {downloadDirectory}");
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
                var allFormats = await GetAvailableFormats(youtubeUrl);

                if (allFormats.Count == 0)
                {
                    Console.WriteLine("No formats found or error occurred.");
                    return;
                }

                // Separate video and audio formats (skip combined formats)
                var videoFormats = allFormats.Where(f => f.IsVideoOnly).ToList();
                var audioFormats = allFormats
                    .Where(f => f.IsAudioOnly && string.Equals(f.Extension, "webm", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Get video title for default filename
                if (string.IsNullOrEmpty(outputFilename))
                {
                    outputFilename = await GetVideoTitle(youtubeUrl);
                    outputFilename = SanitizeFilename(outputFilename);
                    Console.WriteLine($"Using video title as filename: {outputFilename}");
                }

                // User selections
                string selectedVideoFormat = null;
                string selectedAudioFormat = null;
                string selectedVideoExtension = null;

                // Video selection
                if (videoFormats.Any())
                {
                    Console.WriteLine("\n=== VIDEO SELECTION ===");
                    DisplayVideoFormats(videoFormats);
                    Console.WriteLine($"{videoFormats.Count + 1}. Skip video (audio only)");

                    int videoSelection = GetUserSelection(videoFormats.Count + 1, "video format");
                    if (videoSelection <= videoFormats.Count)
                    {
                        var selectedVideoFormatObj = videoFormats[videoSelection - 1];
                        selectedVideoFormat = selectedVideoFormatObj.FormatId;
                        selectedVideoExtension = selectedVideoFormatObj.Extension;
                        Console.WriteLine($"Selected video format: {selectedVideoFormat} ({selectedVideoExtension})");
                    }
                    else
                    {
                        Console.WriteLine("Skipping video download (audio only)");
                    }
                }
                else
                {
                    Console.WriteLine("No video-only formats available.");
                }

                // Audio selection
                if (audioFormats.Any())
                {
                    Console.WriteLine("\n=== AUDIO SELECTION ===");
                    DisplayAudioFormats(audioFormats);
                    Console.WriteLine($"{audioFormats.Count + 1}. Skip audio (video only)");

                    int audioSelection = GetUserSelection(audioFormats.Count + 1, "audio format");
                    if (audioSelection <= audioFormats.Count)
                    {
                        selectedAudioFormat = audioFormats[audioSelection - 1].FormatId;
                        Console.WriteLine($"Selected audio format: {selectedAudioFormat}");
                    }
                    else
                    {
                        Console.WriteLine("Skipping audio download (video only)");
                    }
                }
                else
                {
                    Console.WriteLine("No audio-only formats available.");
                }

                if (selectedVideoFormat == null && selectedAudioFormat == null)
                {
                    Console.WriteLine("No formats selected. Exiting.");
                    return;
                }

                // Create temporary download folder
                string tempFolder = Path.Combine(downloadDirectory, "temp_download");
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);

                try
                {
                    // Download selected formats
                    string videoFile = null;
                    string audioFile = null;

                    if (selectedVideoFormat != null)
                    {
                        Console.WriteLine($"\n=== Downloading Video Format: {selectedVideoFormat} ===");
                        videoFile = await DownloadFormat(youtubeUrl, selectedVideoFormat, tempFolder, outputFilename,
                            "video");
                    }

                    if (selectedAudioFormat != null)
                    {
                        Console.WriteLine($"\n=== Downloading Audio Format: {selectedAudioFormat} ===");
                        audioFile = await DownloadFormat(youtubeUrl, selectedAudioFormat, tempFolder, outputFilename,
                            "audio");
                    }

                    // Handle final output
                    string finalOutputPath;

                    if (videoFile != null && audioFile != null)
                    {
                        // Merge video and audio - use video's extension to avoid re-encoding
                        string outputExtension = selectedVideoExtension ?? "mp4";
                        finalOutputPath = Path.Combine(downloadDirectory, outputFilename + "." + outputExtension);

                        Console.WriteLine("\n=== Merging Video and Audio ===");
                        Console.WriteLine($"Output will be saved as: {outputExtension} (matching video format)");
                        await MergeVideoAudio(videoFile, audioFile, finalOutputPath);
                        Console.WriteLine($"Merged file created: {finalOutputPath}");
                    }
                    else if (videoFile != null)
                    {
                        // Video only - copy to final location
                        finalOutputPath = Path.Combine(downloadDirectory,
                            outputFilename + Path.GetExtension(videoFile));
                        File.Move(videoFile, finalOutputPath);
                        Console.WriteLine($"Video-only file created: {finalOutputPath}");
                    }
                    else if (audioFile != null)
                    {
                        // Audio only - copy to final location  
                        finalOutputPath = Path.Combine(downloadDirectory,
                            outputFilename + Path.GetExtension(audioFile));
                        File.Move(audioFile, finalOutputPath);
                        Console.WriteLine($"Audio-only file created: {finalOutputPath}");
                    }
                }
                finally
                {
                    // Clean up temp folder
                    if (Directory.Exists(tempFolder))
                    {
                        Directory.Delete(tempFolder, true);
                        Console.WriteLine("Temporary files cleaned up.");
                    }
                }

                Console.WriteLine("Process completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static bool ParseArguments(string[] args)
        {
            if (args.Length == 0)
                return false;

            youtubeUrl = args[0];
            if (!IsValidYouTubeUrl(youtubeUrl))
                return false;

            for (int i = 1; i < args.Length - 1; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-d":
                        if (i + 1 < args.Length)
                            downloadDirectory = args[++i];
                        break;
                    case "-f":
                        if (i + 1 < args.Length)
                            outputFilename = args[++i];
                        break;
                }
            }

            return true;
        }

        private static bool IsValidYouTubeUrl(string url)
        {
            return url.Contains("youtube.com/watch") || url.Contains("youtu.be/");
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: Program.exe <youtube_url> [-d directory] [-f filename]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  youtube_url    YouTube video URL (required)");
            Console.WriteLine("  -d directory   Download directory (optional, default: current directory)");
            Console.WriteLine("  -f filename    Output filename without extension (optional, default: video title)");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  Program.exe \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\"");
            Console.WriteLine(
                "  Program.exe \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\" -d \"C:\\Downloads\" -f \"my_video\"");
        }

        private static string SanitizeFilename(string filename)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                filename = filename.Replace(c, '_');
            }

            return filename.Trim();
        }

        private static async Task<string> GetVideoTitle(string url)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "yt-dlp",
                        Arguments = $"--get-title \"{url}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string title = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 ? title.Trim() : "downloaded_video";
            }
            catch
            {
                return "downloaded_video";
            }
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

                                // Only add formats that are either video+audio, video-only, or audio-only
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

        private static void DisplayVideoFormats(List<VideoFormat> formats)
        {
            Console.WriteLine("\nAvailable video-only formats:");
            Console.WriteLine("============================");
            Console.WriteLine($"{"#",-3} {"ID",-8} {"Extension",-10} {"Resolution",-15} {"Size",-12} {"Note",-20}");
            Console.WriteLine(new string('-', 80));

            for (int i = 0; i < formats.Count; i++)
            {
                var format = formats[i];
                string sizeStr = format.Filesize.HasValue ? FormatBytes(format.Filesize.Value) : "Unknown";

                Console.WriteLine(
                    $"{i + 1,-3} {format.FormatId,-8} {format.Extension,-10} {format.Resolution,-15} {sizeStr,-12} {format.Note,-20}");
            }
        }

        private static void DisplayAudioFormats(List<VideoFormat> formats)
        {
            Console.WriteLine("\nAvailable audio-only formats:");
            Console.WriteLine("============================");
            Console.WriteLine($"{"#",-3} {"ID",-8} {"Extension",-10} {"Quality",-15} {"Size",-12} {"Note",-20}");
            Console.WriteLine(new string('-', 80));

            for (int i = 0; i < formats.Count; i++)
            {
                var format = formats[i];
                string sizeStr = format.Filesize.HasValue ? FormatBytes(format.Filesize.Value) : "Unknown";
                string quality = format.Note.Contains("kbps") ? format.Note : "Unknown quality";

                Console.WriteLine(
                    $"{i + 1,-3} {format.FormatId,-8} {format.Extension,-10} {quality,-15} {sizeStr,-12} {format.Note,-20}");
            }
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

        private static int GetUserSelection(int maxOptions, string selectionType)
        {
            while (true)
            {
                Console.Write($"\nSelect {selectionType} to download (1-{maxOptions}): ");
                string input = Console.ReadLine();

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= maxOptions)
                {
                    return selection;
                }

                Console.WriteLine("Invalid selection. Please try again.");
            }
        }

        private static async Task<string> DownloadFormat(string url, string formatId, string directory, string filename,
            string type)
        {
            string outputTemplate = Path.Combine(directory, filename + "_" + type + ".%(ext)s");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = $"-f {formatId} -o \"{outputTemplate}\" --newline \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            await RunDownloadProcess(process);

            // Find the downloaded file
            string[] possibleExtensions = { ".mp4", ".webm", ".mkv", ".flv", ".avi", ".mp3", ".m4a", ".webm", ".ogg" };
            foreach (string ext in possibleExtensions)
            {
                string possiblePath = Path.Combine(directory, filename + "_" + type + ext);
                if (File.Exists(possiblePath))
                    return possiblePath;
            }

            throw new FileNotFoundException($"Downloaded {type} file not found");
        }

        private static async Task RunDownloadProcess(Process process)
        {
            DateTime lastUpdate = DateTime.MinValue;
            downloadTimer.Restart();

            process.Start();

            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    HandleProgressLine(line, ref lastUpdate);
                }
            });

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    HandleProgressLine(line, ref lastUpdate);
                }
            });

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);

            Console.WriteLine(); // Move to next line after progress bar

            if (process.ExitCode != 0)
            {
                throw new Exception("Download failed");
            }
        }

        private static async Task MergeVideoAudio(string videoFile, string audioFile, string outputFile)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{videoFile}\" -i \"{audioFile}\" -c copy -map 0:v:0 -map 1:a:0 \"{outputFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) Console.WriteLine(e.Data);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) Console.WriteLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception("Video/Audio merge failed");
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

                // Speed
                string speed = match.Groups["speed"].Success ? match.Groups["speed"].Value.Trim() : "";

                // ETA
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

        private static void ShowProgressBar(double percentage, string downloaded, string total, string speed,
            string eta, string elapsed)
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
            string progressText =
                $"{progressBar} {percentage:F1}% ({downloaded}/{total}) {speed}{etaPart} Elapsed:{elapsed}".Trim();

            int consoleWidth = 80;
            try
            {
                consoleWidth = Console.WindowWidth;
            }
            catch
            {
            }

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
            var match = Regex.Match(total, @"(?<value>\d+(?:[.,]\d+)?)(?<unit>[KMGT]?i?B)", RegexOptions.IgnoreCase);
            if (!match.Success) return $"{percentage:F1}%";

            double.TryParse(match.Groups["value"].Value.Replace(",", "."), NumberStyles.Any,
                CultureInfo.InvariantCulture, out double totalValue);
            string unit = match.Groups["unit"].Value;

            double downloadedValue = (percentage / 100.0) * totalValue;
            return $"{downloadedValue:F2}{unit}";
        }
    }
}