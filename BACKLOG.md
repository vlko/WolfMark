# Zápisník – backlog

Backlog je zoradený podľa míľnikov. Každý míľnik končí **funkčným, otestovaným stavom**, ktorý sa dá spustiť. Položky majú ID (`M1-03`), aby sa na ne dalo odkazovať v promptoch pre Claude Code („implementuj M2-04“).

Formát položky: **ID – názov** · popis · *Akceptačné kritériá (AK)*.

Legenda: 🟢 MVP · 🟡 odporúčané · ⚪ voliteľné

Stav: ✅ hotové a overené testami · 🟨 implementované, čaká na overenie na Windows / čiastočne · ⬜ otvorené

---

## M0 – Kostra projektu 🟢

- ✅ **M0-01 – Solution a projekty.** Vytvoriť štruktúru zo SPEC §2 (Core, Storage, Index, Sync.ClaudeCode, Editor, App + testy). `global.json` pre .NET 11, `Directory.Build.props` so spoločnými nastaveniami (`Nullable`, `TreatWarningsAsErrors`, `IsAotCompatible`, AOT/trim analyzéry).
  *Stav:* Build bez warningov (TreatWarningsAsErrors), Core/Storage/Index/Sync sú `net11.0` bez WinUI.
  *AK:* `dotnet build` prejde bez warningov; Core/Storage/Index nemajú referenciu na WinUI.
- 🟨 **M0-02 – Prázdna WinUI 3 aplikácia s Native AOT.** Okno s Mica a vlastným title barom, `PublishAot=true`, `WindowsAppSDKSelfContained=true`, RID `win-x64` a `win-arm64`.
  *Stav:* Mica, vlastný title bar, `PublishAot`, self-contained, x64+ARM64. IL trimmer nad celou aplikáciou bez warningov; AOT publish treba spustiť na Windows (CI).
  *AK:* `dotnet publish -c Release -r win-x64` vyprodukuje spustiteľný AOT binár; aplikácia sa spustí na čistom stroji bez nainštalovaného .NET.
- ✅ **M0-03 – Logovanie a DI.** Composition root, `ILogger` so súborovým sinkom v `%LOCALAPPDATA%\Zapisnik\logs`, globálny handler neošetrených výnimiek.
  *Stav:* `AppHost` (ADR 0002), `FileLoggerProvider`, `BackgroundRunner` – test: výnimka sa zaloguje. Globálne handlery v `App`.
  *AK:* Výnimka v background tasku sa zaloguje a nezhodí aplikáciu.
- 🟨 **M0-04 – CI.** GitHub Actions: build, testy, AOT publish pre x64 (artefakt).
  *Stav:* `.github/workflows/ci.yml` (Linux testy + Windows build/test/AOT publish x64/arm64) – zatiaľ nespustené.
  *AK:* Zelený pipeline na push.
- 🟨 **M0-05 – Jedna inštancia.** Druhé spustenie aktivuje existujúce okno (Windows App SDK `AppInstance`).
  *Stav:* `AppInstance.FindOrRegisterForKey` + redirect bez blokovania STA; neoverené na Windows.
  *AK:* Dve spustenia = jedno okno.

## M1 – Directory engine 🟢

- ✅ **M1-01 – `NotePath` parser.** Parsovanie/generovanie ciest a názvov podľa SPEC §3.3 (tag, čas, sekundy, kolízny suffix, neštandardné názvy, diakritika, NFC).
  *Stav:* `NoteName`/`NotePath` + testy vrátane round-tripu a konfliktných kópií.
  *AK:* Unit testy pokrývajú všetky varianty gramatiky vrátane neplatných vstupov; round-trip parse→format je identita.
- ✅ **M1-02 – Konfigurácia koreňového adresára.** Detekcia OneDrive (premenné `OneDrive`, `OneDriveConsumer`, `OneDriveCommercial`), fallback, uloženie v lokálnych nastaveniach (STJ source-gen).
  *Stav:* `RootDirectoryResolver` + testy.
  *AK:* Na stroji s OneDrive je predvolený adresár v OneDrive; bez neho v Dokumentoch.
- ✅ **M1-03 – Asynchrónna enumerácia.** `IAsyncEnumerable<NoteFile>` cez celý strom; nespúšťa stiahnutie obsahu OneDrive placeholderov.
  *Stav:* 10 000 súborov < 2 s (test); enumerácia číta len metadáta (`FileSystemEnumerable`), placeholder atribúty testované.
  *AK:* Enumerácia 10 000 súborov < 2 s; test s placeholder atribútmi (mock) nečíta obsah.
- ✅ **M1-04 – CRUD a atomický zápis.** Create (s riešením kolízií), Read, Save (temp + replace), Rename (zmena tagu), Delete (do koša cez Shell API).
  *Stav:* Atomický zápis + test prerušeného zápisu; kôš: `WindowsRecycleBin` (SHFileOperation, CsWin32), v testoch `FolderRecycleBin`.
  *AK:* Simulovaný pád počas zápisu nezanechá poškodený ani prázdny súbor; zmazaný súbor je v koši.
- ✅ **M1-05 – File watcher a doménové udalosti.** `FileSystemWatcher` → `Channel` → debounce → `NoteEvent` (union typ). Pri pretečení bufferu plný rescan. Potlačenie ech vlastných zápisov.
  *Stav:* Test: externá úprava = presne 1 `NoteChanged`, vlastné uloženie/premenovanie/zmazanie = 0 externých udalostí.
  *AK:* Úprava súboru v Notepade vyvolá presne jednu `NoteChanged`; vlastný save nevyvolá žiadnu.
- ✅ **M1-06 – Detekcia OneDrive konfliktných kópií.**
  *Stav:* `praca_1403-PC.md` → `ConflictOriginal`; v UI InfoBar + počítadlo konfliktov.
  *AK:* Súbor `praca_1403-PC.md` je rozpoznaný ako konflikt k `praca_1403.md`.
- ✅ **M1-07 – Tagy a farby.** Načítanie/zápis `.zapisnik/tags.json`, deterministické priradenie farby z palety, reakcia na externú zmenu súboru.
  *Stav:* FNV-1a hash → paleta 14 farieb; test „dva čisté stroje“.
  *AK:* Dva čisté stroje priradia novému tagu rovnakú farbu.

## M2 – Index: SQLite, FTS5, vektory 🟢

- ✅ **M2-01 – Schéma a single-writer.** Schéma zo SPEC §4.2, WAL, jedno zapisovacie spojenie za kanálom, migrácie cez `meta.schema_version`.
  *Stav:* Single writer + test 200 paralelných zápisov.
  *AK:* Paralelné požiadavky na zápis sa serializujú bez `database is locked`.
- ✅ **M2-02 – Inkrementálny indexer.** Odber `NoteEvent` + počiatočný sken; porovnanie `mtime` + hash; extrakcia titulku, náhľadu, tagov (názov, `#tag` v texte, front matter), počtu slov a úloh.
  *Stav:* mtime+size+hash; testy „druhý sken nič“, „1 zmena = 1 reindex“.
  *AK:* Druhé spustenie bez zmien nereindexuje nič; zmena jedného súboru reindexuje práve jeden.
- ✅ **M2-03 – FTS5 vyhľadávanie.** Tokenizer `unicode61 remove_diacritics 2`, zvýraznenie výsledkov (`highlight`/`snippet`).
  *Stav:* „poznamka“ nájde „poznámka“, snippet/highlight; FTS < 50 ms na 10 000 poznámkach (test). ADR 0003.
  *AK:* „poznamka“ nájde „poznámka“; dotaz < 50 ms na 10 000 poznámkach.
- 🟨 **M2-04 – sqlite-vec integrácia.** Načítanie extension pod Native AOT (x64 aj arm64 natívne binárky v publish výstupe).
  *Stav:* vec0 sa načíta a testy prechádzajú na linux-x64; `vec0.dll` je vo win-x64 výstupe. Balík nemá win-arm64 → na ARM64 vypnuté.
  *AK:* AOT publish spustí `SELECT vec_version()` úspešne.
- 🟨 **M2-05 – Embedding provider.** Rozhranie `IEmbeddingProvider`; implementácia ONNX Runtime + multilingválny model; stiahnutie modelu pri prvom použití s overením hashu; chunking; dávkové počítanie v pozadí s nízkou prioritou.
  *Stav:* Čiastočne: `IEmbeddingProvider`, chunking, `EmbeddingIndexer` (nízka priorita, zmena modelu = prepočet). **ONNX provider + model chýba.**
  *AK:* Indexovanie embeddingov nezvýši latenciu UI (merať); AOT build bez trim warningov. Ak ONNX pod AOT zlyhá, zdokumentovať a navrhnúť alternatívu.
- ✅ **M2-06 – Hybridné vyhľadávanie.** FTS5 + vektorové výsledky zlúčené cez RRF; API `SearchAsync(query, filters, ct)`.
  *Stav:* RRF; test sémantického dotazu s testovacím providerom.
  *AK:* Sémantický dotaz („čo som riešil s deploymentom“) nájde relevantné poznámky bez presnej zhody slov.
- ✅ **M2-07 – Rebuild a kontrola integrity.** Príkaz rebuild s progresom; `integrity_check` pri štarte → automatický rebuild.
  *Stav:* `quick_check` + schema version, obnova poškodenej/zmazanej DB, `ResetAsync` (testy).
  *AK:* Zmazanie `index.db` počas behu → aplikácia ho postaví znovu bez pádu.

## M3 – Kalendár v databáze 🟢

- ✅ **M3-01 – Agregácia `calendar_days`.** Udržiavaná indexerom (počet, rozpad tagov).
  *Stav:* Prepočet dní v tej istej transakcii; test „do 1 s“.
  *AK:* Pridanie poznámky aktualizuje deň do 1 s.
- ✅ **M3-02 – Calendar service API.** `GetWeekAsync`, `GetMonthAsync`, `GetYearAsync`, ISO týždne, pondelok ako prvý deň.
  *Stav:* Testy týždeň 53 a 1. 1. v nedeľu.
  *AK:* Testy na hraniciach rokov (týždeň 53, 1. 1. v nedeľu).
- ✅ **M3-03 – Resync.** Plný a čiastočný (mesiac) resync z directory engine.
  *Stav:* `ScanMonthAsync`/`ScanDirectoryAsync` + test.
  *AK:* Ručne pridaný súbor do adresára (pri vypnutej aplikácii) sa po resyncu objaví v kalendári.
- ⬜ **M3-04 – Sviatky a meniny SK.** ⚪ Statická dátová vrstva.

## M4 – Markdown editor 🟢

- 🟨 **M4-01 – Spike: voľba prístupu.** Porovnať `RichEditBox` + vlastné farbenie vs. vlastný control (Win2D/DirectWrite). Merať latenciu písania na 5 000 riadkoch, pamäť, AOT kompatibilitu. Výsledok zapísať do `docs/adr/0001-editor.md`.
  *Stav:* ADR 0001 – rozhodnutie RichEditBox; merania latencie na Windows chýbajú.
  *AK:* ADR s meraniami a rozhodnutím.
- ✅ **M4-02 – Inkrementálny markdown lexer.** Riadkový lexer so stavom (bloky kódu, front matter), výstup = rozsahy tokenov.
  *Stav:* Test: úprava riadku = 1 retokenizovaný riadok.
  *AK:* Úprava jedného riadku prepočíta len dotknuté riadky (test); benchmark < 1 ms na riadok.
- 🟨 **M4-03 – Raw režim so zvýraznením.**
  *Stav:* Implementované, neoverené na Windows.
  *AK:* Písanie bez viditeľného oneskorenia; zvýraznenie dobehne asynchrónne.
- 🟨 **M4-04 – Formátovaný (live preview) režim.** Skrývanie značiek mimo aktívneho riadku, klikateľné checkboxy, inline obrázky, tabuľky.
  *Stav:* Skrývanie značiek mimo aktívneho riadku, klikateľné checkboxy; inline obrázky a tabuľky nie.
  *AK:* Prepnutie `Ctrl+E` zachová kurzor a scroll; text sa nemení (round-trip bez strát).
- 🟨 **M4-05 – Autosave a stav ukladania.** Debounce, uloženie pri strate fokusu/zatvorení, indikátor stavu.
  *Stav:* Debounce 800 ms, flush pri strate fokusu/deaktivácii/zatvorení, indikátor stavu.
  *AK:* Zabitie procesu po 1 s nečinnosti nestratí text.
- ✅ **M4-06 – Editovacie skratky.** Bold/italic/link, checkbox, odsadenie, pokračovanie zoznamu.
  *Stav:* Logika v `MarkdownEditing` + testy; napojenie v editore neoverené na Windows.
- 🟨 **M4-07 – Vkladanie obrázkov** do `assets/` dňa.
- 🟨 **M4-08 – Externé zmeny otvoreného súboru** (tichý reload / porovnanie pri konflikte).
  *Stav:* Tichý reload / InfoBar „Načítať z disku | Ponechať moje“; diff/merge UI nie.
- 🟨 **M4-09 – Hlavné okno v1.** Hlavička poznámky (výber tagu s farbou, čas, prepínač režimu, `+`), `Ctrl+N`, otvorenie poslednej poznámky pri štarte, vytvorenie súboru až po prvom znaku.
  *Stav:* Súbor vzniká až prvým znakom; posledná poznámka sa otvára bez indexu. Štart < 300 ms nezmeraný.
  *AK:* Od spustenia po písanie < 300 ms (AOT, SSD).

## M5 – História záznamov 🟢

- 🟨 **M5-01 – Virtualizovaný zoznam** pod editorom s posuvným oddeľovačom, farebný pruh tagu, dátum, čas, titulok, náhľad; stránkovanie z indexu.
  *AK:* Plynulý scroll cez 10 000 položiek.
- 🟨 **M5-02 – Filtre** podľa tagu a dňa; živá aktualizácia pri nových/zmenených poznámkach.
- 🟨 **M5-03 – Vyhľadávanie `Ctrl+K`** (command paleta nad `SearchAsync`), filtre `#tag`, `@YYYY-MM`.
- 🟨 **M5-04 – Kontextové akcie:** zmena tagu, otvoriť v Prieskumníkovi, zmazať, kopírovať odkaz.

## M6 – Kalendár v UI 🟢

- 🟨 **M6-01 – Kompaktná týždenná lišta** pod title barom: dni s počtom záznamov a farebnými prúžkami tagov, navigácia týždňov, dnešok zvýraznený.
  *Stav:* Krúžky dní s farebnými segmentmi tagov a progresom úloh.
- 🟨 **M6-02 – Plný kalendár (ročný pohľad)** podľa SPEC §5.2: mriežka mesiacov s číslami týždňov, bunky vyplnené farbami tagov, dnešok, legenda tagov s filtrovaním.
  *AK:* Plynulý scroll celým rokom; klik na deň otvorí deň.
- 🟨 **M6-03 – „Lupa“ na týždeň** a štítky pod týždňom.
- 🟨 **M6-04 – Mesačný pohľad** a prepínanie Rok/Mesiac.
- 🟨 **M6-05 – Prístupnosť kalendára** (klávesnica, Narrator popisy buniek).
  *Stav:* Šípky/Enter/Esc, Narrator popisy buniek.
- 🟨 **M6-06 – Vizuálne doladenie** podľa referencie (papierové pozadie, karta, typografia dátumu, pilulky tagov), light/dark.

## M7 – Synchronizácia z Claude Code 🟢

- ✅ **M7-01 – Tolerantný JSONL parser** (`history.jsonl`, transkripty v `projects/`), STJ source-gen, neznáme polia ignorovať.
  *Stav:* Testy na viacerých formátoch, poškodený riadok sa preskočí.
  *AK:* Testy na vzorových súboroch z viacerých verzií Claude Code; poškodený riadok sa preskočí.
- ✅ **M7-02 – Filtrovanie promptov človeka** (bez `tool_result`, systémových a kompakčných správ).
- ✅ **M7-03 – Zápis poznámok** `ClaudeCode_HHmm.md` po sedeniach, rozdelenie cez polnoc, hlavička (projekt, branch, session).
  *Stav:* Test rozdelenia cez polnoc.
- ✅ **M7-04 – Idempotentný inkrementálny sync** so stavom v DB.
  *Stav:* Test: žiadne duplicity aj po zmazaní indexu (hľadá podľa `session:` vo front matter).
  *AK:* Opakovaný sync nevytvorí duplicity; nový prompt sa doplní do existujúcej poznámky.
- 🟨 **M7-05 – Spúšťanie:** pri štarte, periodicky, voliteľne watcher.
  *Stav:* `ClaudeCodeSyncScheduler`: štart, interval, voliteľný watcher.
- ✅ **M7-06 – Filter citlivých údajov** a vylúčenie projektov.
- ✅ **M7-07 – Upozornenie na `cleanupPeriodDays`.**
  *Stav:* Kontrola + zápis do `settings.json` (test); InfoBar v UI.
- ⬜ **M7-08 – ⚪ Hook `UserPromptSubmit`** + malý CLI `zapisnik-cli capture` pre real-time zápis.

## M8 – Pohodlie a integrácia s Windows 🟡

- ⬜ **M8-01 – Globálna skratka rýchleho zápisu** (malé okno, Enter uloží a skryje).
- ⬜ **M8-02 – Tray ikona**, minimalizácia do tray, štart s Windows.
- ⬜ **M8-03 – Jump list** a protokol `zapisnik://`.
- 🟨 **M8-04 – Nastavenia** (SPEC §4.6) vrátane zmeny koreňového adresára s presunom.
  *Stav:* Čiastočne: priečinok (presun/prepnutie), téma, backdrop, jazyk, editor, Claude Code, rebuild indexu.
- ⬜ **M8-05 – Premenovanie a zlúčenie tagov** (hromadné premenovanie súborov s rollbackom pri chybe).
  *Stav:* Zmena farby a štítkov tagu je hotová; hromadné premenovanie/zlúčenie nie.
- 🟨 **M8-06 – Lokalizácia** sk/en.
  *Stav:* `.resw` sk/en generované z jednej tabuľky (`tools/gen-strings.py`).
- 🟨 **M8-07 – Indikátor OneDrive stavu** a UI pre konflikty.
  *Stav:* Čiastočne: konflikty v UI, ikona pre placeholdery; stav synchronizácie OneDrive nie.

## M9 – Rozšírenia 🟡/⚪

- 🟨 **M9-01 – 🟡 Úlohy:** pohľad „Nedokončené“ naprieč dňami, počty v kalendári.
  *Stav:* Čiastočne: filter „Nedokončené“, progres úloh v týždennej lište.
- ⬜ **M9-02 – 🟡 Denný pohľad** (všetky poznámky dňa ako jedna stránka).
- ⬜ **M9-03 – 🟡 Podobné poznámky** pod editorom (vektory).
- ⬜ **M9-04 – 🟡 Wiki odkazy a spätné odkazy.**
- ⬜ **M9-05 – 🟡 Šablóny** pre tagy.
- ⬜ **M9-06 – 🟡 Export** dňa/týždňa/tagu do `.md`/PDF.
- ⬜ **M9-07 – ⚪ Lokálny MCP server** na čítanie/vyhľadávanie v zápisníku pre Claude.
- ⬜ **M9-08 – ⚪ Read-only vrstva Outlook/Google kalendára.**
- ⬜ **M9-09 – ⚪ LLM zhrnutie dňa/týždňa.**
- ⬜ **M9-10 – ⚪ Šifrované súkromné poznámky.**
- ⬜ **M9-11 – ⚪ Štatistiky a heatmapa.**

## M10 – Distribúcia 🟡

- ⬜ **M10-01 – MSIX balík** a podpis; overiť prístup k OneDrive a `~/.claude`.
- ⬜ **M10-02 – winget manifest.**
- ⬜ **M10-03 – Automatické aktualizácie** (MSIX App Installer alebo vlastný mechanizmus).
