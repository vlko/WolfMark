using Zapisnik.Storage.Trash;

namespace Zapisnik.Storage;

/// <summary>
/// Moves the notes root to another folder (SPEC §3.1, M8-04): copy everything, verify sizes, then
/// send the old folder to the Recycle Bin. Nothing is deleted permanently; if verification fails the
/// old folder stays untouched.
/// </summary>
public static class NotesMover
{
    public sealed record Result(int FilesCopied, int FilesSkipped);

    public static async Task<Result> MoveAsync(string sourceRoot, string targetRoot, IRecycleBin recycleBin, IProgress<int>? progress, CancellationToken ct)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        targetRoot = Path.GetFullPath(targetRoot);
        if (string.Equals(sourceRoot.TrimEnd(Path.DirectorySeparatorChar), targetRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return new Result(0, 0);
        }

        if (targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The new folder cannot be inside the current notes folder.");
        }

        if (!Directory.Exists(sourceRoot))
        {
            Directory.CreateDirectory(targetRoot);
            return new Result(0, 0);
        }

        var copied = 0;
        var skipped = 0;
        var files = Directory.EnumerateFiles(sourceRoot, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false }).ToList();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (AtomicFile.IsTempFile(Path.GetFileName(file)))
            {
                continue;
            }

            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (new FileInfo(target).Length == new FileInfo(file).Length)
                {
                    skipped++;
                    continue;
                }

                throw new IOException($"'{target}' already exists with different content.");
            }

            await using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var dst = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            copied++;
            progress?.Report(copied + skipped);
        }

        // Verify before removing anything.
        foreach (var file in files)
        {
            if (AtomicFile.IsTempFile(Path.GetFileName(file)))
            {
                continue;
            }

            var target = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(file).Length)
            {
                throw new IOException($"Verification failed for '{target}'. The original folder was kept.");
            }
        }

        await recycleBin.MoveToRecycleBinAsync(sourceRoot, ct).ConfigureAwait(false);
        return new Result(copied, skipped);
    }
}
