using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Zapisnik.Core.Text;
using Zapisnik.Index.Database;
using Zapisnik.Index.Indexing;

namespace Zapisnik.Index.Embeddings;

/// <summary>
/// Computes chunk embeddings in the background at low priority (SPEC §4.2, M2-05): small batches,
/// a pause between batches, wakes up when the indexer reports changes. When the model changes,
/// all vectors are dropped and recomputed.
/// </summary>
public sealed partial class EmbeddingIndexer
{
    private const int NotesPerBatch = 8;

    private readonly IndexDatabase _db;
    private readonly IEmbeddingProvider _provider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _signal = new(0, 1);

    public EmbeddingIndexer(IndexDatabase db, IEmbeddingProvider provider, NoteIndexer indexer, ILogger<EmbeddingIndexer> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
        indexer.Changed += (_, _) => Wake();
    }

    public TimeSpan PauseBetweenBatches { get; init; } = TimeSpan.FromMilliseconds(200);

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_db.VectorsAvailable)
        {
            LogDisabled();
            return;
        }

        await _db.WriteAsync(EnsureVectorTable, ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            var done = await ProcessBatchAsync(ct).ConfigureAwait(false);
            if (done == 0)
            {
                await _signal.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(PauseBetweenBatches, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Embeds up to one batch of stale notes. Returns the number of notes processed.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var stale = await _db.ReadAsync(LoadStale, ct).ConfigureAwait(false);
        if (stale.Count == 0)
        {
            return 0;
        }

        var chunks = new List<(long NoteId, string Hash, int Ord, string Text)>();
        foreach (var note in stale)
        {
            var pieces = TextChunker.Chunk(note.Text);
            for (var i = 0; i < pieces.Count; i++)
            {
                chunks.Add((note.Id, note.Hash, i, pieces[i]));
            }
        }

        var vectors = chunks.Count == 0
            ? []
            : await _provider.EmbedAsync([.. chunks.Select(c => c.Text)], EmbeddingPurpose.Passage, ct).ConfigureAwait(false);

        await _db.WriteAsync(
            c =>
            {
                using var tx = c.BeginTransaction();
                foreach (var note in stale)
                {
                    // Skip if the note changed while we were embedding.
                    using var check = c.Command("SELECT content_hash FROM notes WHERE id = $id;", tx).With("$id", note.Id);
                    if (!string.Equals(check.ExecuteScalar() as string, note.Hash, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    NoteIndexer.DeleteVectors(c, tx, note.Id);
                    for (var i = 0; i < chunks.Count; i++)
                    {
                        if (chunks[i].NoteId != note.Id)
                        {
                            continue;
                        }

                        using var ins = c.Command("INSERT INTO note_chunks (note_id, ord, text, content_hash) VALUES ($n, $o, $t, $h) RETURNING id;", tx)
                            .With("$n", note.Id).With("$o", chunks[i].Ord).With("$t", chunks[i].Text).With("$h", note.Hash);
                        var chunkId = (long)ins.ExecuteScalar()!;
                        using var vec = c.Command("INSERT INTO note_chunks_vec (rowid, embedding) VALUES ($id, $v);", tx)
                            .With("$id", chunkId).With("$v", VectorBlob.ToBlob(VectorBlob.Normalize(vectors[i])));
                        vec.ExecuteNonQuery();
                    }

                    if (!chunks.Any(ch => ch.NoteId == note.Id))
                    {
                        // Empty note: store a marker chunk so it is not picked up again.
                        using var marker = c.Command("INSERT INTO note_chunks (note_id, ord, text, content_hash) VALUES ($n, -1, '', $h);", tx)
                            .With("$n", note.Id).With("$h", note.Hash);
                        marker.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            },
            ct).ConfigureAwait(false);

        return stale.Count;
    }

    private static List<StaleNote> LoadStale(SqliteConnection c)
    {
        using var cmd = c.Command(
            """
            SELECT n.id, n.content_hash, f.title || char(10) || f.body
            FROM notes n JOIN notes_fts f ON f.rowid = n.id
            WHERE n.content_hash <> ''
              AND NOT EXISTS (SELECT 1 FROM note_chunks ch WHERE ch.note_id = n.id AND ch.content_hash = n.content_hash)
            ORDER BY n.created_at DESC LIMIT $l;
            """).With("$l", NotesPerBatch);
        using var r = cmd.ExecuteReader();
        var list = new List<StaleNote>();
        while (r.Read())
        {
            list.Add(new StaleNote(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }

        return list;
    }

    private void EnsureVectorTable(SqliteConnection c)
    {
        var model = c.Scalar("SELECT value FROM meta WHERE key = 'embedding_model';");
        var expected = _provider.ModelId + "|" + _provider.Dimensions.ToString(CultureInfo.InvariantCulture);
        var exists = c.Scalar("SELECT count(*) FROM sqlite_master WHERE name = 'note_chunks_vec';") == "1";
        if (exists && model == expected)
        {
            return;
        }

        LogModelChanged(model ?? "(none)", expected);
        using var tx = c.BeginTransaction();
        c.Execute("DROP TABLE IF EXISTS note_chunks_vec; DELETE FROM note_chunks;", tx);
        c.Execute($"CREATE VIRTUAL TABLE note_chunks_vec USING vec0(embedding float[{_provider.Dimensions.ToString(CultureInfo.InvariantCulture)}]);", tx);
        using (var cmd = c.Command("INSERT INTO meta(key, value) VALUES ('embedding_model', $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", tx))
        {
            cmd.With("$v", expected).ExecuteNonQuery();
        }

        tx.Commit();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vector extension not available – embeddings disabled")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Embedding model changed from {Old} to {New} – recomputing all vectors")]
    private partial void LogModelChanged(string old, string @new);

    private sealed record StaleNote(long Id, string Hash, string Text);
}
