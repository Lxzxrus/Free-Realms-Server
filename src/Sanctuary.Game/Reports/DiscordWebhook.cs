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

/// <summary>Builds the JSON posted to the webhook. Player text can never ping anyone or make a link.</summary>
public static class DiscordPayload
{
    /// <summary>Discord refuses a message over 2000 characters; this stays well under.</summary>
    public const int MaxContentLength = 1900;

    private const int MaxClientTextLength = 700;
    private const int MaxServerContextLength = 600;
    private const char ZeroWidthSpace = '​';

    public static string Build(ReportNotification notification)
    {
        var content = BuildContent(notification);

        // allowed_mentions with an empty parse list means no mention in the message pings anyone, whatever it says.
        return JsonSerializer.Serialize(new
        {
            username = "Evergrove reports",
            content,
            allowed_mentions = new { parse = Array.Empty<string>() }
        });
    }

    public static string BuildContent(ReportNotification notification)
    {
        var builder = new StringBuilder();

        builder.Append(notification.Kind == ReportKind.Bug ? "**Bug report #" : "**Report #").Append(notification.Id).Append("** (")
            .Append(notification.Kind.ToString().ToLowerInvariant()).Append(") from **").Append(Escape(notification.ReporterName)).Append("**");

        if (notification.Kind != ReportKind.Bug && !string.IsNullOrEmpty(notification.TargetName))
            builder.Append(" about **").Append(Escape(notification.TargetName)).Append("**");

        if (!string.IsNullOrEmpty(notification.Summary))
            builder.Append(": ").Append(Escape(Cut(notification.Summary, 100)));

        builder.Append('\n').Append("Where: ").Append(Escape(notification.Zone)).Append(' ').Append(Escape(notification.Position));

        if (notification.ClientText.Length > 0)
            builder.Append("\nThe reporter's client says:\n").Append(Escape(Cut(notification.ClientText, MaxClientTextLength)));

        if (notification.ServerContext.Length > 0)
            builder.Append("\nThe server saw:\n").Append(Escape(Cut(notification.ServerContext, MaxServerContextLength)));

        builder.Append("\nIn game: !mod report ").Append(notification.Id);

        return Cut(builder.ToString(), MaxContentLength);
    }

    /// <summary>
    /// Makes player text plain text in Discord: markdown characters are escaped, an <c>@</c> is broken so
    /// <c>@everyone</c> and <c>@here</c> are not mentions, <c>&lt;</c> is escaped so <c>&lt;@id&gt;</c> is not one, and a
    /// link's <c>://</c> is broken so it is not clickable or embedded.
    /// </summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length + 16);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            switch (c)
            {
                case '\\':
                case '*':
                case '_':
                case '~':
                case '`':
                case '|':
                case '>':
                case '<':
                case '#':
                case '[':
                case ']':
                case '(':
                case ')':
                    builder.Append('\\').Append(c);
                    break;

                case '@':
                    builder.Append('@').Append(ZeroWidthSpace);
                    break;

                case ':' when i + 2 < text.Length && text[i + 1] == '/' && text[i + 2] == '/':
                    builder.Append(':').Append(ZeroWidthSpace);
                    break;

                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Cuts to the length, not in the middle of a surrogate pair or after a lone escaping backslash.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;

        var length = max - 3;

        if (char.IsHighSurrogate(text[length - 1]))
            length--;

        // An odd run of backslashes at the cut would escape the dots.
        var backslashes = 0;

        for (var i = length - 1; i >= 0 && text[i] == '\\'; i--)
            backslashes++;

        if (backslashes % 2 == 1)
            length--;

        return text[..length] + "...";
    }
}
