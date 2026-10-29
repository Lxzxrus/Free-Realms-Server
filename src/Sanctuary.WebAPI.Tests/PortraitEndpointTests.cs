using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.WebAPI.Endpoints;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Cicp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanctuary.WebAPI.Tests;

[TestClass]
public class PortraitEndpointTests
{
    // Hardcoded in the client, unquoted.
    private const string ClientContentType = "multipart/form-data; boundary=AaBb432101234bBaA";

    private static readonly byte[] Thumbnail = Png(70, 70);
    private static readonly byte[] Portrait = Png(180, 330);

    [TestMethod]
    public async Task OwnerCanUploadTheirCharactersPortrait()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var directory = Path.Combine(host.ImagesDirectory, seed.AliceCharacterGuid.ToString());

        using (var headshot = Image.Load(Path.Combine(directory, "headshot.png")))
            Assert.AreEqual(70, headshot.Width);

        using (var portrait = Image.Load(Path.Combine(directory, "portrait.png")))
            Assert.AreEqual(330, portrait.Height);
    }

    [TestMethod]
    public async Task OldUnauthenticatedRouteIsGone()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        using var response = await PostAsync(host, "/image", seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsFalse(Directory.Exists(host.ImagesDirectory));
    }

    [TestMethod]
    [DataRow("/image/not-a-token")]
    [DataRow("/image/0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task MadeUpTokenIsUnauthorized(string path)
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsFalse(Directory.Exists(host.ImagesDirectory));
    }

    [TestMethod]
    public async Task TamperedTokenIsUnauthorized()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        // Bob's token, with its user id changed to Alice's (1). The signature no longer matches.
        var bobPath = await host.GetPortraitUploadPathAsync("bob");
        var token = bobPath["/image/".Length..];
        var forged = "/image/" + "0000000000000001" + token[16..];

        using var response = await PostAsync(host, forged, seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ExpiredTokenIsUnauthorized()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        host.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CannotUploadForSomeoneElsesCharacter()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, seed.BobCharacterGuid);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.IsFalse(Directory.Exists(Path.Combine(host.ImagesDirectory, seed.BobCharacterGuid.ToString())));
    }

    [TestMethod]
    public async Task CannotUploadForACharacterThatDoesNotExist()
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");
        var missing = (999UL << 4) | 1;

        using var response = await PostAsync(host, path, missing);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.IsFalse(Directory.Exists(host.ImagesDirectory));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("16")]
    [DataRow("18")]
    [DataRow("-1")]
    [DataRow("abc")]
    public async Task CharacterIdMustBeAPlayerGuid(string characterId)
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, characterId, Thumbnail, Portrait);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task BannedUserCannotUploadWithAnUnexpiredToken()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        // Login refuses banned users, so a banned user's token can only predate the ban. Ban Alice after login.
        var path = await host.GetPortraitUploadPathAsync("alice");

        await BanAsync(host, "alice");

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OversizedRequestIsRefusedBeforeItIsRead()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:PortraitMaxRequestBytes"] = "300000" });
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), Thumbnail, new byte[400_000]);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.IsFalse(Directory.Exists(host.ImagesDirectory));
    }

    [TestMethod]
    public async Task WrongDimensionsAreRefusedAndNothingIsWritten()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        // A good thumbnail with a bad portrait: the thumbnail must not be written either.
        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), Thumbnail, Png(181, 330));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(File.Exists(Path.Combine(host.ImagesDirectory, seed.AliceCharacterGuid.ToString(), "headshot.png")));
    }

    [TestMethod]
    public async Task HugeDeclaredDimensionsAreRefused()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        // A valid PNG header claiming 60000x60000 (14 GB as RGBA). The endpoint refuses it from the header alone.
        var bomb = Png(1, 1);
        bomb[16] = 0; bomb[17] = 0; bomb[18] = 0xEA; bomb[19] = 0x60;
        bomb[20] = 0; bomb[21] = 0; bomb[22] = 0xEA; bomb[23] = 0x60;

        // IHDR's CRC covers its type and data (bytes 12-28).
        var crc = Crc32(bomb.AsSpan(12, 17));
        bomb[29] = (byte)(crc >> 24); bomb[30] = (byte)(crc >> 16); bomb[31] = (byte)(crc >> 8); bomb[32] = (byte)crc;

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), Thumbnail, bomb);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task NotAPngIsRefused()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), Thumbnail, "not an image"u8.ToArray());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    [DataRow(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 }, DisplayName = "TIFF")]
    [DataRow(new byte[] { 0x49, 0x49, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, DisplayName = "BigTIFF")]
    [DataRow(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 }, DisplayName = "JPEG")]
    [DataRow(new byte[] { 0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00 }, DisplayName = "BMP")]
    public async Task OtherImageFormatsAreRefusedWithoutBeingDecoded(byte[] header)
    {
        // Uploads are read with the PNG codec alone, so a TIFF (where ImageSharp's open advisories are) never reaches
        // a TIFF decoder.
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), Thumbnail, header);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SavedPortraitsKeepThePixelsAndNothingElse()
    {
        // The Gateway sends saved portraits on to other players' game clients, so an upload's ICC and CICP profiles,
        // text, EXIF, XMP, gamma, resolution and animation frames must not reach the saved files. Only its pixels do.
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        var thumbnail = PngWithMetadata(70, 70);
        var portrait = Animated(PngWithMetadata(180, 330), extraFrames: 2);

        // The uploads really do carry all of that.
        CollectionAssert.IsSubsetOf(new[] { "gAMA", "cICP", "iCCP", "pHYs", "eXIf", "iTXt", "tEXt", "zTXt" }, PngChunks(thumbnail));

        using (var uploaded = Image.Load<Rgba32>(portrait))
        {
            Assert.AreEqual(3, uploaded.Frames.Count);
            Assert.IsNotNull(uploaded.Metadata.IccProfile);
        }

        using var response = await PostAsync(host, path, seed.AliceCharacterGuid.ToString(), thumbnail, portrait);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());

        foreach (var name in new[] { "headshot.png", "portrait.png" })
        {
            var saved = File.ReadAllBytes(Path.Combine(host.ImagesDirectory, seed.AliceCharacterGuid.ToString(), name));
            var chunks = PngChunks(saved);

            // Nothing but the header, the image data and the end.
            CollectionAssert.AreEqual(new[] { "IHDR", "IDAT", "IEND" }, chunks.Distinct().ToArray(), $"{name}: {string.Join(' ', chunks)}");

            foreach (var chunk in new[] { "iCCP", "tEXt", "zTXt", "iTXt", "eXIf" })
                CollectionAssert.DoesNotContain(chunks, chunk, $"{name} kept {chunk}.");

            using var image = Image.Load<Rgba32>(saved);

            Assert.AreEqual(1, image.Frames.Count);
            Assert.AreEqual(new Rgba32(200, 100, 50), image[0, 0]);
        }
    }

    [TestMethod]
    public void OnlyTheFirstFrameOfAnAnimatedPngIsDecoded()
    {
        // Each extra frame of an animated PNG decodes to a full canvas however small it is in the file, so without the
        // limit one upload could make the WebAPI allocate gigabytes. The saved files can't show it, as they are made
        // from the first frame either way.
        var animated = Animated(Portrait, extraFrames: 3);

        using (var everyFrame = Image.Load<Rgba32>(animated))
            Assert.AreEqual(4, everyFrame.Frames.Count);

        using var firstFrame = Image.Load<Rgba32>(PortraitEndpoints.PngOnly, animated);

        Assert.AreEqual(1, firstFrame.Frames.Count);
    }

    [TestMethod]
    public async Task StorageStaysAtTwoFilesPerCharacter()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        for (var i = 0; i < 3; i++)
        {
            using var response = await PostAsync(host, path, seed.AliceCharacterGuid);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        var files = Directory.GetFiles(Path.Combine(host.ImagesDirectory, seed.AliceCharacterGuid.ToString()))
            .Select(Path.GetFileName)
            .Order()
            .ToArray();

        // Still a fixed set, overwritten on every upload: the two images and upstream OSFR's 4-byte portrait.crc,
        // which the Gateway reads to refresh a changed portrait.
        CollectionAssert.AreEqual(new[] { "headshot.png", "portrait.crc", "portrait.png" }, files);
        Assert.AreEqual(1, Directory.GetDirectories(host.ImagesDirectory).Length);
    }

    [TestMethod]
    public async Task DuplicateFilesInOneRequestAreRefused()
    {
        using var host = new WebAPIHost();
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        using var content = Form(seed.AliceCharacterGuid.ToString());
        content.Add(PngPart(Thumbnail), "thumbnailFile", "headshot.png");
        content.Add(PngPart(Thumbnail), "thumbnailFile", "headshot.png");

        using var response = await host.CreateClient(WebAPIHost.DefaultAddress).PostAsync(path, content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadIsRateLimitedPerAddress()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:ImagePerMinute"] = "2" });
        var seed = await host.StartAsync();

        var path = await host.GetPortraitUploadPathAsync("alice");

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await PostAsync(host, path, seed.AliceCharacterGuid);
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await PostAsync(host, path, seed.AliceCharacterGuid);

        Assert.AreEqual((HttpStatusCode)429, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter?.Delta);
    }

    [TestMethod]
    public async Task PortraitUploadsOffWhenNoUrlIsConfigured()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:PortraitUploadUrl"] = "" });
        await host.StartAsync();

        using var response = await host.LoginAsync("alice");
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();

        Assert.AreEqual("AssetDelivery:IndirectServerAddress=http://assets.example", body!.LaunchArguments);
    }

    private static Task<HttpResponseMessage> PostAsync(WebAPIHost host, string path, ulong characterGuid)
    {
        return PostAsync(host, path, characterGuid.ToString(), Thumbnail, Portrait);
    }

    private static async Task<HttpResponseMessage> PostAsync(WebAPIHost host, string path, string characterId, byte[] thumbnail, byte[] portrait)
    {
        using var content = Form(characterId);

        content.Add(PngPart(thumbnail), "thumbnailFile", "headshot.png");
        content.Add(PngPart(portrait), "imageFile", "portrait.png");

        return await host.CreateClient(WebAPIHost.DefaultAddress).PostAsync(path, content);
    }

    private static MultipartFormDataContent Form(string characterId)
    {
        var content = new MultipartFormDataContent("AaBb432101234bBaA")
        {
            { new StringContent("portrait"), "imageType" },
            { new StringContent(characterId), "characterId" }
        };

        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", ClientContentType);

        return content;
    }

    private static ByteArrayContent PngPart(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);

        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        return file;
    }

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 100, 50));
        using var stream = new MemoryStream();

        image.SaveAsPng(stream);

        return stream.ToArray();
    }

    // A PNG carrying every ancillary chunk ImageSharp writes from an image's metadata.
    private static byte[] PngWithMetadata(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 100, 50));

        image.Metadata.IccProfile = new IccProfile(new byte[200]);
        image.Metadata.CicpProfile = new CicpProfile(1, 13, 0, true);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Software, "uploader");
        image.Metadata.XmpProfile = new XmpProfile("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray());

        var png = image.Metadata.GetPngMetadata();
        png.Gamma = 0.45455f;
        // Written as tEXt, as zTXt (long enough to be compressed) and as iTXt (it has a language).
        png.TextData.Add(new PngTextData("Comment", "uploader text", "", ""));
        png.TextData.Add(new PngTextData("Description", new string('z', 2000), "", ""));
        png.TextData.Add(new PngTextData("Title", "uploader title", "en", "Title"));

        using var stream = new MemoryStream();

        image.SaveAsPng(stream);

        return stream.ToArray();
    }

    // The 8-bit RGBA PNG made animated: an acTL chunk before its image data and one-pixel frames before its end, each
    // an fcTL and an fdAT chunk, as an attacker would make them. ImageSharp 3.1's Identify skips 4 bytes too many after
    // each fdAT (the sequence number twice) and so rejects many animated PNGs, its own included, but 4 spare bytes
    // after each frame get one past it. Load reads frames correctly and steps over the spare bytes.
    private static byte[] Animated(byte[] png, int extraFrames)
    {
        using var output = new MemoryStream();

        output.Write(png, 0, 8);

        // A frame's data is one scanline: filter type 0, then one opaque blue pixel.
        var frameData = Zlib([0, 0, 0, 255, 255]);
        var sequence = 0u;
        var started = false;

        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);

            if (type == "IDAT" && !started)
            {
                // Frame count, then plays (0 is forever). The image itself isn't one of the frames.
                var animationControl = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(animationControl, extraFrames);
                WriteChunk(output, "acTL", animationControl);

                started = true;
            }

            if (type == "IEND")
            {
                for (var i = 0; i < extraFrames; i++)
                {
                    // Sequence number, 1x1 at (0, 0), shown for 1/100 s.
                    var frameControl = new byte[26];
                    BinaryPrimitives.WriteUInt32BigEndian(frameControl, sequence++);
                    BinaryPrimitives.WriteInt32BigEndian(frameControl.AsSpan(4), 1);
                    BinaryPrimitives.WriteInt32BigEndian(frameControl.AsSpan(8), 1);
                    BinaryPrimitives.WriteUInt16BigEndian(frameControl.AsSpan(20), 1);
                    BinaryPrimitives.WriteUInt16BigEndian(frameControl.AsSpan(22), 100);
                    WriteChunk(output, "fcTL", frameControl);

                    var frame = new byte[4 + frameData.Length];
                    BinaryPrimitives.WriteUInt32BigEndian(frame, sequence++);
                    frameData.CopyTo(frame, 4);
                    WriteChunk(output, "fdAT", frame);

                    output.Write(new byte[4]);
                }
            }

            output.Write(png, offset, 12 + length);
            offset += 12 + length;
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];

        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);

        // The CRC covers the type and the data.
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(chunk.AsSpan(4, 4 + data.Length)));

        stream.Write(chunk);
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);

        return output.ToArray();
    }

    // The chunk types of a PNG, in file order.
    private static string[] PngChunks(byte[] png)
    {
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        var chunks = new List<string>();

        // Each chunk is its data length (4 bytes), type (4), data, then a CRC (4).
        for (var offset = 8; offset < png.Length; offset += 12 + BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset)))
            chunks.Add(Encoding.ASCII.GetString(png, offset + 4, 4));

        return chunks.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc ^= b;

            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    private static async Task BanAsync(WebAPIHost host, string username)
    {
        var dbContextFactory = host.Services.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        var user = dbContext.Users.Single(x => x.Username == username);
        user.LockedUntil = DateTimeOffset.UtcNow.AddDays(1);

        await dbContext.SaveChangesAsync();
    }
}
