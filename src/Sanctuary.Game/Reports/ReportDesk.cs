using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.ChatCommands;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Reports;

/// <summary>
/// What the packet handlers call for a report. It turns the player into a <see cref="ReportContext"/>, finds the reported
/// player if they are online, passes the report to <see cref="ReportService"/>, and tells the staff who are online about
/// a new one. A <see cref="Player"/> cannot be built in a test, so everything with logic is in the parts this uses.
/// </summary>
public sealed class ReportDesk
{
    private readonly ReportService _service;
    private readonly IZoneManager _zoneManager;
    private readonly ILogger _logger;

    public ReportDesk(ReportService service, IZoneManager zoneManager, ILoggerFactory loggerFactory)
    {
        _service = service;
        _zoneManager = zoneManager;
        _logger = loggerFactory.CreateLogger<ReportDesk>();
    }

    public ReportResult ReportPlayer(Player reporter, string? reportedName, IReadOnlyList<(int Channel, string Message, ulong Time)> lines)
    {
        OnlineReportTarget? online = null;

        var name = ModArguments.CleanText(reportedName, ReportLimits.MaxTargetNameLength);

        if (name is not null && _zoneManager.TryGetPlayer(name, out var target))
            online = new OnlineReportTarget(target.UserId, target.CharacterId, target.Name.FullName, target.ChatEvidence.Snapshot());

        return AlertStaff(reporter, _service.SubmitPlayerReport(ContextOf(reporter), name, lines, online));
    }

    public ReportResult ReportHouse(Player reporter, ulong houseGuid, ulong ownerGuid, string? houseName, string? ownerName) =>
        AlertStaff(reporter, _service.SubmitHouseReport(ContextOf(reporter), houseGuid, ownerGuid, houseName, ownerName));

    public ReportResult ReportBug(Player reporter, string? bugXml) =>
        AlertStaff(reporter, _service.SubmitBugReport(ContextOf(reporter), bugXml));

    private static ReportContext ContextOf(Player player)
    {
        var position = player.Position;

        return new ReportContext(
            player.UserId,
            player.CharacterId,
            player.Name.FullName,
            player.Zone.Name,
            string.Create(CultureInfo.InvariantCulture, $"{position.X:F1} {position.Y:F1} {position.Z:F1}"));
    }

    /// <summary>One system message to each staff member online, for a report that was kept.</summary>
    private ReportResult AlertStaff(Player reporter, ReportResult result)
    {
        if (result.Outcome != ReportOutcome.Accepted || result.Report is null)
            return result;

        var notice = ReportQueries.FormatStaffNotice(result.Report, result.Summary);

        foreach (var staff in _zoneManager.Zones.SelectMany(zone => zone.Players).Where(player => player.ChatCommandRole >= ChatCommandRole.Mod).ToList())
        {
            try
            {
                ChatHelper.SendSystemMessage(staff, notice);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to tell a staff member about report {Id}.", result.Report.Id);
            }
        }

        return result;
    }
}
