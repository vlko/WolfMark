using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace WolfMark.Storage.Trash;

/// <summary>
/// Moves files to the Windows Recycle Bin via <c>SHFileOperationW</c> (FOF_ALLOWUNDO). Only for paths where
/// <see cref="CanRecycle"/> holds: elsewhere Windows deletes for good instead of recycling.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsRecycleBin : IRecycleBin
{
    /// <summary>
    /// True when <paramref name="path"/> is on a local fixed drive. Network shares and most removable drives have no
    /// Recycle Bin – a "recycle" there is a permanent delete (CLAUDE.md rule 5), so callers use a trash folder instead.
    /// </summary>
    public static bool CanRecycle(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrEmpty(root)
                && !root.StartsWith(@"\\", StringComparison.Ordinal)
                && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public Task MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(() => MoveToRecycleBin(fullPath), cancellationToken);

    private static unsafe void MoveToRecycleBin(string fullPath)
    {
        // pFrom must be double-null terminated.
        var from = Path.GetFullPath(fullPath) + "\0\0";
        fixed (char* pFrom = from)
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = PInvoke.FO_DELETE,
                pFrom = pFrom,
                // FOF_WANTNUKEWARNING: if Windows would destroy the item after all (Recycle Bin switched off for the
                // drive, item larger than the bin), it asks first instead of deleting silently; "No" aborts below.
                fFlags = (ushort)(FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOF_NOCONFIRMATION | FILEOPERATION_FLAGS.FOF_SILENT
                    | FILEOPERATION_FLAGS.FOF_NOERRORUI | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING),
            };

            var result = PInvoke.SHFileOperation(ref op);
            if (result != 0 || op.fAnyOperationsAborted)
            {
                throw new IOException($"Moving '{fullPath}' to the Recycle Bin failed (0x{result:X}).");
            }
        }
    }
}
