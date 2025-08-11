# Youtube Downloader in C#
This Code is written with Claude and ChatGPT.



# Usage
========================================

Usage: Program.exe <youtube_url> [-d directory] [-f filename]

Arguments:
 - youtube_url    YouTube video URL (required)
 - -d directory   Download directory (optional, default: current directory)
 - -f filename    Output filename without extension (optional, default: video title)

Example:
```shell
  ./youtubedl "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```
```shell
  ./youtubedl "https://www.youtube.com/watch?v=dQw4w9WgXcQ" -d "C:\Downloads" -f "my_video"
```

