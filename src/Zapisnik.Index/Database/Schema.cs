namespace Zapisnik.Index.Database;

/// <summary>
/// Index schema (SPEC §4.2). The index is a cache: on version mismatch it is simply rebuilt.
/// Deviation from the spec sketch: <c>notes_fts</c> stores its content (not contentless) so that
/// <c>snippet()</c>/<c>highlight()</c> work and the embedding worker can read bodies – see ADR 0003.
/// </summary>
internal static class Schema
{
    public const int Version = 1;

    public const string Create = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);

        CREATE TABLE notes (
          id INTEGER PRIMARY KEY,
          rel_path TEXT NOT NULL UNIQUE COLLATE NOCASE,
          created_at TEXT NOT NULL,
          day TEXT NOT NULL,
          primary_tag TEXT,
          primary_tag_key TEXT,
          title TEXT NOT NULL DEFAULT '',
          preview TEXT NOT NULL DEFAULT '',
          content_hash TEXT NOT NULL DEFAULT '',
          file_mtime INTEGER NOT NULL,
          file_size INTEGER NOT NULL,
          word_count INTEGER NOT NULL DEFAULT 0,
          open_tasks INTEGER NOT NULL DEFAULT 0,
          done_tasks INTEGER NOT NULL DEFAULT 0,
          source TEXT NOT NULL DEFAULT 'user',
          session_id TEXT,
          is_standard INTEGER NOT NULL DEFAULT 1,
          is_conflict INTEGER NOT NULL DEFAULT 0,
          conflict_of TEXT,
          is_placeholder INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX ix_notes_day ON notes(day);
        CREATE INDEX ix_notes_created ON notes(created_at DESC, id DESC);
        CREATE INDEX ix_notes_tag ON notes(primary_tag_key, created_at DESC);
        CREATE INDEX ix_notes_session ON notes(session_id) WHERE session_id IS NOT NULL;

        CREATE TABLE note_tags (
          note_id INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
          tag TEXT NOT NULL,
          tag_key TEXT NOT NULL,
          PRIMARY KEY (note_id, tag_key)
        ) WITHOUT ROWID;
        CREATE INDEX ix_note_tags_key ON note_tags(tag_key);

        CREATE VIRTUAL TABLE notes_fts USING fts5(title, body, tags, tokenize = 'unicode61 remove_diacritics 2');

        CREATE TABLE note_chunks (
          id INTEGER PRIMARY KEY,
          note_id INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
          ord INTEGER NOT NULL,
          text TEXT NOT NULL,
          content_hash TEXT NOT NULL
        );
        CREATE INDEX ix_chunks_note ON note_chunks(note_id);

        CREATE TABLE calendar_days (
          day TEXT PRIMARY KEY,
          note_count INTEGER NOT NULL,
          tag_summary TEXT NOT NULL,
          open_tasks INTEGER NOT NULL,
          done_tasks INTEGER NOT NULL
        ) WITHOUT ROWID;
        """;
}
