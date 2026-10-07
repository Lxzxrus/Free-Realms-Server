using System;
using System.Collections.Generic;

namespace Launcher.Helpers;

public static class LaunchArguments
{
    /// <summary>
    /// The game client's command line. WebAPI's <c>launchArguments</c> are passed through unchanged, split on
    /// spaces (docs/webapi.md): they carry this login's portrait upload URL, which must not be rebuilt here.
    /// </summary>
    public static List<string> Build(string loginServer, string sessionId, string locale, string? serverArguments)
    {
        var arguments = new List<string>
        {
            $"Server={loginServer}",
            $"SessionId={sessionId}",
            $"Internationalization:Locale={locale}"
        };

        if (!string.IsNullOrWhiteSpace(serverArguments))
            arguments.AddRange(serverArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return arguments;
    }
}
