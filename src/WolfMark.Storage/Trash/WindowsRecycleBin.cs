using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace WolfMark.Storage.Trash;

/// <summary>Moves files to the Windows Recycle Bin via <c>SHFileOperationW</c> (FOF_ALLOWUNDO).</summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsRecycleBin : IRecycleBin
{
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
                fFlags = (ushort)(FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOF_NOCONFIRMATION | FILEOPERATION_FLAGS.FOF_SILENT | FILEOPERATION_FLAGS.FOF_NOERRORUI),
            };

            var result = PInvoke.SHFileOperation(ref op);
            if (result != 0 || op.fAnyOperationsAborted)
            {
                throw new IOException($"Moving '{fullPath}' to the Recycle Bin failed (0x{result:X}).");
            }
        }
    }
}
