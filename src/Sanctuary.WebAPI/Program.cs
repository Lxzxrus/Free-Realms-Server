using System;
using System.Globalization;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NLog.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Database;
using Sanctuary.WebAPI.Endpoints;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Security;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls();

// Options
builder.Services.AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptionsWithValidateOnStart<WebAPIOptions>()
    .BindConfiguration(WebAPIOptions.Section)
    .Validate(x => x.TrustedProxies.TrueForAll(p => IPAddress.TryParse(p, out _) || System.Net.IPNetwork.TryParse(p, out _)),
        "WebAPI:TrustedProxies must hold IP addresses or CIDR ranges.")
    .ValidateOnStart();

var webAPIOptions = builder.Configuration.GetSection(WebAPIOptions.Section).Get<WebAPIOptions>() ?? new WebAPIOptions();

// Proxy Server / Load Balancer
// X-Forwarded-For and -Proto are honoured only from trusted proxies: loopback by default, so a TLS proxy on the
// same machine works with no setup, and a client talking to Kestrel directly can't pick its own address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    if (webAPIOptions.TrustedProxies.Count > 0)
    {
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in webAPIOptions.TrustedProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
                options.KnownProxies.Add(address);
            else if (System.Net.IPNetwork.TryParse(proxy, out var network))
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network.BaseAddress, network.PrefixLength));
        }
    }
});

builder.Services.Configure<ForwardedHeadersOptions>(builder.Configuration.GetSection("ForwardedHeadersOptions"));

// Abuse limits
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddWebAPIRateLimiting(webAPIOptions.RateLimits);

// Database
builder.Services.AddDatabase(builder.Configuration);

// Logging
builder.Logging.ClearProviders();

#if DEBUG

builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddHttpLogging(logging =>
{
    // Bodies carry passwords and session IDs, so they are never logged.
    logging.LoggingFields = HttpLoggingFields.All & ~(HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody);
});

#endif

var nlogConfigFile = builder.Environment.IsDevelopment()
    ? "NLog.Development.config"
    : "NLog.config";

builder.Logging.AddNLog(nlogConfigFile);

var app = builder.Build();

#if DEBUG

app.UseHttpLogging();

#endif

// Configure the HTTP request pipeline.

app.UseForwardedHeaders();

app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapPortraitEndpoints();

app.Run();

// For the ASP.NET test host in Sanctuary.WebAPI.Tests.
public partial class Program;