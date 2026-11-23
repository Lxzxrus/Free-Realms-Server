using System;
using System.IO;
using System.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sanctuary.Core.Configuration;

/// <summary>
/// Deletes a server's log files once they are <see cref="DefaultDays"/> days old: at start, then once a day. NLog writes a
/// file a day (Chat-2026-11-23.log, Gateway-Info-2026-11-23.log) and never deletes one: its archive settings
/// (maxArchiveDays, maxArchiveFiles) don't clean up files named by date, as tried with NLog 6.1.4. Nate chose 30 days for
/// chat logs (2026-11-23, launch prep), long enough to look into a report. The Audit log (staff commands) is kept.
/// </summary>
public static class LogRetention
{
    public const string DaysKey = "Logging:RetentionDays";
    public const int DefaultDays = 30;

    /// <summary>The folder NLog writes to: <c>${basedir}/Logs</c> in every NLog.config.</summary>
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "Logs");

    /// <summary>
    /// Starts the cleanup. Dispose the result to stop it. <see cref="DaysKey"/> sets the days; 0 or less keeps everything.
    /// </summary>
    public static IDisposable Start(IConfiguration configuration, ILogger logger)
    {
        var days = int.TryParse(configuration[DaysKey], out var configured) ? configured : DefaultDays;

        if (days <= 0)
            return new Timer(_ => { });

        var maxAge = TimeSpan.FromDays(days);

        return new Timer(_ =>
        {
            try
            {
                var deleted = DeleteOlderThan(LogDirectory, DateTime.UtcNow - maxAge);

                if (deleted > 0)
                    logger.LogInformation("Deleted {count} log files older than {days} days.", deleted, days);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't delete old log files.");
            }
        }, null, TimeSpan.Zero, TimeSpan.FromDays(1));
    }

    /// <summary>
    /// Deletes the <c>.log</c> files in <paramref name="directory"/> last written before <paramref name="cutoffUtc"/>,
    /// except Audit logs. Returns how many it deleted. A file it can't delete (still open, say) is left for the next day.
    /// </summary>
    public static int DeleteOlderThan(string directory, DateTime cutoffUtc)
    {
        if (!Directory.Exists(directory))
            return 0;

        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
        {
            if (Path.GetFileName(file).StartsWith("Audit-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
                continue;

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
