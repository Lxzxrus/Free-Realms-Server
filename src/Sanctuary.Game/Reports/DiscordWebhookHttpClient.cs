using System;
using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;

namespace Sanctuary.Game.Reports;

/// <summary>How the Discord webhook's <see cref="HttpClient"/> is registered.</summary>
public static class DiscordWebhookHttpClient
{
    public const string Name = "DiscordWebhook";

    /// <summary>
    /// A client with a 5 second timeout that follows no redirects, with the factory's own loggers removed. Those log the
    /// full request address at Information level, and a Discord webhook address carries its secret token in the path
    /// (.NET only hides the query string), so with them the token would reach the console, the log files and anyone a
    /// log is pasted to.
    /// </summary>
    public static IHttpClientBuilder AddDiscordWebhookClient(this IServiceCollection services) =>
        services.AddHttpClient(Name, client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
}
