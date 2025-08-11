# Youtube Downloader in C#
This Code is written with Claude and ChatGPT.

# Usage
1. Inside the Code change the Variables `DOWNLOAD_DIRECTORY` & `YOUTUBE_URL`.
2. When these two variables are set, a selection of videos will be inside the console. Given the number, the part will be downloaded
3. When the filename already exists, the file will not be downloaded and the program will exit.

# Combinning Video & Audio Part (for mac)
1. install ffmpeg `brew install ffmpeg`
2. change the variable `folder` to the correct Directory
3. all files with the same name will be merged together. Video must be `.mp4` and audio must `.mp3` or `.webm`
4. A directory will be created with the merged files
