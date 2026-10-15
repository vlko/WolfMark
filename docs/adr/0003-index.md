# ADR 0003 – Index: FTS5 s uloženým obsahom, voliteľné vektory

- Stav: prijaté
- Backlog: M2-01 … M2-07, M3-01

## Rozhodnutia

1. **`notes_fts` nie je contentless.** SPEC §4.2 navrhuje `content=''`, ale contentless FTS5 nevie
   `snippet()`/`highlight()` (M2-03) a mazanie vyžaduje pôvodný obsah. Uloženie tela v indexe je pri
   10 000 poznámkach zanedbateľné; index je stále len cache.
2. **Jeden writer** = jedna slučka nad `Channel` s jedným spojením (`IndexDatabase.WriteAsync`);
   čítania majú vlastný pool spojení (`Pooling=false` + vlastný pool, lebo sa na každé spojenie
   načítava sqlite-vec).
3. **Obnova:** `quick_check` + `schema_version` pri štarte; poškodenie počas behu (CORRUPT/NOTADB/IOERR)
   → súbor sa zmaže a vytvorí znovu, `Reset` udalosť spustí plný sken. `ResetAsync` zahodí *všetky*
   tabuľky (aj stav Claude Code sync), takže rebuild je skutočne čistý.
4. **Vektory sú voliteľné.** sqlite-vec sa načíta z výstupného adresára; balík `sqlite-vec` nemá
   `win-arm64` binárku → na ARM64 sa sémantické vyhľadávanie vypne (FTS ostáva). Tabuľka `vec0` sa
   vytvára lenivo podľa dimenzie modelu; zmena `meta.embedding_model` = prepočet všetkého.
5. **Embedding model zatiaľ nie je dodaný.** `IEmbeddingProvider` + `EmbeddingIndexer` + RRF sú hotové
   a otestované testovacím providerom; ONNX + `multilingual-e5-small` (M2-05) je otvorené – vyžaduje
   overenie AOT kompatibility ONNX Runtime a SentencePiece (Unigram) tokenizéra.
6. **OneDrive placeholdery** sa pri skene indexujú len z metadát (deň + tag z cesty); obsah dočíta
   `IndexPendingContentAsync` pomaly na pozadí.
