namespace Zapisnik.Storage.Trash;

/// <summary>Notes are never deleted permanently (CLAUDE.md rule 5).</summary>
public interface IRecycleBin
{
    Task MoveToRecycleBinAsync(string fullPath, CancellationToken cancellationToken);
}
