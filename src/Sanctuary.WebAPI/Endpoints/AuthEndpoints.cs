using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MiniValidation;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.WebAPI.Models;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Security;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class AuthEndpoints
{
    /// <summary>Login and register bodies are two short strings; nothing legitimate comes close.</summary>
    private const long MaxRequestBytes = 4 * 1024;

    private static ILogger _logger = null!;

    /// <summary>Checked when the username doesn't exist, so a missing account takes as long as a wrong password.</summary>
    private static readonly Lazy<string> _dummyPasswordHash = new(() => BC.HashPassword(Guid.NewGuid().ToString("N"), BC.GenerateSalt()));

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(AuthEndpoints));

        app.MapPost("/login", LoginHandlerAsync)
            .RequireRateLimiting(RateLimiting.LoginPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));

        app.MapPost("/register", RegisterHandlerAsync)
            .RequireRateLimiting(RateLimiting.RegisterPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));
    }

    private static async Task<IResult> LoginHandlerAsync(
        HttpContext context,
        LoginRequestModel request,
        CancellationToken cancellationToken,
        IOptionsSnapshot<WebAPIOptions> webAPIOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        LoginThrottle loginThrottle)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        var address = ClientAddress.GetKey(context);

        // Checked before the password, so a locked-out attacker learns nothing from further guesses.
        if (loginThrottle.GetRetryAfter(request.Username, address) is { } retryAfter)
        {
            _logger.LogWarning("Login refused, locked out for {RetryAfter}. ( Username: {Username}, Address: {Address} )", retryAfter, request.Username, address);

            context.Response.Headers.RetryAfter = RateLimiting.RetryAfterSeconds(retryAfter);

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dbUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

        if (dbUser is null)
        {
            BC.Verify(request.Password, _dummyPasswordHash.Value);

            loginThrottle.RecordFailure(request.Username, address);

            _logger.LogWarning("Login failed, user not found for username: {Username}", request.Username);

            return Results.Unauthorized();
        }

        if (!BC.Verify(request.Password, dbUser.Password))
        {
            loginThrottle.RecordFailure(request.Username, address);

            _logger.LogWarning("Login failed, invalid password for username: {Username}", request.Username);

            return Results.Unauthorized();
        }

        loginThrottle.RecordSuccess(request.Username, address);

        if (dbUser.LockedUntil > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login failed, account is banned for username: {Username}", request.Username);

            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        dbUser.Session = Guid.NewGuid().ToString("N");
        dbUser.SessionCreated = DateTimeOffset.UtcNow;

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
        {
            _logger.LogError("Failed to update session info for username: {Username}", dbUser.Username);

            return Results.InternalServerError();
        }

        return Results.Ok(new LoginResponseModel
        {
            SessionId = dbUser.Session,
            LaunchArguments = webAPIOptions.Value.LaunchArguments
        });
    }

    private static async Task<IResult> RegisterHandlerAsync(
        RegisterRequestModel request,
        CancellationToken cancellationToken,
        IOptions<WebAPIOptions> webAPIOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var usernameTaken = await dbContext.Users.AnyAsync(x => x.Username == request.Username, cancellationToken);

        if (usernameTaken)
        {
            _logger.LogWarning("Registration failed, username already taken {Username}", request.Username);

            return Results.Conflict();
        }

        var salt = BC.GenerateSalt();
        var hashedPassword = BC.HashPassword(request.Password, salt);

        var dbUser = new DbUser
        {
            Username = request.Username,
            Password = hashedPassword,
            IsMember = webAPIOptions.Value.MemberByDefault ?? true,
        };

        await dbContext.Users.AddAsync(dbUser, cancellationToken);

        if (await dbContext.SaveChangesAsync(cancellationToken) <= 0)
        {
            _logger.LogError("Failed to add new user: {Username}", request.Username);

            return Results.InternalServerError();
        }

        return Results.Ok();
    }
}