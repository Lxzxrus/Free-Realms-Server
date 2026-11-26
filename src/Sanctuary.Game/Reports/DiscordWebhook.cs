using System;
using System.Text;
using System.Text.Json;

using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Reports;

/// <summary>The <c>Reports</c> section of the Gateway's settings.</summary>
public sealed class ReportsOptions
{
    public const string Section = "Reports";

    /// <summary>
    /// The Discord webhook that new reports are posted to. Empty (the tracked default) turns it off. It is a secret: it
    /// goes in the VPS environment or the git-ignored <c>gateway.local.json</c>, never in a tracked file.
    /// </summary>
    public string DiscordWebhookUrl { get; set; } = string.Empty;
}

public enum WebhookUrlStatus
{
    /// <summary>Nothing set: the webhook is off.</summary>
    Empty,

    /// <summary>A Discord webhook address.</summary>
    Valid,

    /// <summary>Set, but not an address a webhook may use.</summary>
    Invalid
}

/// <summary>
/// Whether a configured address may be posted to. The address is a secret and is also where player text is sent, so only
/// a real Discord webhook over https is accepted: a typo or a changed setting cannot point the server at another host.
/// </summary>
public static class DiscordWebhookUrl
{
    private const string PathPrefix = "/api/webhooks/";

    public static WebhookUrlStatus Validate(string? url, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(url))
            return WebhookUrlStatus.Empty;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
            return WebhookUrlStatus.Invalid;

        var isDiscordHost = parsed.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase);

        if (parsed.Scheme != Uri.UriSchemeHttps || !isDiscordHost || !parsed.IsDefaultPort || parsed.UserInfo.Length > 0)
            return WebhookUrlStatus.Invalid;

        if (!parsed.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal) || parsed.AbsolutePath.Length == PathPrefix.Length)
            return WebhookUrlStatus.Invalid;

        uri = parsed;
        return WebhookUrlStatus.Valid;
    }
}

/// <summary>
/// Builds the JSON posted to the webhook. Every piece of player text is put inside a code span or a code block: Discord
/// shows those as plain text, so a link (with or without a scheme, a bare domain or a discord.gg invite), a mention,
/// markdown or an emoji code in it stays inert and nothing in it is unfurled into an embed. The text only has to not
/// contain a backtick to be unable to leave its block, so backticks are replaced.
/// </summary>
public static class DiscordPayload
{
    /// <summary>Discord refuses a message over 2000 characters; this stays well under.</summary>
    public const int MaxContentLength = 1900;

    private const int MaxNameLength = 64;
    private const int MaxSummaryLength = 100;
    private const int MaxWhereLength = 64;
    private const int MaxClientTextLength = 700;
    private const int MaxServerContextLength = 600;

    /// <summary>The message flag that stops Discord making embeds for any link in the message.</summary>
    private const int SuppressEmbeds = 4;

    public static string Build(ReportNotification notification)
    {
        var content = BuildContent(notification);

        // allowed_mentions with an empty parse list means no mention in the message pings anyone, whatever it says.
        // SuppressEmbeds is a second layer: even a link that got through would not be unfurled.
        return JsonSerializer.Serialize(new
        {
            username = "Evergrove reports",
            content,
            allowed_mentions = new { parse = Array.Empty<string>() },
            flags = SuppressEmbeds
        });
    }

    public static string BuildContent(ReportNotification notification)
    {
        var builder = new StringBuilder();

        builder.Append(notification.Kind == ReportKind.Bug ? "**Bug report #" : "**Report #").Append(notification.Id).Append("** (")
            .Append(notification.Kind.ToString().ToLowerInvariant()).Append(") from ").Append(Inline(notification.ReporterName, MaxNameLength));

        if (notification.Kind != ReportKind.Bug && !string.IsNullOrEmpty(notification.TargetName))
            builder.Append(" about ").Append(Inline(notification.TargetName, MaxNameLength));

        if (!string.IsNullOrEmpty(notification.Summary))
            builder.Append(": ").Append(Inline(notification.Summary, MaxSummaryLength));

        builder.Append('\n').Append("Where: ").Append(Inline(notification.Zone, MaxWhereLength)).Append(' ').Append(Inline(notification.Position, MaxWhereLength));

        if (notification.ClientText.Length > 0)
            builder.Append("\nThe reporter's client says:\n").Append(Block(notification.ClientText, MaxClientTextLength));

        if (notification.ServerContext.Length > 0)
            builder.Append("\nThe server saw:\n").Append(Block(notification.ServerContext, MaxServerContextLength));

        builder.Append("\nIn game: !mod report ").Append(notification.Id);

        // The pieces are capped, so this is only a guard against a later change; it is not expected to cut anything.
        return builder.Length <= MaxContentLength ? builder.ToString() : builder.ToString(0, MaxContentLength);
    }

    /// <summary>Player text on one line, as a code span: no backtick, no line break, cut to the length.</summary>
    public static string Inline(string? text, int max)
    {
        var clean = Clean(text, max, keepLineBreaks: false);

        return clean.Length == 0 ? "`?`" : "`" + clean + "`";
    }

    /// <summary>Player text as a code block: no backtick, so no way to end the block, cut to the length.</summary>
    public static string Block(string? text, int max)
    {
        var clean = Clean(text, max, keepLineBreaks: true);

        return "```\n" + (clean.Length == 0 ? "(nothing)" : clean) + "\n```";
    }

    /// <summary>Backticks become apostrophes, control characters (and line breaks, if not kept) become spaces, then it is cut.</summary>
    private static string Clean(string? text, int max, bool keepLineBreaks)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, max + 3));

        foreach (var c in text)
        {
            if (c == '`')
                builder.Append('\'');
            else if (c == '\n' && keepLineBreaks)
                builder.Append(c);
            else if (char.IsControl(c))
                builder.Append(' ');
            else
                builder.Append(c);
        }

        return Cut(builder.ToString().Trim(), max);
    }

    /// <summary>Cuts to the length, not in the middle of a surrogate pair.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;

        var length = max - 3;

        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;

        return text[..length] + "...";
    }
}
