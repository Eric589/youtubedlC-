# Youtube Downloader in C#
This Code is written with Claude and ChatGPT.



# Usage
YouTube Video Downloader with Auto-Merge
========================================
Usage: Program.exe <youtube_url> [-d directory] [-f filename]

Arguments:
  youtube_url    YouTube video URL (required)
  -d directory   Download directory (optional, default: current directory)
  -f filename    Output filename without extension (optional, default: video title)

Example:
  ./youtubedl "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
  ./youtubedl "https://www.youtube.com/watch?v=dQw4w9WgXcQ" -d "C:\Downloads" -f "my_video"
  
# Combinning Video & Audio Part (for mac)
1. install ffmpeg `brew install ffmpeg`
2. Inside `FileMerger.cs` change the variable `folder` to the correct Directory
3. all files with the same name will be merged together. Video must be `.mp4` and audio must `.mp3` or `.webm`
4. A directory will be created with the merged files


