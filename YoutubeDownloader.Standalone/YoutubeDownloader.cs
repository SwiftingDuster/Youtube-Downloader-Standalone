using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using YoutubeExplode;
using YoutubeExplode.Models;
using YoutubeExplode.Models.MediaStreams;

namespace SwiftingDuster.YoutubeDownloader.Standalone
{
    internal delegate void DownloadStart(string videoTitle);
    internal delegate void DownloadComplete(string fileNameWithExtension);
    internal delegate void FFmpegProcessComplete(string fileNameWithExtension);

    internal class YoutubeDownloader
    {
        internal static readonly string YoutubeDownloadsDirectory = Path.Combine(Environment.CurrentDirectory, "Youtube Downloads");
        private static readonly string FFmpegDirectory = Path.Combine(Environment.CurrentDirectory, "Tools");
        private const string FFmpegExeName = "ffmpeg.exe";
        private const string FFprobeExeName = "ffprobe.exe";

        private static YoutubeClient client = new YoutubeClient();

        internal static void Initialize()
        {
            if (!Directory.Exists(YoutubeDownloadsDirectory))
            {
                Directory.CreateDirectory(YoutubeDownloadsDirectory);
            }

            FFbase.FFmpegDir = FFmpegDirectory;
            FFbase.FFmpegExecutableName = FFmpegExeName;
            FFbase.FFprobeExecutableName = FFprobeExeName;
        }

        /// <summary>
        /// Retrieves a <see cref="Video"/> object containing information about a Youtube video.
        /// </summary>
        /// <param name="youtubeURL">The youtube video link.</param>
        internal static async Task<Video> GetVideoInfoAsync(string youtubeURL)
        {
            String videoId = YoutubeClient.ParseVideoId(youtubeURL);
            return await client.GetVideoAsync(videoId);
        }

        /// <summary>
        /// Retrieves a video stream of the highest resolution equal or less than maxVideoResolution and an audio stream of the highest bitrate of a youtube video. 
        /// </summary>
        /// <param name="youtubeURL">The youtube video link.</param>
        /// <param name="maxVideoResolution"></param>
        internal static async Task<Tuple<VideoStreamInfo, AudioStreamInfo>> GetBestVideoAndAudioStreamInfoAsync(string youtubeURL, int maxVideoResolution = 1080)
        {
            String videoId = YoutubeClient.ParseVideoId(youtubeURL);
            MediaStreamInfoSet mediaStreamInfoSet = await client.GetVideoMediaStreamInfosAsync(videoId);

            VideoStreamInfo videoStreamInfo = mediaStreamInfoSet.Video.OrderByDescending(vid => vid.Resolution.Height).FirstOrDefault(vid => vid.Resolution.Height <= maxVideoResolution && vid.VideoEncoding == VideoEncoding.H264) ??
                mediaStreamInfoSet.Video.OrderByDescending(vid => vid.Resolution.Height).First(vid => vid.Resolution.Height <= maxVideoResolution);
            AudioStreamInfo audioStreamInfo = mediaStreamInfoSet.Audio.WithHighestBitrate();

            return new Tuple<VideoStreamInfo, AudioStreamInfo>(videoStreamInfo, audioStreamInfo);
        }

        /// <summary>
        /// Retrieves the highest bitrate audio stream of a youtube video.
        /// </summary>
        /// <param name="youtubeURL">The youtube video link.</param>
        internal static async Task<AudioStreamInfo> GetBestAudioStreamInfoAsync(string youtubeURL)
        {
            String videoId = YoutubeClient.ParseVideoId(youtubeURL);
            MediaStreamInfoSet mediaStreamInfoSet = await client.GetVideoMediaStreamInfosAsync(videoId);

            AudioStreamInfo audioStreamInfo = mediaStreamInfoSet.Audio.WithHighestBitrate();

            return audioStreamInfo;
        }

        /// <summary>
        /// Retrieves the best quality video stream and highest bitrate audio stream, then downloads them both and use FFmpeg tool to encode both together into a muxed video.
        /// </summary>
        /// <param name="youtubeURL">The youtube video link.</param>
        /// <param name="onDownloadStart">Callback when download starts.</param>
        /// <param name="onDownloadComplete">Callback when download finishes.</param>
        /// <param name="onFFmpegProcessComplete">Callback when processing of video and audio using FFmpeg finishes.</param>
        /// <param name="outputDirectory">Directory to output the final video. This is the same directory the raw files before encoding will be temporarily stored.</param>
        /// <param name="maxVideoResolution">Maximum desired resolution of the video.</param>
        internal static async void DownloadVideoAndAudioAsync(string youtubeURL, DownloadStart onDownloadStart, DownloadComplete onDownloadComplete, FFmpegProcessComplete onFFmpegProcessComplete, string outputDirectory, int maxVideoResolution = 1080)
        {
            Video video = await GetVideoInfoAsync(youtubeURL);
            String videoTitle = video.Title.Trim();

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (videoTitle.IndexOfAny(invalidChars) != -1)
            {
                videoTitle = new String(videoTitle.Where(c => !invalidChars.Contains(c)).ToArray());
            }

            onDownloadStart?.Invoke(videoTitle);

            Tuple<VideoStreamInfo, AudioStreamInfo> mediaStreamInfos = await GetBestVideoAndAudioStreamInfoAsync(youtubeURL, maxVideoResolution);
            VideoStreamInfo videoStreamInfo = mediaStreamInfos.Item1;
            AudioStreamInfo audioStreamInfo = mediaStreamInfos.Item2;

            String videoFileName = $"{videoTitle}.{videoStreamInfo.Container.GetFileExtension()}";
            String audioFileName = $"{videoTitle}.{audioStreamInfo.Container.GetFileExtension()}";

            String tempVideoFilePath = Path.Combine(outputDirectory, $"[Vid] {videoFileName}");
            String tempAudioFilePath = Path.Combine(outputDirectory, $"[Aud] {audioFileName}");
            String videoFilePath = Path.Combine(outputDirectory, Path.ChangeExtension(videoFileName, ".mp4"));
            await client.DownloadMediaStreamAsync(videoStreamInfo, tempVideoFilePath);
            await client.DownloadMediaStreamAsync(audioStreamInfo, tempAudioFilePath);

            onDownloadComplete?.Invoke(videoFileName);

            if (File.Exists(videoFilePath)) File.Delete(videoFilePath);
            await ConversionHelper.AddAudio(tempVideoFilePath, tempAudioFilePath, videoFilePath).Start();

            File.Delete(tempVideoFilePath);
            File.Delete(tempAudioFilePath);

            onFFmpegProcessComplete?.Invoke(videoFileName);
        }

        /// <summary>
        /// Retrieves the highest bitrate audio stream, then downloads it and optionally use FFmpeg tool to re-encode into MP3 format.
        /// </summary>
        /// <param name="youtubeURL">The youtube video link.</param>
        /// <param name="onDownloadStart">Callback when download starts.</param>
        /// <param name="onDownloadComplete">Callback when download finishes.</param>
        /// <param name="onFFmpegProcessComplete">Callback when processing of audio file using FFmpeg finishes. This is only called convertToMp3 parameter is true.</param>
        /// <param name="convertToMP3">Whether to convert to mp3 if downloaded audio file is not MP3 format.</param>
        /// <param name="outputDirectory">Directory to output the final video. This is the same directory the raw files before encoding will be temporarily stored.</param>
        internal static async void DownloadAudioAsync(string youtubeURL, DownloadStart onDownloadStart, DownloadComplete onDownloadComplete, FFmpegProcessComplete onFFmpegProcessComplete, string outputDirectory, bool convertToMP3 = true)
        {
            Video video = await GetVideoInfoAsync(youtubeURL);
            String videoTitle = video.Title.Trim();

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (videoTitle.IndexOfAny(invalidChars) != -1)
            {
                videoTitle = new String(videoTitle.Where(c => !invalidChars.Contains(c)).ToArray());
            }

            onDownloadStart?.Invoke(videoTitle);

            AudioStreamInfo audioStreamInfo = await GetBestAudioStreamInfoAsync(youtubeURL);

            String audioFileName = $"{videoTitle}.{audioStreamInfo.Container.GetFileExtension()}";
            String audioFilePath = Path.Combine(outputDirectory, audioFileName);
            await client.DownloadMediaStreamAsync(audioStreamInfo, audioFilePath);

            onDownloadComplete?.Invoke(audioFileName);

            if (convertToMP3 && !audioFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                audioFileName = Path.ChangeExtension(audioFileName, ".mp3");
                String mp3AudioFilePath = Path.Combine(outputDirectory, audioFileName);

                if (File.Exists(mp3AudioFilePath)) File.Delete(mp3AudioFilePath);
                await new Conversion().SetInput(audioFilePath).SetOutput(mp3AudioFilePath).Start();

                File.Delete(audioFilePath);
                onFFmpegProcessComplete?.Invoke(mp3AudioFilePath);
            }
            else
            {
                onFFmpegProcessComplete?.Invoke(audioFileName);
            }
        }
    }
}