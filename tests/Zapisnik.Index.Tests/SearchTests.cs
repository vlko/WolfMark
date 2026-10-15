using Zapisnik.Index.Embeddings;
using Zapisnik.Index.Queries;
using Microsoft.Extensions.Logging.Abstractions;

namespace Zapisnik.Index.Tests;

public sealed class SearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Diacritics_insensitive_with_highlight()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "# Poznámka o exporte\nDlhá poznámka.");
        f.Write("2026/10/15/1500.md", "# Iné\nnič");
        await f.Indexer.FullScanAsync(null, Ct);

        var hit = Assert.Single(await new SearchService(f.Db).SearchAsync("poznamka", 10, Ct));
        Assert.Equal("2026/10/15/praca_1403.md", hit.Note.RelativePath);
        Assert.Contains("\u0002Poznámka\u0003", hit.TitleHighlighted);
        Assert.Contains('\u0002', hit.Snippet);
    }

    [Fact]
    public async Task Filters_by_tag_and_date()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/praca_1403.md", "deploy A");
        f.Write("2026/09/15/praca_1403.md", "deploy B");
        f.Write("2026/10/15/1500.md", "deploy C #praca");
        f.Write("2026/10/15/rodina_1600.md", "deploy D");
        await f.Indexer.FullScanAsync(null, Ct);
        var search = new SearchService(f.Db);

        var hits = await search.SearchAsync("deploy #praca @2026-10", 10, Ct);
        Assert.Equal(["2026/10/15/1500.md", "2026/10/15/praca_1403.md"], hits.Select(h => h.Note.RelativePath).Order());

        var onlyFilters = await search.SearchAsync("#rodina", 10, Ct);
        Assert.Equal("2026/10/15/rodina_1600.md", Assert.Single(onlyFilters).Note.RelativePath);
    }

    [Fact]
    public async Task Query_syntax_cannot_break_fts()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/1500.md", "a \"quoted\" NEAR(text) OR AND");
        await f.Indexer.FullScanAsync(null, Ct);
        var search = new SearchService(f.Db);
        Assert.Single(await search.SearchAsync("\"quoted", 10, Ct));
        Assert.Single(await search.SearchAsync("NEAR(text)", 10, Ct));
        Assert.Empty(await search.SearchAsync("*", 10, Ct));
    }

    [Fact]
    public void Parses_query()
    {
        var q = SearchQuery.Parse("deploy #Praca @2026-10 export");
        Assert.Equal(["deploy", "export"], q.Terms);
        Assert.Equal(["Praca"], q.Tags);
        Assert.Equal(new DateOnly(2026, 10, 1), q.From);
        Assert.Equal(new DateOnly(2026, 10, 31), q.To);
        Assert.Equal("\"deploy\"* \"export\"*", q.ToFtsExpression());
    }

    [Fact]
    public async Task Semantic_search_finds_notes_without_exact_words()
    {
        await using var f = await IndexFixture.CreateAsync();
        Assert.True(f.Db.VectorsAvailable, "sqlite-vec native library was not found next to the test assembly");

        f.Write("2026/10/15/praca_1403.md", "# Nasadenie\nRelease na produkciu prebehol cez pipeline.");
        f.Write("2026/10/14/rodina_1800.md", "# Nákup\nMlieko, chlieb, maslo.");
        await f.Indexer.FullScanAsync(null, Ct);

        var provider = new ConceptEmbeddingProvider();
        var embedder = new EmbeddingIndexer(f.Db, provider, f.Indexer, NullLogger<EmbeddingIndexer>.Instance);
        await f.Db.WriteAsync(_ => { }, Ct);
        var run = Task.Run(() => embedder.RunAsync(Ct), Ct);
        await WaitForEmbeddingsAsync(f);

        var hits = await new SearchService(f.Db, provider).SearchAsync("čo som riešil s deploymentom", 5, Ct);
        Assert.NotEmpty(hits);
        Assert.Equal("2026/10/15/praca_1403.md", hits[0].Note.RelativePath);
        Assert.True(hits[0].FromSemantic);
        Assert.False(hits[0].FromKeyword);
    }

    [Fact]
    public async Task Model_change_recomputes_vectors()
    {
        await using var f = await IndexFixture.CreateAsync();
        f.Write("2026/10/15/1403.md", "deployment");
        await f.Indexer.FullScanAsync(null, Ct);

        var e1 = new EmbeddingIndexer(f.Db, new ConceptEmbeddingProvider(), f.Indexer, NullLogger<EmbeddingIndexer>.Instance);
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct))
        {
            var run = Task.Run(() => e1.RunAsync(cts.Token), Ct);
            await WaitForEmbeddingsAsync(f);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        var e2 = new EmbeddingIndexer(f.Db, new ConceptEmbeddingProvider("other-model"), f.Indexer, NullLogger<EmbeddingIndexer>.Instance);
        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ = Task.Run(() => e2.RunAsync(cts2.Token), Ct);
        string? model = null;
        for (var i = 0; i < 250 && model?.StartsWith("other-model", StringComparison.Ordinal) != true; i++)
        {
            await Task.Delay(20, Ct);
            model = await f.Db.ReadAsync(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT value FROM meta WHERE key = 'embedding_model'";
                return cmd.ExecuteScalar() as string;
            }, Ct);
        }

        Assert.StartsWith("other-model", model);
        await WaitForEmbeddingsAsync(f);
        await cts2.CancelAsync();
    }

    private static async Task WaitForEmbeddingsAsync(IndexFixture f)
    {
        for (var i = 0; i < 250; i++)
        {
            var pending = await f.Db.ReadAsync(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    SELECT (SELECT count(*) FROM sqlite_master WHERE name = 'note_chunks_vec') = 1
                       AND NOT EXISTS (SELECT 1 FROM notes n WHERE NOT EXISTS
                          (SELECT 1 FROM note_chunks ch WHERE ch.note_id = n.id AND ch.content_hash = n.content_hash))
                    """;
                return (long)cmd.ExecuteScalar()! == 1;
            }, Ct);
            if (pending)
            {
                return;
            }

            await Task.Delay(20, Ct);
        }

        Assert.Fail("embeddings were not computed");
    }

    /// <summary>
    /// Test double: maps words to a handful of "concepts" so related words in different
    /// languages land close together – enough to exercise the vector pipeline end to end.
    /// </summary>
    private sealed class ConceptEmbeddingProvider(string modelId = "test-concepts") : IEmbeddingProvider
    {
        private static readonly (string Prefix, int Dim)[] Concepts =
        [
            ("deploy", 0), ("nasaden", 0), ("release", 0), ("pipeline", 0), ("produkci", 0),
            ("nákup", 1), ("mlieko", 1), ("chlieb", 1), ("maslo", 1),
        ];

        public string ModelId => modelId;

        public int Dimensions => 8;

        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult(texts.Select(Embed).ToArray());

        private float[] Embed(string text)
        {
            var v = new float[Dimensions];
            v[7] = 0.05f;
            foreach (var word in text.ToLowerInvariant().Split([' ', '\n', '.', ',', '#'], StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var (prefix, dim) in Concepts)
                {
                    if (word.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        v[dim] += 1;
                    }
                }
            }

            return v;
        }
    }
}
