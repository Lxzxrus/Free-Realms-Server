using System;
using System.Diagnostics.CodeAnalysis;

using Velopack.Sources;

namespace Launcher.Helpers;

/// <summary>
/// Where the launcher updates itself from: this repository's GitHub Releases, never the game server.
/// An update is a program every player runs, so whoever controls the update source controls their PCs. Keeping it
/// off the game server means a break-in there can't push a launcher to anyone.
/// </summary>
public static class UpdateSource
{
    /// <summary>
    /// Accepts only <c>https://github.com/{owner}/{repository}</c>. Velopack treats any other host as a GitHub
    /// Enterprise server, so a typo or a game-server address would quietly become an update source.
    /// </summary>
    public static bool TryCreate(string repositoryUrl, [NotNullWhen(true)] out GithubSource? source)
    {
        source = null;

        if (!TryGetRepository(repositoryUrl, out var repository))
            return false;

        // No access token: a token baked into the launcher would be readable by every player.
        source = new GithubSource(repository.AbsoluteUri, accessToken: null, prerelease: false);
        return true;
    }

    public static bool TryGetRepository(string repositoryUrl, [NotNullWhen(true)] out Uri? repository)
    {
        repository = null;

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/');

        if (segments.Length != 2 || Array.Exists(segments, s => s.Length == 0 || s is "." or ".."))
            return false;

        repository = new Uri($"https://github.com/{segments[0]}/{segments[1]}");
        return true;
    }
}
