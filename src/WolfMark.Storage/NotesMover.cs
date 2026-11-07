using WolfMark.Storage.Trash;

namespace WolfMark.Storage;

/// <summary>
/// Moves the notes root to another folder (SPEC §3.1, M8-04): copy everything, verify sizes, then
/// send the old folder to the Recycle Bin. Nothing is deleted permanently; if verification fails the
/// old folder stays untouched.
/// </summary>
public static class NotesMover
{
    public sealed record Result(int FilesCopied, int FilesSkipped);

    /// <summary>Copies the notes, verifies the copy and sends <paramref name="sourceRoot"/> to the recycle bin.</summary>
    public static async Task<Result> MoveAsync(string sourceRoot, string targetRoot, IRecycleBin recycleBin, IProgress<int>? progress, CancellationToken ct)
    {
        var result = await CopyAsync(sourceRoot, targetRoot, progress, replaceDifferent: null, ct).ConfigureAwait(false);
        if (!SameFolder(sourceRoot, targetRoot) && Directory.Exists(sourceRoot))
        {
            await recycleBin.MoveToRecycleBinAsync(Path.GetFullPath(sourceRoot), ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Copies every file of <paramref name="sourceRoot"/> into <paramref name="targetRoot"/> and verifies the sizes.
    /// Nothing is removed from the source. A file already in the target with the same content is skipped; one with
    /// other content stops the copy – or, with <paramref name="replaceDifferent"/>, goes to that recycle bin and is
    /// replaced (never overwritten in place).
    /// </summary>
    public static async Task<Result> CopyAsync(string sourceRoot, string targetRoot, IProgress<int>? progress, IRecycleBin? replaceDifferent, CancellationToken ct)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        targetRoot = Path.GetFullPath(targetRoot);
        if (SameFolder(sourceRoot, targetRoot))
        {
            return new Result(0, 0);
        }

        if (targetRoot.StartsWith(sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The new folder cannot be inside the current notes folder.");
        }

        if (sourceRoot.StartsWith(targetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The new folder cannot contain the current notes folder.");
        }

        if (!Directory.Exists(sourceRoot))
        {
            Directory.CreateDirectory(targetRoot);
            return new Result(0, 0);
        }

        var copied = 0;
        var skipped = 0;
        var files = Directory.EnumerateFiles(sourceRoot, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
            .Where(f => !AtomicFile.IsTempFile(Path.GetFileName(f)))
            .ToList();
        Directory.CreateDirectory(targetRoot);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (await SameContentAsync(file, target, ct).ConfigureAwait(false))
                {
                    skipped++;
                    progress?.Report(copied + skipped);
                    continue;
                }

                if (replaceDifferent is null)
                {
                    throw new IOException($"'{target}' already exists with different content.");
                }

                await replaceDifferent.MoveToRecycleBinAsync(target, ct).ConfigureAwait(false);
            }

            await using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
            await using (var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            copied++;
            progress?.Report(copied + skipped);
        }

        // Verify before anyone removes anything.
        foreach (var file in files)
        {
            var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(file).Length)
            {
                throw new IOException($"Verification failed for '{target}'. The original folder was kept.");
            }
        }

        return new Result(copied, skipped);
    }

    private static async Task<bool> SameContentAsync(string a, string b, CancellationToken ct)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length)
        {
            return false;
        }

        const int size = 81920;
        await using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, size, useAsync: true);
        await using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, size, useAsync: true);
        var ba = new byte[size];
        var bb = new byte[size];
        while (true)
        {
            var na = await fa.ReadAtLeastAsync(ba, size, throwOnEndOfStream: false, ct).ConfigureAwait(false);
            var nb = await fb.ReadAtLeastAsync(bb, size, throwOnEndOfStream: false, ct).ConfigureAwait(false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb)))
            {
                return false;
            }

            if (na < size)
            {
                return true;
            }
        }
    }

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);
}
