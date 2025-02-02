﻿using DevExpress.Mvvm;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Weatube.Models;
using Weatube.Properties;
using System.Text.RegularExpressions;

namespace Weatube.Viewmodels
{
    class WeatubeViewModel : NotifyPropertyChangedBehavior
    {
        public ObservableCollection<VideoModel> QueuedVideos { get; private set; }

        public SuggestionModel suggestion { get; private set; }

        public YoutubeDL currentVideo { get; private set; }

        private string _SearchVideo { get; set; }

        public bool IsSearching { get { return currentVideo != null; } }

        public bool IsDownloading { get; private set; }
        public string _SaveDirectoryPath { get; private set; }
        public string SaveDirectoryPath 
        { 
            get
            {
                return _SaveDirectoryPath;
            } 
            set
            {
                _SaveDirectoryPath = value;
                Settings.Default.DefaultSavePath = value;
            }
        }

        private List<string> _MessageOfTheDay;

        public string MessageOfTheDay { get { return _MessageOfTheDay.OrderBy(x => Guid.NewGuid()).FirstOrDefault(); } }

        public string SearchVideo
        {
            get => _SearchVideo;
            set
            {
                _SearchVideo = value;
                _ = Task.Run(async () =>
                {
                    if (_SearchVideo.Length < 1)
                    {
                        suggestion.Disable();
                        await Task.Delay(300);
                        suggestion = new SuggestionModel();
                        return;
                    }
                    async Task<bool> UserKeepsTyping()
                    {
                        string txt = _SearchVideo;
                        await Task.Delay(500);
                        return txt != _SearchVideo;
                    }
                    if (await UserKeepsTyping()) return;

                    currentVideo = new YoutubeDL(_SearchVideo);
                    if (await currentVideo.InitAsync() != null)
                          if (_SearchVideo == currentVideo.SourceUrl) suggestion = new SuggestionModel(currentVideo);
                    currentVideo = null;
                });
            }
        }

        public WeatubeViewModel()
        {
            suggestion = new SuggestionModel();
            QueuedVideos = new ObservableCollection<VideoModel>();
            _SaveDirectoryPath = (Settings.Default.DefaultSavePath.Length > 0) 
                ? Settings.Default.DefaultSavePath
                : Path.Combine(Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%"), "Downloads");
            _MessageOfTheDay = File.Exists("nsfw.dfcn") ? Resources.motd.Split('\n').ToList() : new List<string>() { "Added videos:" }; 
            if (MessageOfTheDay.Contains("Monika"))
            {
                var dialog = new Window1();
                dialog.ShowDialog();
                dialog.Focus();
            }
        }

        public ICommand AddVideo => 
            new DelegateCommand<List<YoutubeDL.Video>>(async (videos) =>
                {
                    foreach (var video in videos)
                    {
                        video.SelectedFormat = suggestion.SelectedType;
                        QueuedVideos.Add(new VideoModel(video));
                        await Task.Delay(15);
                        QueuedVideos.Last().IsPanelEnabled = true;
                    }
                    SearchVideo = "";
                }, (videos) => suggestion.SuggestedVideoName != null && SearchVideo.Length > 1);

        public ICommand DeleteVideo =>
            new DelegateCommand<VideoModel>(async (video) =>
            {
                QueuedVideos[QueuedVideos.IndexOf(video)].IsPanelEnabled = false;
                video.Disable();
                await Task.Delay(300);
                QueuedVideos.Remove(video);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(() =>
                {
                    var path = SaveDirectoryPath + '\\';
                    foreach (string ext in new string[]{ "*.part", "*.webp" })
                    foreach (var f in Directory.GetFiles(path, ext))
                    {
                        try { File.Delete(f); }
                        catch { Console.WriteLine("nevermind..."); }
                    }
                });
#pragma warning restore CS4014
                CommandManager.InvalidateRequerySuggested();
            }, (video) => QueuedVideos.Contains(video));

        public ICommand DownloadVideos =>
            new DelegateCommand(async () =>
            {
                VideoModel vid;
                IsDownloading = true;
                while ((vid = QueuedVideos.FirstOrDefault(a => !a.IsDownloaded)) != null)
                {
                    var filename = SaveDirectoryPath + '\\' + "%(title)s.%(ext)s";
                    var process = vid.DownloadProcess = YoutubeDL.RunProcess(vid.YoutubeVideo.GetCommandArguments(filename));
                    while (!process.HasExited)
                        vid.DownloadStateChange(await process.StandardOutput.ReadLineAsync());
                    if(!File.Exists(vid.SavePath))
                        vid.DownloadState = 
                            Utils.DownloadStateChange(Utils.PercentFromOutput(null, await process.StandardError.ReadLineAsync()));
                    vid.IsDownloaded = true;
                }
                IsDownloading = false;
                CommandManager.InvalidateRequerySuggested();
            }, () => QueuedVideos.Count > 0 && !IsDownloading);

        public ICommand ClearQueue =>
            new DelegateCommand(async () =>
            {
                foreach (var item in QueuedVideos.Reverse())
                    DeleteVideo.Execute(item);
                await Task.Delay(300);
                QueuedVideos.Clear();
                CommandManager.InvalidateRequerySuggested();
            }, () => QueuedVideos.Count > 0);

        public ICommand ChooseSaveDirectory =>
            new DelegateCommand(() =>
            {
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
                if(dialog.ShowDialog(Application.Current.MainWindow).GetValueOrDefault())
                    SaveDirectoryPath = dialog.SelectedPath;
            }, () => true);

        public ICommand OpenFileInExplorer =>
            new DelegateCommand<VideoModel>((video) =>
            {
                if (video.SavePath != null && !Regex.IsMatch(video.SavePath, @"\p{IsCyrillic}"))
                    System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", video.SavePath.Replace(@"\\", @"\")));
                else System.Diagnostics.Process.Start("explorer.exe", Settings.Default.DefaultSavePath);
            }, (video) => video != null && video.IsDownloaded == true);
    }
}
