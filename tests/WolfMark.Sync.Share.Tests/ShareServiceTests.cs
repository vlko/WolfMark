using Microsoft.Extensions.Logging.Abstractions;

namespace WolfMark.Sync.Share.Tests;

/// <summary>The file through which the phone finds this computer.</summary>
public sealed class ShareServiceTests : IDisposable
{
    private readonly ShareFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static ShareDeviceInfo Info(params string[] addresses) => new()
    {
        Id = "abc123",
        Name = "DESKTOP",
        Port = 47821,
        Addresses = [.. addresses],
        Key = Convert.ToBase64String(ShareFixture.Key),
    };

    [Fact]
    public async Task The_device_file_is_written_once_and_only_rewritten_when_something_changes()
    {
        Assert.True(await ShareDeviceFile.PublishAsync(_f.Root, Info("192.168.1.20"), CancellationToken.None));
        var path = Path.Combine(_f.Root, ".wolfmark", "share", "abc123.json");
        var written = File.GetLastWriteTimeUtc(path);

        Assert.False(await ShareDeviceFile.PublishAsync(_f.Root, Info("192.168.1.20"), CancellationToken.None));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));

        Assert.True(await ShareDeviceFile.PublishAsync(_f.Root, Info("10.0.0.7", "192.168.1.20"), CancellationToken.None));
        var read = await ShareDeviceFile.ReadAsync(_f.Root, "abc123", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(["10.0.0.7", "192.168.1.20"], read.Addresses);
        Assert.Equal(47821, read.Port);
        Assert.Equal("DESKTOP", read.Name);
        Assert.Equal(ShareFixture.Key, Convert.FromBase64String(read.Key));
        // The names the Android app reads.
        var json = await File.ReadAllTextAsync(path);
        foreach (var name in new[] { "\"version\": 1", "\"id\"", "\"name\"", "\"port\"", "\"addresses\"", "\"key\"" })
        {
            Assert.Contains(name, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_device_file_is_not_a_note_and_can_be_taken_back()
    {
        await ShareDeviceFile.PublishAsync(_f.Root, Info("192.168.1.20"), CancellationToken.None);

        var notes = new List<string>();
        await foreach (var note in _f.Engine.EnumerateAsync())
        {
            notes.Add(note.Path.RelativePath);
        }

        Assert.Empty(notes);
        ShareDeviceFile.Remove(_f.Root, "abc123");
        Assert.Null(await ShareDeviceFile.ReadAsync(_f.Root, "abc123", CancellationToken.None));
        ShareDeviceFile.Remove(_f.Root, "abc123"); // already gone
    }

    [Fact]
    public async Task The_running_service_announces_its_port_and_addresses_and_receives()
    {
        using var stop = new CancellationTokenSource();
        var options = new ShareOptions("abc123", "DESKTOP", 0, ShareFixture.Key);
        var service = new ShareService(_f.Engine, options, _f.Time, NullLoggerFactory.Instance, () => ["192.168.1.20"]);
        var run = service.RunAsync(stop.Token);

        ShareDeviceInfo? info = null;
        for (var i = 0; i < 200 && info is null; i++)
        {
            await Task.Delay(25);
            info = await ShareDeviceFile.ReadAsync(_f.Root, "abc123", CancellationToken.None);
        }

        Assert.NotNull(info);
        Assert.Equal(service.Port, info.Port);
        Assert.NotEqual(0, info.Port);
        Assert.Equal(["192.168.1.20"], info.Addresses);

        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync(System.Net.IPAddress.Loopback, info.Port);
            var stream = client.GetStream();
            await stream.WriteAsync(ShareFrame.Seal(ShareFrameKind.Request, Convert.FromBase64String(info.Key), _f.Message("through the service").Encode()));
            var header = new byte[ShareFrame.HeaderLength];
            await stream.ReadExactlyAsync(header);
            Assert.True(ShareFrame.TryReadHeader(header, out _, out var length));
            await stream.ReadExactlyAsync(new byte[length]);
        }

        Assert.Single(_f.Notes());
        await stop.CancelAsync();
        await run;
        await service.DisposeAsync();
    }

    [Fact]
    public void Local_addresses_are_usable_IPv4_addresses()
    {
        foreach (var address in ShareDeviceFile.LocalAddresses())
        {
            Assert.True(System.Net.IPAddress.TryParse(address, out var ip));
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, ip.AddressFamily);
            Assert.False(System.Net.IPAddress.IsLoopback(ip));
            Assert.False(address.StartsWith("169.254.", StringComparison.Ordinal));
        }
    }
}
