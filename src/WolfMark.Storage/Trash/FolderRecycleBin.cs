namespace WolfMark.Storage.Trash;

/// <summary>
/// Fallback for non-Windows hosts and tests: moves files into a trash folder
/// (by default <c>&lt;root&gt;/.wolfmark/trash</c>), keeping the relative structure. Files and folders.
/// </summary>
public sealed class FolderRecycleBin(string root, string trashDirectory) : IRecycleBin
{
    public FolderRecycleBin(string root)
        : this(root, Path.Combine(root, ".wolfmark", "trash"))
    {
    }

    public string TrashDirectory { get; } = trashDirectory;

    public Task MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var relative = Path.GetRelativePath(root, fullPath);
                var target = Path.Combine(TrashDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    target = Path.Combine(
                        Path.GetDirectoryName(target)!,
                        $"{Path.GetFileNameWithoutExtension(target)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}{Path.GetExtension(target)}");
                }

                if (Directory.Exists(fullPath))
                {
                    Directory.Move(fullPath, target);
                }
                else
                {
                    File.Move(fullPath, target);
                }
            },
            cancellationToken);
}
