using System.Globalization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Extensions;
using Sanctuary.Database;
using Sanctuary.WebAPI.Endpoints;
using Sanctuary.WebAPI.Options;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls();

// Local settings (git-ignored), then the environment and command line again so they still win.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

// Proxy Server / Load Balancer
var forwardedHeaderSection = builder.Configuration.GetSection("ForwardedHeadersOptions");

if (forwardedHeaderSection is not null)
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaderSection);

// Options
builder.Services.AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptionsWithValidateOnStart<WebAPIOptions>()
    .BindConfiguration(WebAPIOptions.Section)
    .ValidateOnStart();

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
const bool isDebugBuild = true;
#else
const bool isDebugBuild = false;
#endif

if (!StartupChecks.Passes(app.Logger, isDebugBuild, StartupChecks.CheckBuild(isDebugBuild, app.Configuration)))
    return 1;

#if DEBUG

app.UseHttpLogging();

#endif

// Configure the HTTP request pipeline.

app.MapAuthEndpoints();
app.MapPortraitEndpoints();

app.Run();

return 0;
