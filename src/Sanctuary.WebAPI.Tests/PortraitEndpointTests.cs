using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;

using SixLabors.ImageSharp;
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

        CollectionAssert.AreEqual(new[] { "headshot.png", "portrait.png" }, files);
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
