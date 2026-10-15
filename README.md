# Zápisník

Rýchly natívny denný zápisník pre Windows 11 (.NET 11, WinUI 3, Native AOT). Poznámky sú obyčajné
`.md` súbory v `YYYY/MM/DD/<tag>_<HHmm>.md` (štandardne v OneDrive); SQLite index je len cache.
Detaily: [`SPEC.md`](SPEC.md), poradie práce a stav: [`BACKLOG.md`](BACKLOG.md), pravidlá: [`CLAUDE.md`](CLAUDE.md),
rozhodnutia: [`docs/adr`](docs/adr).

## Štruktúra

| Projekt | Obsah | Platforma |
|---|---|---|
| `Zapisnik.Core` | `NotePath`/`NoteName` parser, tagy a paleta, markdown lexer + inkrementálny model, editovacie príkazy, undo, kalendárna matematika, nastavenia (STJ source-gen) | `net11.0` |
| `Zapisnik.Storage` | directory engine (enumerácia, CRUD, atomické zápisy, kôš), watcher s debounce a potlačením ech, `tags.json`, lokálne nastavenia, logovanie | `net11.0` |
| `Zapisnik.Index` | SQLite (WAL, single writer), FTS5, sqlite-vec, inkrementálny indexer, `calendar_days`, história, hybridné vyhľadávanie | `net11.0` |
| `Zapisnik.Sync.ClaudeCode` | import promptov z `~/.claude/projects/**.jsonl`, filtrovanie, redakcia tajomstiev, idempotentný sync | `net11.0` |
| `Zapisnik.Editor` | markdown editor (RichEditBox + lexer na pozadí) | WinUI 3 |
| `Zapisnik.App` | okno, kalendár, história, vyhľadávanie, nastavenia (UI v C#, ADR 0002) | WinUI 3 |

## Build

Windows (Visual Studio 2026 alebo CLI, .NET 11 SDK podľa `global.json`):

```powershell
dotnet build Zapisnik.sln -p:Platform=x64
dotnet test --solution Zapisnik.CrossPlatform.slnf
dotnet publish src/Zapisnik.App -c Release -r win-x64      # Native AOT, self-contained
dotnet run --project src/Zapisnik.App -p:Platform=x64
```

Linux/WSL (doménové knižnice a testy; WinUI projekty sa dajú typovo skontrolovať, nie spustiť):

```bash
dotnet test --solution Zapisnik.CrossPlatform.slnf
dotnet build Zapisnik.sln -p:Platform=x64   # type-check celého riešenia vrátane UI
```

## Klávesové skratky

| | |
|---|---|
| `Ctrl+N` | nová poznámka (súbor vznikne až prvým znakom) |
| `Ctrl+K` | vyhľadávanie (`#tag`, `@2026-10`); s označeným textom v editore vloží odkaz |
| `Ctrl+Shift+C` | plný kalendár (šípky, Enter, Esc) |
| `Ctrl+E` | raw ↔ formátovaný režim |
| `Ctrl+B` / `Ctrl+I` / `Ctrl+Shift+L` | tučné / kurzíva / úloha |
| `Tab` / `Shift+Tab`, `Enter` | odsadenie a pokračovanie zoznamu |
| `Ctrl+Alt+←/→` | predchádzajúci / nasledujúci týždeň |
| `Ctrl+,` | nastavenia |
