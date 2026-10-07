using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Launcher.Helpers;
using Launcher.Models;

using NLog;

namespace Launcher.ViewModels;

public partial class Login : Popup
{
    private readonly Server _server;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    private string? warning;

    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string username = string.Empty;

    // Held in memory only, for this one request. The launcher never stores a password.
    [Required]
    [ObservableProperty]
    [NotifyDataErrorInfo]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool rememberUsername;

    public bool AutoFocusUsername => string.IsNullOrEmpty(Username);
    public bool AutoFocusPassword => !string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password);

    public Login(Server server)
    {
        _server = server;

        AddSecureWarning();

        RememberUsername = _server.Info.RememberUsername;
        Username = RememberUsername ? _server.Info.Username ?? string.Empty : string.Empty;

        View = new Views.Login
        {
            DataContext = this
        };
    }

    // Handles changes to the "Remember Username" checkbox
    partial void OnRememberUsernameChanged(bool value)
    {
        _server.Info.RememberUsername = value;

        if (!value)
            _server.Info.Username = null;

        Settings.Instance.Save();
    }

    [RelayCommand]
    public void Register()
    {
        App.ShowPopup(new Register(_server));
    }

    public override async Task<bool> ProcessAsync()
    {
        ProgressDescription = App.GetText("Text.Login.Loading");

        using var httpClient = HttpHelper.CreateHttpClient();

        var result = await WebApiClient.LoginAsync(httpClient, _server.Info.WebApiUrl, Username, Password);

        if (result.Status != WebApiStatus.Ok || result.Login is null)
        {
            _logger.Warn("Login failed for server '{Name}': {Status} (HTTP {HttpStatus}).", _server.Info.Name, result.Status, (int?)result.HttpStatus);

            App.AddNotification(WebApiMessages.Describe(result, isLogin: true), true);

            if (result.Status == WebApiStatus.WrongCredentials)
                Password = string.Empty;

            return false;
        }

        Password = string.Empty;

        SaveRememberedUsername();

        await LaunchClientAsync(result.Login.SessionId, result.Login.LaunchArguments);

        return true;
    }

    private void AddSecureWarning()
    {
        if (!TransportPolicy.IsAllowed(_server.Info.WebApiUrl))
            Warning = App.GetText("Text.WebApi.Insecure");
        else if (Uri.TryCreate(_server.Info.WebApiUrl, UriKind.Absolute, out var webApiUrl) && webApiUrl.Scheme != Uri.UriSchemeHttps)
            Warning = App.GetText("Text.Server.LocalHttpWarning");
    }

    private void SaveRememberedUsername()
    {
        _server.Info.Username = RememberUsername && !string.IsNullOrEmpty(Username) ? Username : null;

        Settings.Instance.Save();
    }

    private async Task LaunchClientAsync(string sessionId, string? serverArguments)
    {
        if (!Dx9Helper.IsInstalled())
        {
            await NotifyDirectX9MissingAsync();
            return;
        }

        var arguments = LaunchArguments.Build(_server.Info.LoginServer, sessionId, Settings.Instance.Locale.ToString(), serverArguments);

        var workingDirectory = Path.Combine(Constants.SavePath, _server.Info.SavePath, "Client");
        var executablePath = Path.Combine(workingDirectory, Constants.ClientExecutableName);

        // Checked again here, right before starting it: the folder can change while the login popup is open.
        if (!await _server.CheckClientPinAsync())
            return;

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        // Platform-specific process startup logic
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var winePath = WineHelper.GetPath();

            if (string.IsNullOrEmpty(winePath))
            {
                App.AddNotification("Unable to launch the game, wine is not installed.", true);

                return;
            }

            startInfo.FileName = winePath;
            startInfo.ArgumentList.Add(Constants.ClientExecutableName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = executablePath;
        }
        else
        {
            App.AddNotification("Unable to launch the game, your operating system is not supported.", true);

            return;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        _server.Process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _server.Process.Exited += _server.ClientProcessExited;

        try
        {
            _server.Process.Start();
        }
        catch (Exception ex)
        {
            App.AddNotification("An error occurred while launching the game. Please try again.", true);

            _logger.Error(ex, "Failed to start the client process for server: {Name}.", _server.Info.Name);

            _server.Process?.Dispose();
            _server.Process = null;
        }
    }

    private async Task NotifyDirectX9MissingAsync()
    {
        App.AddNotification("Unable to launch the game, DirectX 9 is not available.", true);

        await Task.Delay(500);

        try
        {
            var window = App.GetWindow();

            await window.Launcher.LaunchUriAsync(new Uri(Constants.DirectXDownloadUrl));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open the DirectX download page automatically.");

            App.AddNotification($"""
                                 Could not open the DirectX download page.
                                 Please open this URL manually: {Constants.DirectXDownloadUrl}
                                 """, true);
        }
    }
}