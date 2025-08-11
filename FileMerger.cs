using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

class Program
{
    // Set your folder path here:
    static string folder = "/Path/To/Dir";

    static void Main()
    {
        var videoFiles = Directory.GetFiles(folder, "*.mp4");

        // Create mergedFiles folder path
        string mergedFolder = Path.Combine(folder, "mergedFiles");
        if (!Directory.Exists(mergedFolder))
        {
            Directory.CreateDirectory(mergedFolder);
            Console.WriteLine($"Created output directory: {mergedFolder}");
        }

        foreach (var videoFile in videoFiles)
        {
            string baseName = Path.GetFileNameWithoutExtension(videoFile);

            string[] audioExts = { ".mp3", ".webm" };
            string audioFile = audioExts
                .Select(ext => Path.Combine(folder, baseName + ext))
                .FirstOrDefault(File.Exists);

            if (audioFile == null)
            {
                Console.WriteLine($"No matching audio file found for {baseName}");
                continue;
            }

            // Output file inside mergedFiles folder with original base name
            string outputFile = Path.Combine(mergedFolder, baseName + ".mp4");

            if (File.Exists(outputFile))
            {
                Console.WriteLine($"Skipping {baseName} because {outputFile} already exists.");
                continue;
            }

            string arguments = $"-i \"{videoFile}\" -i \"{audioFile}\" -c copy -map 0:v:0 -map 1:a:0 \"{outputFile}\"";

            Console.WriteLine($"Merging:\n Video: {videoFile}\n Audio: {audioFile}\n Output: {outputFile}");

            var process = new Process();
            process.StartInfo.FileName = "ffmpeg";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            Console.WriteLine($"Finished merging {baseName}");
        }
    }
}
