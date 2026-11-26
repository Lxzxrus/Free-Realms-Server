using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Reports;

/// <summary>A new report as it is passed on outside the game (the Discord webhook). All text is cleaned and capped.</summary>
public sealed record ReportNotification(
    ulong Id,
    ReportKind Kind,
    string ReporterName,
    string? TargetName,
    string Summary,
    string Zone,
    string Position,
    string ClientText,
    string ServerContext)
{
    public static ReportNotification From(DbReport report, string summary) => new(
        report.Id,
        report.Kind,
        report.ReporterName,
        report.TargetName,
        summary,
        report.Zone,
        report.Position,
        report.ClientText,
        report.ServerContext);
}

/// <summary>
/// Tells people outside the game about a new report. <see cref="Notify"/> is called on the game's packet path, so it must
/// return at once and never throw: an implementation queues the work and does it elsewhere.
/// </summary>
public interface IReportNotifier
{
    void Notify(ReportNotification notification);
}

/// <summary>Passes nothing on. Used when no webhook is set, and in tests.</summary>
public sealed class NullReportNotifier : IReportNotifier
{
    public void Notify(ReportNotification notification)
    {
    }
}
