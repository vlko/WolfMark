# WolfMark – špecifikácia natívnej Windows aplikácie

> Názov: **WolfMark** (kódové meno `WolfMark`, pôvodný pracovný názov Zápisník).
> Dokument je písaný pre Claude Code: popisuje *čo* a *prečo*, backlog v `BACKLOG.md` popisuje *v akom poradí*, `CLAUDE.md` obsahuje pravidlá pre implementáciu.

---

## 1. Vízia

Rýchly, natívny denný zápisník pre Windows. Otvorím aplikáciu → okamžite píšem. Poznámky sú obyčajné `.md` súbory v adresári (štandardne OneDrive), takže sú zálohované, čitateľné aj bez aplikácie a nezávislé od nej. Aplikácia nad nimi stavia rýchle UI: markdown editor, kalendár s farebnými hashtagmi, históriu a sémantické vyhľadávanie.

### Princípy

1. **Súbory sú zdroj pravdy.** SQLite databáza je len index/cache. Kedykoľvek sa musí dať zmazať a znovu postaviť z adresára.
2. **Editor sa nikdy nesekne.** Žiadne I/O, indexovanie, embedding ani synchronizácia na UI vlákne. UI vlákno len renderuje a prijíma vstup.
3. **Natívne a rýchlo.** .NET 11, WinUI 3 (Windows App SDK), Native AOT. Cieľ: studený štart < 300 ms do momentu, keď sa dá písať.
4. **Bez straty dát.** Autosave, atomické zápisy, tolerancia voči OneDrive konfliktom a zmenám zvonka.
5. **Jednoduchosť pred funkciami.** Primárne rozhranie je editor; všetko ostatné je sekundárne a dá sa skryť.

---

## 2. Technologický stack

| Oblasť | Voľba | Poznámka |
|---|---|---|
| Runtime | **.NET 11** (`net11.0-windows10.0.22621.0` alebo novší TFM) | .NET 11 GA je plánované na 10. 11. 2026, RC1 má go-live licenciu. Do GA pracovať na RC, `global.json` s `rollForward: latestFeature`. |
| Jazyk | C# 15 | Union typy a uzavreté hierarchie sú vhodné na doménové udalosti (`NoteEvent`). |
| UI | **WinUI 3 / Windows App SDK** (posledná stabilná 1.x / 2.x) | Najnatívnejšia moderná UI vrstva pre Windows, podporuje Native AOT. Mica/Acrylic backdrop, Fluent. |
| Kompilácia | **Native AOT** (`PublishAot=true`) od prvého commitu | AOT/trim analyzéry zapnuté v každom builde, warningy = errory. |
| MVVM | `CommunityToolkit.Mvvm` (source generátory) | Bez reflexie, AOT-friendly. |
| Interop | `Microsoft.Windows.CsWin32` | Source-generované P/Invoke (napr. globálny hotkey, Cloud Files API). |
| JSON | `System.Text.Json` so **source generation** | Nastavenia, Claude Code JSONL parsing. Žiadna reflexná serializácia. |
| DB | `Microsoft.Data.Sqlite` (bez EF Core) | EF Core má pod AOT obmedzenia – použiť ručné ADO.NET mapovanie. |
| Fulltext | SQLite **FTS5** | Rýchle kľúčové hľadanie, diakritika cez `unicode61 remove_diacritics 2`. |
| Vektory | **sqlite-vec** (NuGet `sqlite-vec`, alt. `LightStudio.sqlite-vec`) | Načítať ako extension cez `SqliteConnection.LoadExtension`. |
| Embeddingy | ONNX Runtime + malý multilingválny model (napr. `multilingual-e5-small`) + `Microsoft.ML.Tokenizers` | Lokálne, offline. Rozhranie `IEmbeddingProvider`, aby sa dal vymeniť (Windows AI API na Copilot+ PC, prípadne vzdialené API). Overiť AOT kompatibilitu v M2. |
| Markdown parsing | **Markdig** (overiť AOT) alebo vlastný inkrementálny lexer | Pre zvýrazňovanie syntaxe je potrebný vlastný rýchly riadkový lexer; Markdig len na export/plný parse. |
| Logovanie | `Microsoft.Extensions.Logging` + jednoduchý súborový sink | Logy v `%LOCALAPPDATA%\WolfMark\logs`. |
| Testy | xUnit v3 (alebo MSTest), testy doménovej vrstvy bez UI | Directory engine a indexer musia byť testovateľné bez WinUI. |

### Štruktúra riešenia

```
WolfMark.sln
├─ src/
│  ├─ WolfMark.Core/          # doména: Note, Tag, NotePath parsing, udalosti – čistá knižnica, žiadne závislosti na UI
│  ├─ WolfMark.Storage/       # directory engine, file watcher, atomické zápisy
│  ├─ WolfMark.Index/         # SQLite, FTS5, sqlite-vec, embeddingy, kalendárové agregácie
│  ├─ WolfMark.Sync.ClaudeCode/ # import promptov z Claude Code
│  ├─ WolfMark.Editor/        # vlastný markdown editor control (WinUI)
│  └─ WolfMark.App/           # WinUI 3 aplikácia, views, viewmodels, composition root
└─ tests/
   ├─ WolfMark.Core.Tests/
   ├─ WolfMark.Storage.Tests/
   └─ WolfMark.Index.Tests/
```

---

## 3. Dátový model na disku

### 3.1 Koreňový adresár

- Predvolene: `%OneDrive%\WolfMark` (zistiť z premennej prostredia `OneDrive`, fallback `OneDriveConsumer` / `OneDriveCommercial`, fallback `Documents\WolfMark`).
- Meniteľný v nastaveniach. Pri zmene: ponúknuť **presun existujúcich poznámok** alebo **len prepnúť** (a reindexovať).

### 3.2 Štruktúra

```
<root>/
├─ 2026/
│  └─ 10/
│     └─ 15/
│        ├─ 0927.md                 # poznámka bez tagu (čas vzniku HHmm)
│        ├─ praca_1403.md           # tag #praca, vznik 14:03
│        ├─ ClaudeCode_1655.md      # importovaný prompt
│        └─ assets/                 # obrázky vložené do poznámok daného dňa
│           └─ 1403-01.png
└─ .wolfmark/
   ├─ tags.json                     # farby a poradie tagov (synchronizuje sa s poznámkami!)
   └─ settings.shared.json          # nastavenia, ktoré majú cestovať medzi počítačmi
```

Lokálne (nesynchronizované) dáta: `%LOCALAPPDATA%\WolfMark\` – SQLite index, embedding model, logy, pozícia okna.

### 3.3 Konvencia názvov súborov

Gramatika (`NotePath` parser v `WolfMark.Core`, pokrytý testami):

```
<tag>_<HHmm>[ss][-n].md      # s tagom
<HHmm>[ss][-n].md            # bez tagu
```

- Dátum = cesta `YYYY/MM/DD`, čas = názov súboru. Spolu dávajú `CreatedAt` poznámky.
- **Kolízia** (dve poznámky v tej istej minúte): najprv pridať sekundy `HHmmss`, potom suffix `-2`, `-3`.
- **Zmena tagu = premenovanie súboru** (`0927.md` → `praca_0927.md`). Čas vzniku sa nemení.
- **Povolené znaky tagu:** písmená vrátane diakritiky, číslice, `-`. Zakázané znaky Windows súborového systému (`\/:*?"<>|`) a `_` (oddeľovač). Tag sa normalizuje na NFC; porovnanie bez ohľadu na veľkosť písmen, zobrazenie podľa prvého výskytu.
- Súbory, ktoré nevyhovujú gramatike, sa **neignorujú**: indexujú sa ako poznámka bez tagu s časom = čas vytvorenia súboru a v UI majú indikátor „neštandardný názov“.

### 3.4 Viac tagov (rozšírenie)

Názov súboru nesie **primárny** tag (farba v kalendári). Ďalšie tagy môžu byť v tele poznámky ako `#tag` alebo vo voliteľnom YAML front matter:

```markdown
---
tags: [praca, finstat]
---
```

Indexer ich zozbiera; kalendár farbí podľa primárneho tagu, vyhľadávanie a filtre podľa všetkých.

### 3.5 Farby tagov

- Uložené v `<root>/.wolfmark/tags.json` (cestuje s poznámkami cez OneDrive).
- Nový tag dostane automaticky farbu z palety (12–16 farieb, kontrastné v light aj dark móde, deterministický výber podľa hashu názvu, aby dva počítače bez synchronizácie zvolili rovnakú farbu).
- Užívateľ môže farbu zmeniť; rezervovaný tag `ClaudeCode` má predvolenú farbu.

---

## 4. Moduly a funkcionalita

### 4.1 Directory engine (`WolfMark.Storage`)

Zodpovednosti:
- **Enumerácia** adresára asynchrónne (`IAsyncEnumerable<NoteFile>`), paralelne po rokoch/mesiacoch.
- **CRUD** poznámok: vytvoriť, načítať, uložiť, premenovať (zmena tagu), zmazať (do koša Windows, nie natrvalo).
- **Atomický zápis:** zápis do `.<name>.tmp` v tom istom adresári → `File.Replace` / `File.Move(overwrite)`. Overiť správanie s OneDrive (dočasné súbory musia byť vylúčené z indexu a ideálne sa nesmú synchronizovať – prefix `.` a prípona `.tmp`).
- **Sledovanie zmien zvonka:** `FileSystemWatcher` na koreň (rekurzívne) → udalosti cez `System.Threading.Channels` do debouncera (napr. 300 ms) → doménové udalosti `NoteCreated / NoteChanged / NoteRenamed / NoteDeleted`. Watcher je nespoľahlivý (pretečenie bufferu) → pri `Error` udalosti spustiť plný rescan.
- **Ignorovanie vlastných zápisov:** engine si pamätá hash/mtime posledného vlastného zápisu, aby echo z watchera nespustilo reload v editore.
- **OneDrive Files On-Demand:** súbory môžu byť len placeholdery. Enumerácia nesmie spustiť stiahnutie všetkého naraz – metadáta (názov, cesta, mtime) stačia pre kalendár; obsah sťahovať lenivo pri indexovaní na pozadí s nízkou prioritou. Rozpoznať atribút `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` / `OFFLINE`. Zvážiť ponuku „Vždy ponechať na tomto zariadení“ pre koreňový priečinok.
- **OneDrive konflikty:** detegovať súbory typu `praca_1403-NAZOVPC.md` (konfliktné kópie) a zobraziť ich v UI ako konflikt s možnosťou porovnať/zlúčiť.

### 4.2 Index (`WolfMark.Index`)

SQLite súbor v `%LOCALAPPDATA%\WolfMark\index.db`, WAL mód, jedno zapisovacie spojenie (single writer na dedikovanom vlákne/kanáli), čítacie spojenia podľa potreby.

Schéma (orientačná):

```sql
CREATE TABLE notes (
  id INTEGER PRIMARY KEY,
  rel_path TEXT NOT NULL UNIQUE,     -- 2026/10/15/praca_1403.md
  created_at TEXT NOT NULL,          -- ISO 8601 lokálny čas + offset
  day TEXT NOT NULL,                 -- 2026-10-15 (pre kalendár)
  primary_tag TEXT,
  title TEXT,                        -- prvý nadpis alebo prvý riadok
  preview TEXT,                      -- ~200 znakov pre zoznam histórie
  content_hash TEXT NOT NULL,        -- xxHash/SHA-256 obsahu
  file_mtime INTEGER NOT NULL,
  word_count INTEGER,
  open_tasks INTEGER, done_tasks INTEGER,  -- počet - [ ] / - [x]
  source TEXT NOT NULL DEFAULT 'user'      -- 'user' | 'claudecode'
);
CREATE TABLE note_tags (note_id INTEGER, tag TEXT, PRIMARY KEY(note_id, tag));
CREATE VIRTUAL TABLE notes_fts USING fts5(title, body, content='', tokenize='unicode61 remove_diacritics 2');
CREATE VIRTUAL TABLE note_chunks_vec USING vec0(embedding float[384]);  -- dimenzia podľa modelu
CREATE TABLE note_chunks (id INTEGER PRIMARY KEY, note_id INTEGER, ord INTEGER, text TEXT, content_hash TEXT);
CREATE TABLE calendar_days (day TEXT PRIMARY KEY, note_count INTEGER, tag_summary TEXT); -- materializovaný pohľad, JSON [{tag,count}]
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);   -- schema_version, embedding_model, last_full_scan
```

Pravidlá:
- **Inkrementálne indexovanie:** porovnať `file_mtime` + `content_hash`; reindexovať len zmenené.
- **Embeddingy po chunkoch** (odsek / ~512 tokenov), počítané v pozadí s nízkou prioritou, dávkovo. Pri zmene modelu (`meta.embedding_model`) prepočítať všetko.
- **Hybridné vyhľadávanie:** FTS5 (BM25) + vektorová podobnosť, zlúčené cez Reciprocal Rank Fusion. Čisto vektorové hľadanie je slabé na mená a presné výrazy.
- **Rebuild:** príkaz „Znovu postaviť index“ zmaže DB a naindexuje adresár. Musí bežať bez blokovania UI a s progresom.
- **Migrácie** schémy cez `meta.schema_version`; pri nekompatibilite jednoducho rebuild (index je cache).

### 4.3 Kalendár

- Údaje z `calendar_days` (počet záznamov, rozpad podľa tagov) – kalendár nikdy nečíta súbory priamo.
- **Resync z directory engine:** plný (rescan všetkého) aj čiastočný (konkrétny mesiac).
- Týždeň začína pondelkom, čísla týždňov podľa ISO 8601, lokalizácia mesiacov a dní (sk-SK, en-US).
- Voliteľne: slovenské sviatky a mená (meniny) ako vrstva v kalendári.

### 4.4 Markdown editor (`WolfMark.Editor`)

Dva režimy nad **jedným textovým modelom**:
1. **Raw** – čistý markdown so zvýraznením syntaxe (nadpisy väčšie/tučné, `**bold**`, `*italic*`, `` `code` ``, odkazy, zoznamy, úlohy `- [ ]`, citácie, bloky kódu). Značky zostávajú viditeľné, ale stlmené.
2. **Formátovaný (live preview)** – štýl Typora/Obsidian: značky sa skryjú mimo riadku s kurzorom, nadpisy, zoznamy, checkboxy (klikateľné), obrázky inline, tabuľky renderované.

Prepínanie: `Ctrl+E` alebo tlačidlo; pozícia kurzora a scroll sa zachová.

Implementačný prístup (preferovaný vlastný, natívny):
- Základ: WinUI `RichEditBox` (RichEdit/TOM) s vlastným formátovaním cez `ITextDocument`/`ITextRange`, alebo – ak sa ukáže ako pomalý pri veľkých dokumentoch – vlastný control nad Win2D / `Microsoft.UI.Composition` s vlastným layoutom textu (DirectWrite).
- **Inkrementálny lexer po riadkoch** (stav na začiatku riadku kvôli blokom kódu) – pri úprave prefarbiť len dotknuté riadky. Zvýrazňovanie musí byť asynchrónne/dávkové, nikdy nesmie zablokovať písanie.
- Prvý spike (M4) má porovnať: `RichEditBox` + vlastné farbenie vs. vlastný control. Merať latenciu písania na dokumente s 5 000 riadkami. Knižnice tretích strán len ako fallback (vyhodnotiť AOT kompatibilitu).

Správanie:
- **Autosave** s debounce (~800 ms nečinnosti) + uloženie pri strate fokusu, prepnutí poznámky a zatvorení okna.
- Ukladanie prebieha na pozadí; indikátor stavu (uložené / ukladá sa / chyba).
- Undo/redo v rámci sedenia.
- Klávesové skratky: `Ctrl+B/I/K` (bold/italic/link), `Ctrl+Shift+L` checkbox, `Tab/Shift+Tab` odsadenie zoznamu, automatické pokračovanie zoznamu na Enter.
- **Vloženie obrázka** (`Ctrl+V` zo schránky) → uloží do `assets/` daného dňa a vloží relatívny odkaz.
- Spell check (RichEditBox ho má natívne cez Windows; jazyk sk/en).
- Pri externej zmene otvoreného súboru: ak editor nemá neuložené zmeny → potichu načítať; ak má → ponúknuť porovnanie/zlúčenie.

### 4.5 Hlavné okno – rozloženie

```
┌───────────────────────────────────────────────────────────────┐
│  [title bar s vlastným obsahom: dátum · vyhľadávanie · ⚙]      │
├───────────────────────────────────────────────────────────────┤
│  KALENDÁROVÁ LIŠTA  ‹  Po13 Ut14 St15● Št16 Pi17 So18 Ne19  › ⤢ │  ← kompaktná (týždeň), ⤢ = plný kalendár
├───────────────────────────────────────────────────────────────┤
│  #praca ▾   14:03                         [Raw | Formát]  [+]  │  ← hlavička poznámky: tag (výber farby), čas, režim, nová poznámka
│                                                               │
│  EDITOR                                                       │
│                                                               │
├──────────────────────── ═══ (posuvný oddeľovač) ══════════════┤
│  HISTÓRIA (virtualizovaný zoznam)                             │
│  ▌praca   St 15. 10.  14:03  Hotfix pre export…               │
│  ▌ClaudeCode  St 15. 10. 11:20  Refaktoruj indexer…           │
│  ▌         Ut 14. 10.  09:27  Nápady na CODECON…               │
└───────────────────────────────────────────────────────────────┘
```

- **Tlačidlo `+`** (aj `Ctrl+N`): vytvorí novú poznámku s aktuálnym časom v dnešnom adresári a presunie do nej fokus. Súbor sa fyzicky vytvorí až po prvom napísanom znaku (prázdne poznámky nevznikajú).
- **Po spustení** sa otvorí posledná upravovaná poznámka s kurzorom na konci.
- **Kalendárová lišta:** dni aktuálneho týždňa, pri každom dni počet záznamov a farebné prúžky tagov. Klik na deň → filtruje históriu na daný deň a otvorí jeho poslednú poznámku. Šípky/scroll posúvajú týždne.
- **Plný kalendár:** prepnutie z lišty (tlačidlo alebo `Ctrl+Shift+C`) – rozbalí sa ako overlay alebo nahradí editor (viď 5.2).
- **História:** virtualizovaný `ItemsRepeater`/`ListView`, nekonečný scroll (stránkovanie z indexu), farebný pruh tagu, dátum, čas, titulok, náhľad. Filtrovanie podľa tagu (klik na tag) a dňa. Klik → otvorí v editore.
- **Vyhľadávanie** (`Ctrl+K`, command-palette štýl): hybridné hľadanie, výsledky so zvýraznením, filter `#tag`, `@2026-10`.

### 4.6 Nastavenia

- Koreňový adresár poznámok (+ presun/prepnutie).
- Téma: systém / svetlá / tmavá; backdrop Mica / Acrylic / žiadny.
- Font editora a veľkosť, šírka textu (max. šírka riadku pre čitateľnosť).
- Predvolený režim editora (raw/formát).
- Farby tagov, premenovanie tagu (hromadné premenovanie súborov), zlúčenie tagov.
- Claude Code sync: zapnúť/vypnúť, cesta k `~/.claude`, filter projektov, interval.
- Spúšťanie pri štarte Windows, globálna skratka rýchleho zápisu.
- Index: stav, „Znovu postaviť“, voľba embedding modelu.

### 4.7 Synchronizácia z Claude Code (`WolfMark.Sync.ClaudeCode`)

Zdroje dát (lokálne súbory Claude Code, formát sa medzi verziami mení – parser musí byť tolerantný a ignorovať neznáme polia):
- `%USERPROFILE%\.claude\history.jsonl` – história promptov naprieč sedeniami (timestamp, projekt).
- `%USERPROFILE%\.claude\projects\<zakódovaná-cesta-projektu>\<session-uuid>.jsonl` – kompletné transkripty, append-only, riadok = JSON objekt s `type`, `timestamp`, `sessionId`, `cwd`, `gitBranch`, `message`.

Pravidlá importu:
- Importujú sa len **prompty zadané človekom** (`type: "user"` s textovým obsahom; vynechať `tool_result`, systémom vložené správy, správy o pokračovaní po kompakcii).
- **Jedna poznámka na jedno sedenie** (nie na prompt), v adresári podľa dátumu prvého promptu: `YYYY/MM/DD/ClaudeCode_HHmm.md`. Obsah: hlavička (projekt = `cwd`, branch, session id), potom prompty s časom ako `### 14:03` + text. Ak sedenie pokračuje cez polnoc, prompty ďalšieho dňa idú do poznámky v adresári ďalšieho dňa.
- **Idempotencia:** stav importu v DB (`session_id` → posledný naimportovaný `uuid`/offset v súbore). Opakovaný sync doplní len nové prompty.
- Importované poznámky sú **len na čítanie** v editore (alebo editovateľné s upozornením, že sync ich prepíše – rozhodnúť v M7; odporúčanie: read-only + možnosť „skopírovať do vlastnej poznámky“).
- Spúšťanie: pri štarte, periodicky (napr. každých 10 min) a voliteľne v reálnom čase cez `FileSystemWatcher` na `~/.claude/projects`.
- **Dôležité:** Claude Code štandardne maže staré transkripty (nastavenie `cleanupPeriodDays` v `~/.claude/settings.json`). Aplikácia by na to mala upozorniť a ponúknuť zvýšenie hodnoty.
- **Súkromie:** prompty môžu obsahovať citlivé údaje (tokeny, heslá, interné cesty). Pred zápisom do OneDrive aplikovať voliteľný filter (regex na API kľúče, `sk-…`, `ghp_…`, connection stringy) a umožniť vylúčiť konkrétne projekty.
- Alternatíva/rozšírenie: Claude Code **hook `UserPromptSubmit`**, ktorý zapíše prompt priamo (malý CLI `wolfmark-cli capture`) – real-time bez parsovania transkriptov.

---

## 5. Vizuálny dizajn

Inšpirácia: aplikácia **Finalist 4** (referenčné screenshoty: ročný plánovač a denný pohľad). Cieľom nie je kópia, ale prevzatie charakteru a prepis do Fluent/WinUI.

### 5.1 Charakter

- Svetlé, teplé „papierové“ pozadie (jemne sivobéžové), hlavný obsah na zaoblenej karte s mäkkým tieňom; v tmavom režime tmavý papier, nie čistá čierna.
- Výrazná **veľká typografia dátumu** (veľké číslo dňa + deň v týždni, napr. „15 · STREDA“).
- **Farby tagov sú hlavný vizuálny jazyk:** sýte, plné farby (modrá, žltá, oranžová, červená, zelená, tyrkysová, fialová…) na bunkách kalendára a ako zvislý pruh / pilulka pri poznámkach.
- Tagy ako **zaoblené pilulky** s farebnou bodkou a počtom (napr. `● praca 3`).
- Kompaktné, veľké písmená pre malé popisky (mesiac, sekcie: „OKTÓBER 2026“, „DNES“, „STARŠIE“).
- Spodná **týždenná lišta s krúžkami dní** – v našom prípade hore pod title barom; krúžok môže zobrazovať progres (pomer splnených úloh).

### 5.2 Plný kalendár (ročný/mesačný pohľad)

- Vertikálne scrollovateľný zoznam mesiacov; každý mesiac = mriežka 7 stĺpcov (Po–Ne) + stĺpec čísla týždňa.
- **Bunka dňa:** číslo dňa, pozadie vyplnené farbami tagov daného dňa – pri viacerých tagoch rozdelené na diagonálne pruhy/segmenty v pomere počtu poznámok; malá značka počtu záznamov. Dni bez záznamov neutrálne.
- **Dnešok:** výrazne zvýraznený (tmavá výplň alebo rámček + šrafovanie).
- **„Lupa“ na týždeň:** pri hoveri/výbere týždňa sa zobrazí zväčšený pás týždňa s veľkými číslami a popisom dominantného tagu (ako v referenčnom dizajne).
- Pod týždňom voliteľne textové štítky (napr. prvý titulok poznámky so špeciálnym tagom).
- **Legenda tagov** dole ako horizontálne pilulky; klik na tag → zvýrazní len dni s týmto tagom (ostatné stlmí).
- Prepínanie Rok / Mesiac; klik na deň → zatvorí kalendár a otvorí deň.
- Implementácia: vlastný virtualizovaný control (Win2D alebo `Canvas` s recyklovanými bunkami) – rok má ~365 buniek, musí scrollovať plynulo pri 120 Hz.

### 5.3 Fluent integrácia

- Mica backdrop, vlastný title bar (`ExtendsContentIntoTitleBar`) s obsahom, systémové accent farby len pre interaktívne prvky (nie pre tagy).
- Rešpektovať systémovú tému, vysoký kontrast, škálovanie DPI, animácie podľa nastavenia „Animačné efekty“.
- Prístupnosť: plné ovládanie klávesnicou, Narrator (AutomationProperties pre bunky kalendára: „15. október, 3 poznámky, praca, ClaudeCode“).

---

## 6. Asynchrónnosť a výkon

- **Vlákna:** UI vlákno (DispatcherQueue) – len UI. Background pipeline: `Channel<IndexWorkItem>` s jedným zapisovačom do DB, samostatný kanál pre embeddingy s nízkou prioritou (`ThreadPriority.BelowNormal` alebo obmedzený počet workerov).
- **Žiadne `.Result` / `.Wait()`**, všetky I/O API asynchrónne, `CancellationToken` všade, kde operácia môže byť zrušená (prepnutie dňa zruší načítavanie predchádzajúceho).
- **Štart:** okno sa zobrazí okamžite s poslednou poznámkou (cesta uložená v lokálnych nastaveniach → priame načítanie jedného súboru). Index, kalendár a história sa načítajú paralelne; plný scan a sync bežia až po zobrazení.
- **Výkonnostné ciele:**
  - studený štart do editovateľného editora < 300 ms (AOT),
  - latencia písania < 16 ms aj pri 5 000-riadkovom dokumente,
  - prepnutie dňa v kalendári < 50 ms,
  - vyhľadávanie (FTS) < 50 ms na 10 000 poznámkach, hybridné < 200 ms,
  - plný reindex 10 000 poznámok bez embeddingov < 10 s.
- Benchmarky (BenchmarkDotNet) pre parser názvov, lexer a indexer.

---

## 7. Ďalšie odporúčané funkcie (mimo pôvodného zadania)

Rozdelené podľa priority; zaradené do backlogu ako samostatné míľniky alebo voliteľné položky.

**Odporúčané do MVP**
- Globálna klávesová skratka (napr. `Win+Shift+N`) na **rýchly zápis** – malé okno, napíšem, Enter, zmizne. Najväčší rozdiel medzi „aplikáciou, ktorú otváram“ a „zápisníkom, ktorý používam“.
- Ikona v system tray, beh na pozadí, jedna inštancia (druhé spustenie aktivuje existujúce okno).
- Hybridné vyhľadávanie (FTS5 + vektory) s command paletou `Ctrl+K`.
- Indikátor stavu ukladania a OneDrive stavu (synchronizované / len online / konflikt).
- Kôš: mazanie do koša Windows, nikdy natrvalo.

**Vysoká hodnota**
- **Úlohy:** `- [ ]` v poznámkach sa počítajú, kalendár ukazuje otvorené úlohy, pohľad „Nedokončené“ (ako „Outstanding“ v referencii) naprieč dňami.
- **Wiki odkazy** `[[2026-10-15]]` a `[[praca_1403]]`, spätné odkazy pod poznámkou.
- **Šablóny** pre nový deň/tag (napr. tag `meeting` → predvyplnená štruktúra).
- **Denný prehľad:** pohľad na celý deň – všetky poznámky dňa za sebou ako jedna stránka (Journal štýl).
- **Export** dňa/týždňa/tagu do jedného `.md` alebo PDF (napr. pre týždenný report).
- **Podobné poznámky** pod editorom – využitie vektorového indexu („k tejto téme si písal 3. 9.“).
- Jump list v taskbare (Nová poznámka, Dnes, Vyhľadať) a protokol `wolfmark://` (otvorenie konkrétneho dňa/poznámky z iných aplikácií).

**Neskôr / voliteľné**
- **MCP server** (lokálny), cez ktorý môže Claude Code/Claude Desktop čítať a vyhľadávať v zápisníku – prirodzené doplnenie importu promptov opačným smerom.
- Integrácia Outlook/Google kalendára ako read-only vrstva (udalosti dňa vedľa poznámok).
- Shrnutie dňa/týždňa cez LLM (lokálne alebo cez API), uložené ako poznámka s tagom `summary`.
- Hlasový záznam s prepisom (Windows Speech / Whisper).
- Šifrované poznámky (tag `private` → obsah šifrovaný DPAPI/heslom) – OneDrive nie je vhodné miesto pre všetko.
- Verzovanie: voliteľne git repozitár v koreňovom adresári s automatickým commitom raz denne.
- Štatistiky: heatmapa písania, počet slov, najčastejšie tagy.

---

## 8. Nefunkčné požiadavky

- **Podporované OS:** Windows 11 (22H2+); Windows 10 22H2 len ak to nič nekomplikuje.
- **Architektúry:** `win-x64` a `win-arm64`.
- **Distribúcia:** nezabalená (unpackaged) aplikácia s `WindowsAppSDKSelfContained=true` pre jednoduché spúšťanie, neskôr MSIX + `winget` manifest. Overiť dopad MSIX na prístup k OneDrive a `~/.claude` (MSIX s `runFullTrust` je OK).
- **Lokalizácia:** slovenčina a angličtina (`.resw`), formát dátumu podľa systému.
- **Telemetria:** žiadna. Logy lokálne, crash dump lokálne.
- **Bezpečnosť:** žiadne sieťové volania okrem voliteľných (stiahnutie embedding modelu, budúce LLM). Model sťahovať z overeného zdroja, overiť hash.
- **Robustnosť:** index sa musí vedieť zotaviť z poškodenia (detekcia `PRAGMA integrity_check` pri štarte → rebuild).

---

## 9. Otvorené otázky (rozhodnúť pred/počas implementácie)

1. Prázdna poznámka bez tagu – názov `HHmm.md`, alebo vždy predvolený tag (napr. `poznamka_HHmm.md`) kvôli konzistentnej farbe?
2. Majú byť importované Claude Code poznámky editovateľné?
3. Jeden primárny tag v názve + ďalšie v texte (navrhnuté), alebo striktne jeden tag?
4. Časové pásmo: ukladať čas lokálne (navrhnuté, zodpovedá adresárom), pri cestovaní akceptovať posun.
5. Pri zmene koreňového adresára presúvať súbory automaticky, alebo len prepnúť?
6. Má editor podporovať viac otvorených poznámok (taby), alebo striktne jednu?

---

## 10. Referencie pre Claude Code

### .NET 11 a AOT
- .NET 11 download: https://dotnet.microsoft.com/download/dotnet/11.0
- Čo je nové v .NET 11: https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/overview
- Native AOT deployment: https://learn.microsoft.com/dotnet/core/deploying/native-aot/
- Príprava knižníc na trimming/AOT: https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming
- System.Text.Json source generation: https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation
- `global.json`: https://learn.microsoft.com/dotnet/core/tools/global-json
- System.Threading.Channels: https://learn.microsoft.com/dotnet/core/extensions/channels

### WinUI 3 / Windows App SDK
- WinUI 3 dokumentácia: https://learn.microsoft.com/windows/apps/winui/winui3/
- Windows App SDK: https://learn.microsoft.com/windows/apps/windows-app-sdk/
- Release notes Windows App SDK (overiť poslednú stabilnú verziu): https://learn.microsoft.com/windows/apps/windows-app-sdk/stable-channel
- Vytvorenie WinUI projektu: https://learn.microsoft.com/windows/apps/get-started/start-here
- Self-contained deployment: https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- Unpackaged aplikácie: https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps
- Title bar customizácia: https://learn.microsoft.com/windows/apps/develop/title-bar
- Mica: https://learn.microsoft.com/windows/apps/design/style/mica
- RichEditBox: https://learn.microsoft.com/windows/apps/design/controls/rich-edit-box
- Win2D: https://github.com/microsoft/Win2D
- Windows App SDK repo + samples: https://github.com/microsoft/WindowsAppSDK , https://github.com/microsoft/WindowsAppSDK-Samples
- WinUI Gallery (ukážky controlov): https://github.com/microsoft/WinUI-Gallery
- Windows App Development CLI (`winapp`, podporuje Native AOT): https://github.com/microsoft/winappCli
- CommunityToolkit.Mvvm: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/
- Windows Community Toolkit (AOT podpora od 8.2): https://github.com/CommunityToolkit/Windows
- CsWin32: https://github.com/microsoft/CsWin32

### Dáta a vyhľadávanie
- Microsoft.Data.Sqlite: https://learn.microsoft.com/dotnet/standard/data/sqlite/
- Načítanie SQLite extension: https://learn.microsoft.com/dotnet/standard/data/sqlite/extensions
- SQLite FTS5: https://www.sqlite.org/fts5.html
- SQLite WAL: https://www.sqlite.org/wal.html
- sqlite-vec: https://github.com/asg017/sqlite-vec , NuGet: https://www.nuget.org/packages/sqlite-vec/
- ONNX Runtime C#: https://onnxruntime.ai/docs/get-started/with-csharp.html
- Microsoft.ML.Tokenizers: https://learn.microsoft.com/dotnet/api/microsoft.ml.tokenizers
- Markdig: https://github.com/xoofx/markdig
- CommonMark spec: https://spec.commonmark.org/ , GFM: https://github.github.com/gfm/

### Súbory a OneDrive
- FileSystemWatcher: https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher
- Cloud Files API (placeholdery, Files On-Demand): https://learn.microsoft.com/windows/win32/cfapi/cloud-files-api-portal
- File attribute constants: https://learn.microsoft.com/windows/win32/fileio/file-attribute-constants

### Claude Code
- Dokumentácia: https://code.claude.com/docs
- Nastavenia (`cleanupPeriodDays`): https://code.claude.com/docs/en/settings
- Hooks (`UserPromptSubmit`): https://code.claude.com/docs/en/hooks
- MCP: https://modelcontextprotocol.io/
