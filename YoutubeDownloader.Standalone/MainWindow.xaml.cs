using ByteSizeLib;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using YoutubeExplode;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Models;
using YoutubeExplode.Models.MediaStreams;

namespace SwiftingDuster.YoutubeDownloader.Standalone
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string DownloadFinishedIndicator = "✔";

        private string downloadDirectory = YoutubeDownloader.DownloadDirectory;

        private List<string> activeDownloadList = new List<string>();

        private bool showDownloadConfirmation = true;

        private Dictionary<string, Video> urlToVideoInfoDictionary = new Dictionary<string, Video>();
        private Dictionary<string, BitmapImage> urlToVideoThumbnailDictionary = new Dictionary<string, BitmapImage>();

        public MainWindow()
        {
            InitializeComponent();
            
            YoutubeDownloadDirectoryLabel.Content = $"Download to: {downloadDirectory}";
            YoutubeDownloadDirectoryLabel.ToolTip = $"{downloadDirectory}";

            VideoResolutionComboBox.SelectionChanged += VideoResolutionComboBox_SelectionChanged;
        }

        private async void YoutubeLinkTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox targetTextBox = sender as TextBox;
            string url = targetTextBox.Text;

            if (!YoutubeClient.TryParseVideoId(url, out string videoID)) return;

            string urlTextBoxNumber = new string(targetTextBox.Name.SkipWhile(c => !Char.IsDigit(c)).Take(1).ToArray());
            TextBlock titleTextBlock = (FindName($"YoutubeTitle{urlTextBoxNumber}TextBlock") as TextBlock);
            Label infoLabel = (FindName($"YoutubeInfo{urlTextBoxNumber}Label") as Label);
            Image thumbnailImage = (FindName($"YoutubeThumbnail{urlTextBoxNumber}Image") as Image);
            Button downloadButton = (FindName($"YoutubeDownload{urlTextBoxNumber}Button") as Button);
            downloadButton.IsEnabled = false;
            downloadButton.Content = "Retrieving...";

            if (!urlToVideoInfoDictionary.TryGetValue(url, out Video video))
            {
                try
                {
                    video = await YoutubeDownloader.GetVideoInfoAsync(url);
                }
                catch (VideoUnavailableException ex)
                {
                    downloadButton.Content = "Download";
                    thumbnailImage.Source = null;
                    titleTextBlock.Text = ex.Message;
                    infoLabel.Content = String.Empty;
                    return;
                }
                urlToVideoInfoDictionary[url] = video;
            }
            Tuple<VideoStreamInfo, AudioStreamInfo> mediaStreamInfos = await YoutubeDownloader.GetBestVideoAndAudioStreamInfoAsync(url);

            string videoSize = Math.Round(ByteSize.FromBytes(mediaStreamInfos.Item1.Size + mediaStreamInfos.Item2.Size).MegaBytes, 2).ToString() + " MB";
            string audioSize = Math.Round(ByteSize.FromBytes(mediaStreamInfos.Item2.Size).MegaBytes, 2).ToString() + " MB";

            titleTextBlock.Text = video.Title + Environment.NewLine + $"[{mediaStreamInfos.Item1.Resolution.Height}p]";
            infoLabel.Content = String.Format("Video Size: {1}{0}Audio Size: {2}{0}Duration: {3}", Environment.NewLine, videoSize, audioSize, video.Duration.ToString());

            if (!urlToVideoThumbnailDictionary.TryGetValue(url, out BitmapImage image))
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadDataCompleted += (s, dlEventArgs) =>
                    {
                        try
                        {
                            byte[] imageBytes = dlEventArgs.Result;
                            thumbnailImage.Source = image = imageBytes.GetImageFromBytes();
                            downloadButton.Content = "Download";
                            downloadButton.IsEnabled = true;
                            
                            if (!urlToVideoThumbnailDictionary.ContainsKey(url)) urlToVideoThumbnailDictionary.Add(url, image);
                        }
                        catch (TargetInvocationException) // Max resolution thumbnail was not found.
                        {
                            client.DownloadDataAsync(new Uri(video.Thumbnails.HighResUrl, UriKind.Absolute));
                        }
                    };
                    client.DownloadDataAsync(new Uri(video.Thumbnails.MaxResUrl, UriKind.Absolute));
                }
            }
            else
            {
                thumbnailImage.Source = image;
                downloadButton.Content = "Download";
                downloadButton.IsEnabled = true;
            }
        }

        private void YoutubeDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;

            if (button.Content.ToString() == DownloadFinishedIndicator) return;

            string urlTextBoxNumber = new String(button.Name.SkipWhile(c => !Char.IsDigit(c)).Take(1).ToArray());
            string url = (FindName($"YoutubeLink{urlTextBoxNumber}TextBox") as TextBox).Text;
            bool audioOnly = (FindName($"YoutubeDownloadAudioOnly{urlTextBoxNumber}CheckBox") as CheckBox).IsChecked.GetValueOrDefault();

            if (!urlToVideoInfoDictionary.TryGetValue(url, out Video video)) return;

            if (showDownloadConfirmation)
            {
                if (MessageBox.Show($"Start downloading {video.Title.Trim()}?", "Download Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            void DownloadStart(string videoTitle)
            {
                button.IsEnabled = false;
                button.Content = "In Progress";
            }

            void DownloadComplete(string fileName)
            {
                button.Content = "Processing";
            }

            void FFmpegProcessComplete(string fileName)
            {
                button.Content = DownloadFinishedIndicator;
                button.IsEnabled = true;
                activeDownloadList.Remove(url);
            }

            if (!audioOnly)
            {
                int desiredResolution = int.Parse(((ComboBoxItem)VideoResolutionComboBox.SelectedItem).Content.ToString().Replace("p", ""));
                YoutubeDownloader.DownloadVideoAndAudioAsync(url, DownloadStart, DownloadComplete, FFmpegProcessComplete, downloadDirectory, desiredResolution);
            }
            else
            {
                YoutubeDownloader.DownloadAudioAsync(url, DownloadStart, DownloadComplete, FFmpegProcessComplete, downloadDirectory, AudioConvertToMP3CheckBox.IsChecked.GetValueOrDefault(true));
            }

            activeDownloadList.Add(url);
        }

        private async void VideoResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int desiredResolution = Int32.Parse(((ComboBoxItem)VideoResolutionComboBox.SelectedItem).Content.ToString().Replace("p", ""));

            string[] links = new string[] { YoutubeLink1TextBox.Text, YoutubeLink2TextBox.Text, YoutubeLink3TextBox.Text, YoutubeLink4TextBox.Text, YoutubeLink5TextBox.Text };

            Button downloadButton;
            for (int i = 0; i < links.Length; i++)
            {
                downloadButton = FindName($"YoutubeDownload{i + 1}Button") as Button;
                if (downloadButton.IsEnabled && YoutubeClient.TryParseVideoId(links[i], out string videoID))
                {
                    downloadButton.IsEnabled = false;
                    Tuple<VideoStreamInfo, AudioStreamInfo> mediaStreamInfos = await YoutubeDownloader.GetBestVideoAndAudioStreamInfoAsync(YoutubeLink1TextBox.Text, desiredResolution);

                    string videoSize = Math.Round(ByteSize.FromBytes(mediaStreamInfos.Item1.Size + mediaStreamInfos.Item2.Size).MegaBytes, 2).ToString() + " MB";
                    string audioSize = Math.Round(ByteSize.FromBytes(mediaStreamInfos.Item2.Size).MegaBytes, 2).ToString() + " MB";

                    if (urlToVideoInfoDictionary.TryGetValue(links[i], out Video video))
                    {
                        TextBlock titleTextBlock = FindName($"YoutubeTitle{i + 1}TextBlock") as TextBlock;
                        Label infoLabel = FindName($"YoutubeInfo{i + 1}Label") as Label;
                        titleTextBlock.Text = video.Title + Environment.NewLine + $"[{mediaStreamInfos.Item1.Resolution.Height}p]";
                        infoLabel.Content = String.Format("Video Size: {1}{0}Audio Size: {2}{0}Duration: {3}", Environment.NewLine, videoSize, audioSize, video.Duration.ToString());
                    }

                    downloadButton.Content = "Download";
                    downloadButton.IsEnabled = true;
                }
            }
        }

        private void YoutubeDownloadDirectoryPickerButton_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog fileDialog = new CommonOpenFileDialog()
            {
                Title = "Select a location to save your downloads",
                IsFolderPicker = true,
                InitialDirectory = downloadDirectory,
                AddToMostRecentlyUsedList = false,
                AllowNonFileSystemItems = false,
                DefaultDirectory = YoutubeDownloader.DownloadDirectory,
                EnsureFileExists = true,
                EnsurePathExists = true,
                EnsureReadOnly = false,
                EnsureValidNames = true,
                Multiselect = false,
                ShowPlacesList = true
            };

            if (fileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                downloadDirectory = fileDialog.FileName;
                YoutubeDownloadDirectoryLabel.Content = $"Download to: {downloadDirectory}";
                YoutubeDownloadDirectoryLabel.ToolTip = $"{downloadDirectory}";
            }
        }

        private void YoutubeDownloadDirectoryGotoButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(downloadDirectory);
        }

        private void DownloadAllAudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Download all audio?", "Download Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            showDownloadConfirmation = false;

            if (YoutubeDownload1Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink1TextBox.Text) && YoutubeDownload1Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly1CheckBox.IsChecked = true;
                YoutubeDownload1Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload2Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink2TextBox.Text) && YoutubeDownload2Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly2CheckBox.IsChecked = true;
                YoutubeDownload2Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload3Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink3TextBox.Text) && YoutubeDownload3Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly3CheckBox.IsChecked = true;
                YoutubeDownload3Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload4Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink4TextBox.Text) && YoutubeDownload4Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly4CheckBox.IsChecked = true;
                YoutubeDownload4Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload5Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink5TextBox.Text) && YoutubeDownload5Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly5CheckBox.IsChecked = true;
                YoutubeDownload5Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }

            showDownloadConfirmation = true;
        }

        private void DownloadAllMuxedButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Download all video and audio?", "Download Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            showDownloadConfirmation = false;

            if (YoutubeDownload1Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink1TextBox.Text) && YoutubeDownload1Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly1CheckBox.IsChecked = false;
                YoutubeDownload1Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload2Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink2TextBox.Text) && YoutubeDownload2Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly2CheckBox.IsChecked = false;
                YoutubeDownload2Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload3Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink3TextBox.Text) && YoutubeDownload3Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly3CheckBox.IsChecked = false;
                YoutubeDownload3Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload4Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink4TextBox.Text) && YoutubeDownload4Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly4CheckBox.IsChecked = false;
                YoutubeDownload4Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (YoutubeDownload5Button.IsEnabled && !activeDownloadList.Contains(YoutubeLink5TextBox.Text) && YoutubeDownload5Button.Content.ToString() != DownloadFinishedIndicator)
            {
                YoutubeDownloadAudioOnly5CheckBox.IsChecked = false;
                YoutubeDownload5Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }

            showDownloadConfirmation = true;
        }

        private void YoutubeLinkTextBox_GotMouseCapture(object sender, MouseEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (textBox.Text.Equals(String.Empty))
                {
                    string clipboardText = Clipboard.GetText();
                    if (YoutubeClient.TryParseVideoId(clipboardText, out string videoID)) // Check if clipboard contains a valid youtube link.
                    {
                        textBox.Text = clipboardText;
                    }
                }
                else
                {
                    textBox.SelectAll();
                }
            }
        }
    }
}