namespace Zapisnik.Index.Embeddings;

public enum EmbeddingPurpose
{
    /// <summary>A search query (e5 models prefix "query: ").</summary>
    Query,

    /// <summary>A note chunk being indexed (e5 models prefix "passage: ").</summary>
    Passage,
}

/// <summary>
/// Pluggable embedding model (SPEC §2): local ONNX model, Windows AI APIs on Copilot+ PCs, or a remote API.
/// Implementations must be thread-safe and must never be called on the UI thread.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Identifies the model; a change triggers re-embedding of all notes.</summary>
    string ModelId { get; }

    int Dimensions { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken);
}
