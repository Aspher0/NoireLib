using FluentAssertions;
using NoireLib.Remote;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Files cross in chunks outside the call body. These cover the four routes the bytes move on and the two
/// places a reference reaches a member.
/// </summary>
public sealed class NoireRemoteFileTests : IDisposable
{
    private readonly NoireRemoteServer server;
    private readonly HttpClient client;
    private readonly string spoolDirectory;

    public NoireRemoteFileTests()
    {
        spoolDirectory = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

        server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableFiles = true,
            MaxRequestBytes = 4096,
            MaxSpooledBytes = 1024,
            FileSpoolDirectory = spoolDirectory,
        });

        server.PublishType(typeof(FileProbe));
        server.Start();

        client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();

        try
        {
            if (Directory.Exists(spoolDirectory))
                Directory.Delete(spoolDirectory, true);
        }
        catch (IOException)
        {
        }
    }

    [NoireRemoteClass("Files")]
    public static class FileProbe
    {
        [NoireRemote]
        public static int Measure(NoireRemoteFile file) => file.ReadAllBytes().Length;

        [NoireRemote]
        public static string NameOf(NoireRemoteFile file) => file.Name;

        [NoireRemote]
        public static NoireRemoteFile Produce(int size)
        {
            var bytes = new byte[size];

            for (var index = 0; index < size; index++)
                bytes[index] = (byte)(index % 251);

            return NoireRemoteFile.FromBytes("produced.bin", bytes, "application/octet-stream");
        }
    }

    private string Url(string route) => "http://127.0.0.1:" + server.Port + route;

    private static byte[] Pattern(int size)
    {
        var bytes = new byte[size];

        for (var index = 0; index < size; index++)
            bytes[index] = (byte)(index % 251);

        return bytes;
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private async Task<NoireRemoteFileStatus> DeclareAsync(string name, byte[] bytes, string? sha = null)
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteFileRequest
            {
                Name = name,
                Size = bytes.LongLength,
                Sha256 = sha ?? HashOf(bytes),
            }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return NoireRemoteJson.Read<NoireRemoteFileStatus>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> PutAsync(string id, long offset, byte[] chunk)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files + "/" + id + "?offset=" + offset))
        {
            Content = new ByteArrayContent(chunk),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<NoireRemoteEnvelope> CallAsync(string member, object args)
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Member("Files", member)),
            new StringContent(
                NoireRemoteJson.Write(new NoireRemoteRequest { Args = NoireRemoteJson.ToToken(args) }),
                Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);

        return NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task AFileSentInChunks_ArrivesCompleteAndReachesAMember()
    {
        var bytes = Pattern(3000);
        var declared = await DeclareAsync("payload.bin", bytes);

        declared.ChunkBytes.Should().Be(server.Options.MaxRequestBytes);
        declared.Complete.Should().BeFalse();

        var chunk = 1000;

        for (var offset = 0; offset < bytes.Length; offset += chunk)
        {
            var slice = new byte[Math.Min(chunk, bytes.Length - offset)];
            Array.Copy(bytes, offset, slice, 0, slice.Length);

            using var response = await PutAsync(declared.Id, offset, slice);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var envelope = await CallAsync("Measure", new { file = new { _file = declared.Id } });

        // The reference field is written by hand, bypassing the type.
        envelope.Ok.Should().BeFalse("the reference field is $file and nothing else resolves");

        var proper = await CallAsync("Measure", new { file = new NoireRemoteFile { Id = declared.Id } });

        proper.Ok.Should().BeTrue();
        proper.ResultAs<int>().Should().Be(3000);
    }

    [Fact]
    public async Task AResumedChunk_PicksUpWhereItStopped()
    {
        var bytes = Pattern(2048);
        var declared = await DeclareAsync("resume.bin", bytes);

        var first = new byte[1024];
        Array.Copy(bytes, 0, first, 0, 1024);

        using (var response = await PutAsync(declared.Id, 0, first))
        {
            var status = NoireRemoteJson.Read<NoireRemoteFileStatus>(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

            status.Received.Should().Be(1024);
            status.Complete.Should().BeFalse();
        }

        var second = new byte[1024];
        Array.Copy(bytes, 1024, second, 0, 1024);

        using (var response = await PutAsync(declared.Id, 1024, second))
        {
            var status = NoireRemoteJson.Read<NoireRemoteFileStatus>(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

            status.Received.Should().Be(2048);
            status.Complete.Should().BeTrue();
        }

        (await CallAsync("Measure", new { file = new NoireRemoteFile { Id = declared.Id } }))
            .ResultAs<int>().Should().Be(2048);
    }

    [Fact]
    public async Task AChecksumMismatch_IsRefusedAndDropsTheTransfer()
    {
        var bytes = Pattern(512);
        var declared = await DeclareAsync("wrong.bin", bytes, HashOf(Pattern(511)));

        using var response = await PutAsync(declared.Id, 0, bytes);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.ChecksumMismatch);

        using var again = await PutAsync(declared.Id, 0, bytes);

        again.StatusCode.Should().Be(HttpStatusCode.NotFound, "a mismatched transfer is dropped, never left half written");
    }

    [Fact]
    public async Task AChunkRunningPastTheDeclaredSize_IsRefused()
    {
        var declared = await DeclareAsync("small.bin", Pattern(16));

        using var response = await PutAsync(declared.Id, 0, Pattern(64));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnIncompleteTransferUsedAsAnArgument_NamesTheMissingRange()
    {
        var bytes = Pattern(2048);
        var declared = await DeclareAsync("partial.bin", bytes);

        var first = new byte[512];
        Array.Copy(bytes, 0, first, 0, 512);

        using (await PutAsync(declared.Id, 0, first))
        {
        }

        var envelope = await CallAsync("Measure", new { file = new NoireRemoteFile { Id = declared.Id } });

        envelope.Ok.Should().BeFalse();
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.FileIncomplete);
        envelope.Error.Message.Should().Contain("512").And.Contain("2048");
    }

    [Fact]
    public async Task AnUnknownReference_IsRefused()
    {
        var envelope = await CallAsync("Measure", new { file = new NoireRemoteFile { Id = "nothing" } });

        envelope.Ok.Should().BeFalse();
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.FileNotFound);
    }

    [Fact]
    public async Task AReferenceReachingAMember_CarriesTheDeclaredName()
    {
        var bytes = Pattern(64);
        var declared = await DeclareAsync("named.bin", bytes);

        using (await PutAsync(declared.Id, 0, bytes))
        {
        }

        (await CallAsync("NameOf", new { file = new NoireRemoteFile { Id = declared.Id } }))
            .ResultAs<string>().Should().Be("named.bin");
    }

    [Fact]
    public async Task AMemberReturningAFile_AnswersAReferenceAndTheBytesAreFetched()
    {
        var envelope = await CallAsync("Produce", new { size = 900 });

        envelope.Ok.Should().BeTrue();

        var file = envelope.ResultAs<NoireRemoteFile>()!;

        file.Id.Should().NotBeNullOrEmpty();
        file.Name.Should().Be("produced.bin");
        file.Size.Should().Be(900);
        NoireRemoteJson.Write(file).Should().Contain("$file");

        using var response = await client.GetAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files + "/" + file.Id),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        fetched.Should().BeEquivalentTo(Pattern(900));
        HashOf(fetched).Should().Be(file.Sha256);
    }

    [Fact]
    public async Task ADownloadInSlices_ReadsTheWholeFile()
    {
        var envelope = await CallAsync("Produce", new { size = 900 });
        var file = envelope.ResultAs<NoireRemoteFile>()!;

        var whole = new byte[900];

        for (var offset = 0; offset < 900; offset += 300)
        {
            using var response = await client.GetAsync(
                Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files + "/" + file.Id + "?offset=" + offset + "&length=300"),
                TestContext.Current.CancellationToken);

            var slice = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

            slice.Length.Should().Be(300);
            Array.Copy(slice, 0, whole, offset, 300);
        }

        whole.Should().BeEquivalentTo(Pattern(900));
    }

    [Fact]
    public async Task ADroppedTransfer_IsGone()
    {
        var declared = await DeclareAsync("dropped.bin", Pattern(32));

        using var deleted = await client.DeleteAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files + "/" + declared.Id),
            TestContext.Current.CancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var after = await PutAsync(declared.Id, 0, Pattern(32));

        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AFileAboveTheSpoolThreshold_GoesToDisk()
    {
        var bytes = Pattern(2000);
        var declared = await DeclareAsync("spooled.bin", bytes);

        using (await PutAsync(declared.Id, 0, bytes))
        {
        }

        Directory.Exists(spoolDirectory).Should().BeTrue();
        Directory.GetFiles(spoolDirectory, "*.part").Should().ContainSingle(
            "the threshold is what keeps a large file out of the process's memory twice");

        (await CallAsync("Measure", new { file = new NoireRemoteFile { Id = declared.Id } }))
            .ResultAs<int>().Should().Be(2000);
    }

    [Fact]
    public async Task AFileDeclaredAboveTheCap_IsRefused()
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteFileRequest
            {
                Name = "huge.bin",
                Size = server.Options.MaxFileBytes + 1,
            }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ANonJsonBodyOnAMemberRoute_IsStillRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(NoireRemotePaths.Member("Files", "NameOf")))
        {
            Content = new ByteArrayContent(Pattern(16)),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "the file routes are the one place a body is not JSON");
    }

    [Fact]
    public async Task TheFileRoutes_AreNotServedWhenTheyAreTurnedOff()
    {
        server.Options.EnableFiles = false;

        try
        {
            using var response = await client.PostAsync(
                Url(NoireRemotePaths.Prefix + NoireRemotePaths.Files),
                new StringContent(NoireRemoteJson.Write(new NoireRemoteFileRequest { Name = "x", Size = 1 }), Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            server.Options.EnableFiles = true;
        }
    }

    [Fact]
    public void TheManifest_ListsFilesOnlyWhenTheyAreOn()
    {
        server.Manifest().Has(NoireRemoteFeatures.Files).Should().BeTrue();

        using var without = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableFiles = false,
        });

        without.PublishType(typeof(FileProbe));
        without.Manifest().Has(NoireRemoteFeatures.Files).Should().BeFalse();
    }
}
