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
    string ServerContext,
    bool AboutStaff = false)
{
    /// <summary>
    /// A report as it is passed on outside the game. One about a staff member (<paramref name="aboutStaff"/>) is passed on
    /// without the reporter, the target or any text: the channel it goes to can be read by staff who may not read it (the
    /// rank rule of <c>!mod report</c>), so it only says that one exists.
    /// </summary>
    public static ReportNotification From(DbReport report, string summary, bool aboutStaff = false) => aboutStaff
        ? new(report.Id, report.Kind, string.Empty, null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, AboutStaff: true)
        : new(
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
