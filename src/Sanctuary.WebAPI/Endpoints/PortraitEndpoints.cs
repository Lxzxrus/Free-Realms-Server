using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Security;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanctuary.WebAPI.Endpoints;

/// <summary>
/// Portrait uploads from the client. The client posts to the <c>Portrait:UploadUrl</c> it was launched with, which
/// login builds as <c>{PortraitUploadUrl}/{token}</c>; the token identifies the user, who must own the character.
/// Each character keeps at most two files of fixed dimensions, overwritten on every upload.
/// </summary>
public static class PortraitEndpoints
{
    // ContentType and Boundary are hardcoded in the client.
    private const string ClientContentType = "multipart/form-data; boundary=AaBb432101234bBaA";

    private static ILogger _logger = null!;
    private readonly static ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = [];

    // Portraits are PNG only, so uploads are read with the PNG codec alone: any other format is refused as unknown
    // before a decoder for it runs. SkipMetadata skips EXIF and text chunks. A PNG's ICC profile is still kept on load,
    // but only as raw bytes: ImageSharp 3.x parses a profile when its Header or Entries are read, which nothing here
    // does. ImageSharp's default configuration would parse TIFF and more from an uploaded file. As of 3.1.12 that
    // leaves none of its open advisories reachable from an upload: GHSA-v76p-62qx-wwq2 (tiled fax TIFF decoder),
    // GHSA-wmxv-xphr-5c9g (BigTIFF decoder), GHSA-j9gm-c75j-xc9q and GHSA-jjfr-hcj7-qf5w (TIFF encoder),
    // GHSA-j3p4-wp97-rph4 (histogram equalization, unused) and GHSA-gwg2-r3hj-4w44 (ICC profile parsing). The fixes
    // ship in 3.2.0 and 4.1.2, and both need a Six Labors license key to build in Release (Debug only warns).
    // Only the first frame is decoded. Each further frame of an animated PNG decodes to a whole canvas however small it
    // is in the file: 1,000 one-pixel frames in a 67 KB file came to about 250 MB, and the 1 MB upload limit fits some
    // 15,000 of them, about 3.7 GB from one request.
    internal static readonly DecoderOptions PngOnly = new()
    {
        Configuration = new Configuration(new PngConfigurationModule()),
        SkipMetadata = true,
        MaxFrames = 1
    };

    // The Gateway sends saved portraits on to other players' game clients, so a saved portrait keeps the upload's
    // pixels and nothing else (see PixelsOnly). Saving the decoded upload itself would copy its ICC and CICP profiles
    // (3.1.12 keeps those whatever SkipMetadata says), its gamma, resolution, transparency and palette chunks, its bit
    // depth and interlacing, and any animation frames into the file. Instead every portrait is written the same way:
    // 8-bit RGBA, not interlaced, with no ancillary chunks (SkipMetadata also drops the resolution chunk a new image
    // would get).
    private static readonly PngEncoder PortraitEncoder = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        InterlaceMethod = PngInterlaceMode.None,
        SkipMetadata = true
    };

    public static void MapPortraitEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(PortraitEndpoints));

        var maxRequestBytes = app.Services.GetRequiredService<IOptions<WebAPIOptions>>().Value.PortraitMaxRequestBytes;

        app.MapPost("/image/{token}", ImageHandlerAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimiting.ImagePolicy)
            .WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));
    }

    private static async Task<IResult> ImageHandlerAsync(
        HttpContext context,
        string token,
        CancellationToken cancellationToken,
        IOptions<WebAPIOptions> webAPIOptions,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        PortraitUploadTokens portraitUploadTokens,
        TimeProvider timeProvider)
    {
        var options = webAPIOptions.Value;

        // Everything up to the ownership check happens before the body is read.
        if (!portraitUploadTokens.TryValidate(token, out var userId))
        {
            _logger.LogWarning("Portrait upload refused, invalid or expired token. ( Address: {Address} )", ClientAddress.GetKey(context));

            return Results.Unauthorized();
        }

        if (context.Request.ContentType != ClientContentType)
        {
            _logger.LogWarning("Invalid Content-Type: {ContentType}", context.Request.ContentType);

            return Results.BadRequest("Invalid Content-Type");
        }

        if (context.Request.ContentLength > options.PortraitMaxRequestBytes)
        {
            _logger.LogWarning("Portrait upload too large: {Length} bytes. ( UserId: {UserId} )", context.Request.ContentLength, userId);

            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (maxRequestBodySizeFeature is { IsReadOnly: false })
            maxRequestBodySizeFeature.MaxRequestBodySize = options.PortraitMaxRequestBytes;

        IFormCollection form;

        try
        {
            context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions
            {
                MultipartBodyLengthLimit = options.PortraitMaxRequestBytes,
                ValueCountLimit = 8
            }));

            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning("Portrait upload body rejected: {Message} ( UserId: {UserId} )", ex.Message, userId);

            return Results.StatusCode(ex.StatusCode);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning("Portrait upload body rejected: {Message} ( UserId: {UserId} )", ex.Message, userId);

            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // ImageType is hardcoded in the client.
        if (form["imageType"] != "portrait")
        {
            _logger.LogWarning("Invalid imageType: {ImageType}", form["imageType"].ToString());

            return Results.BadRequest("Invalid imageType.");
        }

        // The client sends the character's player guid, which is also the folder the Gateway reads portraits from.
        if (!ulong.TryParse(form["characterId"], out var characterGuid) || !IsPlayerGuid(characterGuid))
        {
            _logger.LogWarning("Invalid characterId: {CharacterId}", form["characterId"].ToString());

            return Results.BadRequest("Invalid characterId.");
        }

        var characterId = GuidHelper.GetPlayerId(characterGuid);

        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var owner = await dbContext.Characters
                .Where(x => x.Id == characterId)
                .Select(x => new { x.UserId, x.User.LockedUntil })
                .FirstOrDefaultAsync(cancellationToken);

            if (owner is null || owner.UserId != userId)
            {
                _logger.LogWarning("Portrait upload refused, user {UserId} doesn't own character {CharacterGuid}.", userId, characterGuid);

                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (owner.LockedUntil > timeProvider.GetUtcNow())
            {
                _logger.LogWarning("Portrait upload refused, user {UserId} is banned.", userId);

                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        if (form.Files.Count == 0 || form.Files.Count > 2)
        {
            _logger.LogWarning("Invalid file count: {Count}", form.Files.Count);

            return Results.BadRequest("Invalid file count.");
        }

        // Check every file before writing any, so a bad request leaves the stored portrait untouched.
        var images = new List<(string FileName, Image<Rgba32> Image, uint Crc)>();

        try
        {
            foreach (var file in form.Files)
            {
                var result = await TryLoadImageAsync(file, images, cancellationToken);

                if (result is not null)
                    return result;
            }

            var saveDirectory = Path.Combine(options.ImagesDirectory, characterGuid.ToString());

            Directory.CreateDirectory(saveDirectory);

            foreach (var (fileName, image, _) in images)
            {
                var savePath = Path.Combine(saveDirectory, fileName);

                var fileLock = _fileLocks.GetOrAdd(savePath, _ => new SemaphoreSlim(1, 1));

                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    await image.SaveAsPngAsync(savePath, PortraitEncoder, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving uploaded file for character {CharacterGuid}", characterGuid);

                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
                finally
                {
                    fileLock.Release();
                }
            }
        }
        finally
        {
            foreach (var (_, image, _) in images)
                image.Dispose();
        }

        // Upstream OSFR: the Gateway refreshes a player's portrait when this file changes (Player, portrait.crc).
        // Its folder is PortraitStorage's (SANCTUARY_PORTRAIT_IMAGE_ROOT, else "Images"), so set that variable to
        // the same folder as WebAPI:ImagesDirectory when they differ.
        var portrait = images.FirstOrDefault(image => image.FileName == "portrait.png");

        if (portrait.Image is not null)
        {
            await File.WriteAllBytesAsync(Path.Combine(options.ImagesDirectory, characterGuid.ToString(), "portrait.crc"),
                BitConverter.GetBytes(portrait.Crc), cancellationToken);
        }

        _logger.LogDebug("Successfully uploaded portrait for character {CharacterGuid}.", characterGuid);

        return Results.Ok();
    }

    /// <summary>Validates one file and adds it to <paramref name="images"/>, or returns the error to send.</summary>
    private static async Task<IResult?> TryLoadImageAsync(IFormFile file, List<(string FileName, Image<Rgba32> Image, uint Crc)> images, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            _logger.LogWarning("Invalid file name: {FileName}", file.FileName);

            return Results.BadRequest("Invalid file name.");
        }

        if (file.ContentType != "image/png")
        {
            _logger.LogWarning("Invalid file name: {FileName}", file.FileName);

            return Results.BadRequest("Invalid file name.");
        }

        // Names are hardcoded in the client.
        if (file.Name != "thumbnailFile" && file.Name != "imageFile")
        {
            _logger.LogWarning("Invalid name: {Name}", file.Name);

            return Results.BadRequest("Invalid name.");
        }

        var fileName = Path.GetFileName(file.FileName);

        // File names are hardcoded in the client.
        if (fileName != "headshot.png" && fileName != "portrait.png")
        {
            _logger.LogWarning("Invalid file name: {FileName}", file.FileName);

            return Results.BadRequest("Invalid file name.");
        }

        if (images.Any(x => x.FileName == fileName))
        {
            _logger.LogWarning("Duplicate file name: {FileName}", fileName);

            return Results.BadRequest("Invalid file name.");
        }

        var (width, height) = file.Name == "thumbnailFile" ? (70, 70) : (180, 330);

        try
        {
            using var stream = file.OpenReadStream();

            // Read the header first, so a small file claiming huge dimensions is refused before it is decoded.
            var info = await Image.IdentifyAsync(PngOnly, stream, cancellationToken);

            if (info.Metadata.DecodedImageFormat is not PngFormat)
            {
                _logger.LogWarning("Invalid image format: {Format}", info.Metadata.DecodedImageFormat?.Name);

                return Results.BadRequest("Invalid image format.");
            }

            if (info.Width != width || info.Height != height)
            {
                _logger.LogWarning("Invalid {Name} size: {Width}x{Height}", file.Name, info.Width, info.Height);

                return Results.BadRequest($"Invalid {file.Name} size.");
            }

            // The CRC of the bytes as the client sent them, which the Gateway compares with the client's own
            // (upstream OSFR's portrait refresh, portrait.crc). The request size limit bounds this copy.
            stream.Position = 0;

            using var uploaded = new MemoryStream();
            await stream.CopyToAsync(uploaded, cancellationToken);
            var crc = PortraitStorage.GetPortraitCrc(uploaded.GetBuffer().AsSpan(0, (int)uploaded.Length));

            stream.Position = 0;

            using var decoded = await Image.LoadAsync<Rgba32>(PngOnly, stream, cancellationToken);

            images.Add((fileName, PixelsOnly(decoded), crc));

            return null;
        }
        catch (ImageFormatException ex)
        {
            _logger.LogWarning("Invalid image: {Message}", ex.Message);

            return Results.BadRequest("Invalid image format.");
        }
    }

    /// <summary>A new image holding the first frame's pixels and none of the upload's metadata.</summary>
    private static Image<Rgba32> PixelsOnly(Image<Rgba32> decoded)
    {
        var pixels = new Rgba32[decoded.Width * decoded.Height];

        decoded.CopyPixelDataTo(pixels);

        return Image.LoadPixelData<Rgba32>(pixels, decoded.Width, decoded.Height);
    }

    private static bool IsPlayerGuid(ulong guid)
    {
        // GuidHelper puts the guid type in the low 4 bits; players are type 1.
        return (guid & 0x0F) == 1 && guid >> 4 != 0;
    }
}
