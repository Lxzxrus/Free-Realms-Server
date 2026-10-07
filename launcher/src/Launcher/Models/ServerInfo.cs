using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.Models;

public sealed class ServerInfo : ObservableObject
{
    public required string Url { get; set; }

    private string name = string.Empty;
    public required string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    private string description = string.Empty;
    public required string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public required string WebApiUrl { get; set; }
    public required string LoginServer { get; set; }

    /// <summary>
    /// Where clientmanifest.xml and client/ are served. Null means the server's own <see cref="Url"/>.
    /// </summary>
    public string? ClientUrl { get; set; }

    /// <summary>
    /// This is generated once the server is added.
    /// </summary>
    public required string SavePath { get; init; }

    public string? Username { get; set; }
    public bool RememberUsername { get; set; }
}