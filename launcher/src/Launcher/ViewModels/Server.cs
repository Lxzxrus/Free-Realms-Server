using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Downloader;

using HashDepot;

using Launcher.Helpers;
using Launcher.Models;

using LiveMarkdown.Avalonia;

using NLog;

namespace Launcher.ViewModels;

public partial class Server : ObservableObject
{
    private readonly Main _main = null!;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static SolidColorBrush WhiteBrush = new(Colors.White);
    private static SolidColorBrush GreenBrush = new(Color.FromRgb(35, 165, 90));
    private static SolidColorBrush YellowBrush = new(Color.FromRgb(204, 204, 0));
    private static SolidColorBrush RedBrush = new(Color.FromRgb(242, 63, 67));

    [ObservableProperty]
    private ServerInfo info = null!;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string status = App.GetText("Text.ServerStatus.Offline");

    [ObservableProperty]
    private int onlinePlayers;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private Process? process;

    [ObservableProperty]
    private IBrush? serverStatusFill = WhiteBrush;

    [ObservableProperty]
    private bool isDownloading = false;

    [ObservableProperty]
    private ObservableStringBuilder markdownBuilder = new();

    public Server()
    {
#if DEBUG && DESIGNMODE
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            var faker = new Bogus.Faker();

            Info = new ServerInfo
            {
                Url = "https://example.com",
                Name = $"{faker.Name.FirstName()}'s Server",
                Description = faker.Lorem.Paragraphs(5),
                SavePath = "Name",
                LoginServer = "127.0.0.1:20042",
                WebApiUrl = "https://example.com"
            };
        }
#endif
    }

    public Server(ServerInfo info, Main main)
    {
        Info = info;
        _main = main;
    }

    public async Task OnShowAsync()
    {
        MarkdownBuilder.Clear();
        MarkdownBuilder.Append(Info.Description);

        await RefreshCommand.ExecuteAsync(null);
    }

    public void ClientProcessExited(object? sender, EventArgs e)
    {
        Process?.Dispose();
        Process = null;
    }

    [RelayCommand]
    private async Task OpenUriAsync(LinkClickedEventArgs args)
    {
        if (args.HRef is { IsAbsoluteUri: true, Scheme: "http" or "https" } url)
        {
            var window = App.GetWindow();

            await window.Launcher.LaunchUriAsync(url);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task RefreshAsync()
    {
        Status = App.GetText("Text.ServerStatus.Refreshing");

        ServerStatusFill = YellowBrush;

        if (!string.IsNullOrEmpty(Info.Url))
        {
        try
        {
                var result = await HttpHelper.GetServerManifestAsync(Info.Url);

                if (result.Result != ManifestResult.Success || result.ServerManifest is null)
                {
                    App.AddNotification($"""
                                         Failed to get server info.
                                         {result.Error}
                                         """, true);

                    _logger.Error("Failed to get server manifest for: {Url}: {Error}.", Info.Url, result.Error);

                    switch (result.Result)
                    {
                        case ManifestResult.UnsupportedVersion:
                            ServerStatusFill = RedBrush;
                            Status = App.GetText("Text.ServerStatus.UnsupportedVersion");
                            break;

                        default:
                            ServerStatusFill = RedBrush;
                            Status = App.GetText("Text.ServerStatus.Offline");
                            break;
                    }

                    IsEnabled = false;

                    return;
                }

                var serverManifest = result.ServerManifest;

                Info.Name = serverManifest.Name;
                Info.Description = serverManifest.Description;

                Info.WebApiUrl = serverManifest.WebApiUrl;
                Info.LoginServer = serverManifest.LoginServer;
                Info.ClientUrl = serverManifest.ClientUrl;

                Settings.Instance.Save();
            }
            catch (Exception ex)
            {
                ServerStatusFill = RedBrush;
                Status = App.GetText("Text.ServerStatus.Offline");

                App.AddNotification(ex is InsecureTransportException
                    ? App.GetText("Text.WebApi.Insecure")
                    : App.GetText("Text.WebApi.ServerDown"), true);

                _logger.Error(ex, "An exception was thrown while getting server info for: {Url}.", Info.Url);

                IsEnabled = false;

                return;
            }
        }

        try
        {
            var serverStatus = await ServerStatusHelper.GetAsync(Info.LoginServer);

            IsOnline = serverStatus.IsOnline;

            if (serverStatus.IsOnline)
            {
                Status = App.GetText(serverStatus.IsLocked
                    ? "Text.ServerStatus.Locked"
                    : "Text.ServerStatus.Online");

                OnlinePlayers = serverStatus.OnlinePlayers;

                ServerStatusFill = serverStatus.IsLocked
                    ? RedBrush
                    : GreenBrush;
            }
            else
            {
                Status = App.GetText("Text.ServerStatus.Offline");

                OnlinePlayers = 0;
                ServerStatusFill = RedBrush;
            }
        }
        catch (Exception ex)
        {
            ServerStatusFill = RedBrush;
            Status = App.GetText("Text.ServerStatus.Offline");

            _logger.Error(ex, "Error refreshing server status for: '{Name}'.", Info.Name);

            App.AddNotification("Unable to refresh server status.", true);
        }

        IsEnabled = true;
        }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PlayAsync()
    {
        if (Process != null)
        {
            App.AddNotification("Unable to launch, the game is already open.", true);

            _logger.Warn("Unable to launch, the game is already open for server: '{Name}'.", Info.Name);

            return;
        }

        if (!string.IsNullOrEmpty(Info.Url))
        {
            var (found, clientManifest) = await GetClientManifestAsync();

            if (!found)
                return;

            // Null when the server doesn't host the game files: the player's own copy in the client folder is used.
            if (clientManifest is not null)
            {
                StatusMessage = App.GetText("Text.Server.VerifyClientFiles");

                if (!await VerifyClientFilesAsync(clientManifest))
                {
                    StatusMessage = string.Empty;

                    return;
                }
            }

            if (clientManifest is not null && !clientManifest.Languages.Contains(Settings.Instance.Locale))
            {
                StatusMessage = string.Empty;

                var selectedLanguage = Locale.LocaleMap[Settings.Instance.Locale];
                var supportedLanguages = clientManifest.Languages.Select(l => Locale.LocaleMap[l]);

                App.AddNotification($"""
                                     The selected language "{selectedLanguage}" is not supported by this server.
                                     Please choose a supported language and try again.
                                     Supported languages:
                                     {string.Join(Environment.NewLine, supportedLanguages)}
                                     """, true);

            return;
        }
        }

        if (!IsOnline)
        {
            StatusMessage = string.Empty;

            App.AddNotification(App.GetText("Text.Server.GameServerOffline"), true);

            return;
        }

        StatusMessage = string.Empty;

        // All checks passed, show the login popup
        App.ShowPopup(new Login(this));
    }

    [RelayCommand]
    public async Task OpenFolderAsync()
    {
        bool result;

        try
        {
            var window = App.GetWindow();

            var folderPath = Path.Combine(Constants.SavePath, Info.SavePath);

            var directoryInfo = new DirectoryInfo(folderPath);

            result = await window.Launcher.LaunchDirectoryInfoAsync(directoryInfo);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error opening client folder directory.");

            result = false;
        }

        if (!result)
            App.AddNotification("Unable to open server directory.", true);
    }

    private string ClientBaseUrl => string.IsNullOrEmpty(Info.ClientUrl) ? Info.Url : Info.ClientUrl;

    private string ClientDirectory => Path.Combine(Constants.SavePath, Info.SavePath, "Client");

    /// <summary>
    /// Found is false when the player can't play: the error has been shown. The manifest is null when the server
    /// doesn't host the game files.
    /// </summary>
    private async Task<(bool Found, ClientManifest? Manifest)> GetClientManifestAsync()
    {
        if (string.IsNullOrEmpty(Info.Url))
            return (false, null);

        try
        {
            var result = await HttpHelper.GetClientManifestAsync(ClientBaseUrl);

            if (result.Result == ManifestResult.NotFound)
            {
                _logger.Info("{Url} has no client manifest; using the client folder as it is.", ClientBaseUrl);

                return (true, null);
            }

            if (result.Result != ManifestResult.Success || result.ClientManifest is null)
            {
                App.AddNotification($"""
                                     Failed to get client info.
                                     {result.Error}
                                     """, true);

                _logger.Error("Failed to get client manifest for: {Url}: {Error}.", ClientBaseUrl, result.Error);

                return (false, null);
            }

            return (true, result.ClientManifest);
        }
        catch (Exception ex)
        {
            App.AddNotification(ex is InsecureTransportException
                ? App.GetText("Text.WebApi.Insecure")
                : App.GetText("Text.WebApi.ServerDown"), true);

            _logger.Error(ex, "An exception was thrown while getting client info for: {Url}.", ClientBaseUrl);
        }

        return (false, null);
    }

    private async Task<bool> VerifyClientFilesAsync(ClientManifest clientManifest)
    {
        _logger.Info("Starting verifying client files for: {Name}.", Info.Name);

        List<LocalFile> filesToDownload;

        try
        {
            filesToDownload = await GetFilesToDownloadAsync(clientManifest.RootFolder);
        }
        catch (InvalidDataException ex)
        {
            App.AddNotification(App.GetText("Text.Server.UnsafeClientManifest"), true);

            _logger.Error(ex, "Refusing the client manifest from {Url}.", ClientBaseUrl);

            return false;
        }

        if (filesToDownload.Count == 0)
        {
            _logger.Info("All client files are up to date.");
            return true;
        }

        IsDownloading = true;

        var failedFiles = new ConcurrentBag<string>();

        try
        {
            var filesDownloaded = 0;

            // Choose between parallel and sequential download based on settings
            if (Settings.Instance.ParallelDownload)
            {
                var numParallelDownloads = Math.Max(2, Settings.Instance.DownloadThreads);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = numParallelDownloads
                };

                var servicePool = new ConcurrentBag<DownloadService>();

                for (var i = 0; i < numParallelDownloads; i++)
                    servicePool.Add(new DownloadService(CreateDownloadConfiguration()));

                try
                {
                    await Parallel.ForEachAsync(filesToDownload, parallelOptions, async (file, ct) =>
                    {
                        servicePool.TryTake(out var downloadService); // guaranteed non-null

                        try
                        {
                            if (!await DownloadFileAsync(downloadService!, file.Path, file.Name))
                                failedFiles.Add(file.Name);
                        }
                        finally
                        {
                            servicePool.Add(downloadService!);
                        }

                        filesDownloaded = Interlocked.Increment(ref filesDownloaded);

                        StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
                    });
                }
                finally
                {
                    foreach (var downloadService in servicePool)
                        downloadService.Dispose();
                }
            }
            else
            {
                using var downloadService = new DownloadService(CreateDownloadConfiguration());

                foreach (var file in filesToDownload)
                {
                    if (!await DownloadFileAsync(downloadService, file.Path, file.Name))
                        failedFiles.Add(file.Name);

                    filesDownloaded++;

                    StatusMessage = App.GetText("Text.Server.PreparingGameFiles", filesDownloaded, filesToDownload.Count);
                }
            }
        }
        finally
        {
            IsDownloading = false;
        }

        // Report any files that failed to download
        if (!failedFiles.IsEmpty)
        {
            var message = new StringBuilder();

            message.AppendLine($"Failed to download {failedFiles.Count} file(s):");
            message.AppendLine(string.Join("\n", failedFiles.Take(10)));

            if (failedFiles.Count > 10)
                message.AppendLine($"...And {failedFiles.Count - 10} more.");

            App.AddNotification(message.ToString(), true);
        }

        _logger.Info("Finished verifying client files for: {Name}.", Info.Name);

        return failedFiles.IsEmpty;
    }

    private static DownloadConfiguration CreateDownloadConfiguration() => new()
    {
        CustomHttpClientFactory = () => HttpHelper.DownloadHttpClient,
        MaxTryAgainOnFailure = 5,

        // Parallelism happens per-file
        ChunkCount = 1,
        ParallelDownload = false,
    };

    private async Task<bool> DownloadFileAsync(DownloadService downloadService, string path, string fileName)
    {
        if (string.IsNullOrEmpty(Info.Url))
            return false;

        var downloadFilePath = Path.Combine(path, fileName);

        try
        {
            if (!PathHelper.TryGetPathInside(ClientDirectory, path, fileName, out var filePath))
            {
                _logger.Error("Refusing to write outside the client folder: {Path}.", downloadFilePath);
                return false;
            }

            var clientFileUri = UriHelper.JoinUriPaths(ClientBaseUrl, "client", path, fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await using var fileStream = await downloadService.DownloadFileTaskAsync(clientFileUri);

            if (fileStream is null || fileStream.Length == 0)
            {
                _logger.Error("Failed to get client file or received empty stream: {Path}.", downloadFilePath);
                return false;
            }

            await using var writeStream = File.Create(filePath);

            await fileStream.CopyToAsync(writeStream);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error downloading: {Path}.", downloadFilePath);

            return false;
        }
    }

    private async Task<List<LocalFile>> GetFilesToDownloadAsync(ClientFolder rootFolder, string path = "")
    {
        var results = new List<LocalFile>();

        // Recurse into subfolders
        foreach (var folder in rootFolder.Folders)
        {
            if (!PathHelper.TryGetPathInside(ClientDirectory, path, folder.Name, out _))
                throw new InvalidDataException($"Client manifest folder '{Path.Combine(path, folder.Name)}' is outside the client folder.");

            var folderPath = Path.Combine(path, folder.Name);

            var folderResults = await GetFilesToDownloadAsync(folder, folderPath);

            results.AddRange(folderResults);
        }

        // Check files in the current folder
        foreach (var file in rootFolder.Files)
        {
            if (!PathHelper.TryGetPathInside(ClientDirectory, path, file.Name, out var filePath))
                throw new InvalidDataException($"Client manifest file '{Path.Combine(path, file.Name)}' is outside the client folder.");

            if (File.Exists(filePath))
            {
                try
                {
                    await using var readStream = File.OpenRead(filePath);

                    // First, check if file size matches. This is a quick check before hashing.
                    if (file.Size == readStream.Length)
                    {
                        var hash = await Task.Run(() => XXHash.Hash64(readStream));

                        // If hash also matches, the file is valid.
                        if (file.Hash == hash)
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not verify hash for file: {Path}.", filePath);
                }
            }

            // If file doesn't exist, or size/hash mismatch, add it for download.
            results.Add(new LocalFile
            {
                Path = path,
                Name = file.Name
            });
        }

        return results;
    }
}