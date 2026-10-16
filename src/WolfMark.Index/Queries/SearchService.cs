using Microsoft.Data.Sqlite;
using WolfMark.Core.Tags;
using WolfMark.Index.Database;
using WolfMark.Index.Embeddings;

namespace WolfMark.Index.Queries;

/// <summary>A search hit. Highlighted ranges in <see cref="Snippet"/>/<see cref="TitleHighlighted"/> are wrapped in \u0002…\u0003.</summary>
public sealed record SearchHit(NoteSummary Note, string Snippet, string TitleHighlighted, double Score, bool FromKeyword, bool FromSemantic);

/// <summary>
/// Hybrid search (SPEC §4.2, M2-03, M2-06): FTS5/BM25 + vector similarity merged with Reciprocal Rank
/// Fusion. Vector search is optional – without an embedding provider this is plain FTS.
/// </summary>
public sealed class SearchService(IndexDatabase db, IEmbeddingProvider? embeddings = null)
{
    public const char HighlightStart = '\u0002';
    public const char HighlightEnd = '\u0003';
    private const int RrfK = 60;
    private const int CandidatePool = 50;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string input, int limit, CancellationToken ct)
    {
        var query = SearchQuery.Parse(input);
        if (query.IsEmpty)
        {
            return [];
        }

        if (!query.HasText)
        {
            return await db.ReadAsync(c => FilterOnly(c, query, limit), ct).ConfigureAwait(false);
        }

        var keywordTask = db.ReadAsync(c => Keyword(c, query, CandidatePool), ct);

        IReadOnlyList<(long NoteId, double Distance)> semantic = [];
        if (embeddings is not null && db.VectorsAvailable && query.Text.Length >= 3)
        {
            var vectors = await embeddings.EmbedAsync([query.Text], EmbeddingPurpose.Query, ct).ConfigureAwait(false);
            var blob = VectorBlob.ToBlob(VectorBlob.Normalize(vectors[0]));
            semantic = await db.ReadAsync(c => Semantic(c, blob, CandidatePool), ct).ConfigureAwait(false);
        }

        var keyword = await keywordTask.ConfigureAwait(false);
        if (semantic.Count == 0)
        {
            return [.. keyword.Take(limit)];
        }

        // Reciprocal Rank Fusion.
        var scores = new Dictionary<long, double>();
        for (var i = 0; i < keyword.Count; i++)
        {
            scores[keyword[i].Note.Id] = 1.0 / (RrfK + i + 1);
        }

        for (var i = 0; i < semantic.Count; i++)
        {
            scores[semantic[i].NoteId] = scores.GetValueOrDefault(semantic[i].NoteId) + (1.0 / (RrfK + i + 1));
        }

        var keywordById = keyword.ToDictionary(h => h.Note.Id);
        var semanticIds = semantic.Select(s => s.NoteId).Where(id => !keywordById.ContainsKey(id)).ToList();
        var semanticNotes = await db.ReadAsync(c => LoadNotes(c, semanticIds, query), ct).ConfigureAwait(false);
        var semanticSet = semantic.Select(s => s.NoteId).ToHashSet();

        var merged = new List<SearchHit>();
        foreach (var (id, score) in scores.OrderByDescending(kv => kv.Value))
        {
            if (keywordById.TryGetValue(id, out var hit))
            {
                merged.Add(hit with { Score = score, FromSemantic = semanticSet.Contains(id) });
            }
            else if (semanticNotes.TryGetValue(id, out var note))
            {
                merged.Add(new SearchHit(note, note.Preview, note.Title, score, FromKeyword: false, FromSemantic: true));
            }

            if (merged.Count >= limit)
            {
                break;
            }
        }

        return merged;
    }

    private static (string Sql, Action<SqliteCommand> Bind) Filters(SearchQuery query)
    {
        var parts = new List<string>();
        if (query.From is not null)
        {
            parts.Add("n.day BETWEEN $from AND $to");
        }

        for (var i = 0; i < query.Tags.Count; i++)
        {
            parts.Add($"EXISTS (SELECT 1 FROM note_tags t WHERE t.note_id = n.id AND t.tag_key = $tag{i})");
        }

        return (
            parts.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", parts),
            cmd =>
            {
                if (query.From is { } f)
                {
                    cmd.With("$from", SqliteExtensions.FormatDay(f)).With("$to", SqliteExtensions.FormatDay(query.To!.Value));
                }

                for (var i = 0; i < query.Tags.Count; i++)
                {
                    cmd.With("$tag" + i, TagName.ToKey(query.Tags[i]));
                }
            });
    }

    private static List<SearchHit> Keyword(SqliteConnection c, SearchQuery query, int limit)
    {
        var (filterSql, bind) = Filters(query);
        using var cmd = c.Command(
            $"""
            SELECT {NoteSummaryReader.Columns},
                   snippet(notes_fts, 1, char(2), char(3), '…', 16),
                   highlight(notes_fts, 0, char(2), char(3)),
                   bm25(notes_fts, 5.0, 1.0, 3.0) AS rank
            FROM notes_fts JOIN notes n ON n.id = notes_fts.rowid
            WHERE notes_fts MATCH $q {filterSql}
            ORDER BY rank LIMIT $limit;
            """)
            .With("$q", query.ToFtsExpression())
            .With("$limit", limit);
        bind(cmd);

        using var r = cmd.ExecuteReader();
        var list = new List<SearchHit>();
        var col = NoteSummaryReader.ColumnCount;
        while (r.Read())
        {
            var note = NoteSummaryReader.Read(r);
            list.Add(new SearchHit(note, r.GetString(col), r.GetString(col + 1), -r.GetDouble(col + 2), FromKeyword: true, FromSemantic: false));
        }

        return list;
    }

    private static List<SearchHit> FilterOnly(SqliteConnection c, SearchQuery query, int limit)
    {
        var (filterSql, bind) = Filters(query);
        using var cmd = c.Command(
            $"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE 1 = 1 {filterSql} ORDER BY n.created_at DESC LIMIT $limit;")
            .With("$limit", limit);
        bind(cmd);
        return [.. NoteSummaryReader.ReadAll(cmd).Select(n => new SearchHit(n, n.Preview, n.Title, 0, true, false))];
    }

    private static List<(long NoteId, double Distance)> Semantic(SqliteConnection c, byte[] vector, int k)
    {
        using var cmd = c.Command(
            """
            SELECT ch.note_id, MIN(v.distance) AS d
            FROM (SELECT rowid, distance FROM note_chunks_vec WHERE embedding MATCH $v AND k = $k) v
            JOIN note_chunks ch ON ch.id = v.rowid
            GROUP BY ch.note_id ORDER BY d;
            """)
            .With("$v", vector)
            .With("$k", k);
        using var r = cmd.ExecuteReader();
        var list = new List<(long, double)>();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), r.GetDouble(1)));
        }

        return list;
    }

    private static Dictionary<long, NoteSummary> LoadNotes(SqliteConnection c, IReadOnlyList<long> ids, SearchQuery query)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var (filterSql, bind) = Filters(query);
        using var cmd = c.Command(
            $"SELECT {NoteSummaryReader.Columns} FROM notes n WHERE n.id IN ({string.Join(", ", ids.Select((_, i) => "$id" + i))}) {filterSql};");
        for (var i = 0; i < ids.Count; i++)
        {
            cmd.With("$id" + i, ids[i]);
        }

        bind(cmd);
        return NoteSummaryReader.ReadAll(cmd).ToDictionary(n => n.Id);
    }
}
