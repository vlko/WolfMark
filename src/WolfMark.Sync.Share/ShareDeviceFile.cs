using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolfMark.Storage;

namespace WolfMark.Sync.Share;

/// <summary>
/// How the phone finds and trusts this computer (ADR 0017): a small file in the notes folder, which the phone reads
/// through OneDrive. Whoever can read it can read the notes anyway, so the key adds no new secret to protect.
/// </summary>
public sealed class ShareDeviceInfo
{
    public int Version { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    /// <summary>The computer's name, shown on the phone.</summary>
    public string Name { get; set; } = string.Empty;

    public int Port { get; set; }

    /// <summary>IPv4 addresses the phone may reach this computer on (home network, VPN).</summary>
    public List<string> Addresses { get; set; } = [];

    /// <summary>The 32-byte key, base64.</summary>
    public string Key { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShareDeviceInfo))]
internal sealed partial class ShareJsonContext : JsonSerializerContext;

public static class ShareDeviceFile
{
    /// <summary>Under <c>.wolfmark</c>: synchronised with the notes, invisible to the watcher and the index.</summary>
    public const string RelativeDirectory = ".wolfmark/share";

    public static string GetPath(string root, string deviceId) =>
        Path.Combine(root, ".wolfmark", "share", deviceId + ".json");

    /// <summary>Writes the file when it is missing or says something else; an unchanged file is left alone (no sync churn).</summary>
    /// <returns>True when the file was written.</returns>
    public static async Task<bool> PublishAsync(string root, ShareDeviceInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        var path = GetPath(root, info.Id);
        var json = JsonSerializer.Serialize(info, ShareJsonContext.Default.ShareDeviceInfo);
        try
        {
            if (string.Equals(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), json, StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
        }

        await AtomicFile.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task<ShareDeviceInfo?> ReadAsync(string root, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(GetPath(root, deviceId), cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ShareJsonContext.Default.ShareDeviceInfo);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Receiving was switched off: the phone must stop trying this computer.</summary>
    public static void Remove(string root, string deviceId)
    {
        var path = GetPath(root, deviceId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// IPv4 addresses of the interfaces that are up, without loopback and self-assigned (169.254) ones, sorted so
    /// that an unchanged network gives an unchanged file.
    /// </summary>
    public static List<string> LocalAddresses()
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || System.Net.IPAddress.IsLoopback(address))
                {
                    continue;
                }

                var bytes = address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    continue;
                }

                result.Add(address.ToString());
            }
        }

        return [.. result];
    }
}
