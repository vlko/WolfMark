# WolfMark – backlog

Backlog je zoradený podľa míľnikov. Každý míľnik končí **funkčným, otestovaným stavom**, ktorý sa dá spustiť. Položky majú ID (`M1-03`), aby sa na ne dalo odkazovať v promptoch pre Claude Code („implementuj M2-04“).

Formát položky: **ID – názov** · popis · *Akceptačné kritériá (AK)*.

Legenda: 🟢 MVP · 🟡 odporúčané · ⚪ voliteľné

Stav: ✅ hotové a overené testami · 🟨 implementované, čaká na overenie na Windows / čiastočne · ⬜ otvorené

---

## M0 – Kostra projektu 🟢

- ✅ **M0-01 – Solution a projekty.** Vytvoriť štruktúru zo SPEC §2 (Core, Storage, Index, Sync.ClaudeCode, Editor, App + testy). `global.json` pre .NET 11, `Directory.Build.props` so spoločnými nastaveniami (`Nullable`, `TreatWarningsAsErrors`, `IsAotCompatible`, AOT/trim analyzéry).
  *Stav:* Build bez warningov (TreatWarningsAsErrors), Core/Storage/Index/Sync sú `net11.0` bez WinUI.
  *AK:* `dotnet build` prejde bez warningov; Core/Storage/Index nemajú referenciu na WinUI.
- ✅ **M0-02 – Prázdna WinUI 3 aplikácia s Native AOT.** Okno s Mica a vlastným title barom, `PublishAot=true`, `WindowsAppSDKSelfContained=true`, RID `win-x64` a `win-arm64`.
  *Stav:* Mica, vlastný title bar, `PublishAot`, self-contained, x64+ARM64. IL trimmer nad celou aplikáciou bez warningov; AOT publish beží v CI.
  Smoke test (`tools/smoke-windows.ps1`, CI po AOT publishi): importy všetkých binárok sú vo výstupe alebo súčasť
  Windows (bez .NET a VC++ redistributable), binárka sa spustí a vyrenderuje okno. Lokálne na Windows (2026-11-16):
  bez runtime súborov, štart a vykreslenie OK. Importy všetkých 37 binárok (vrátane oneskorených, `e_sqlite3`,
  `vec0`) prečítané z PE hlavičiek: len systémové DLL Windows, žiadny VC++ runtime ani .NET (WinAppSDK a Win2D majú
  CRT staticky).
  *AK:* `dotnet publish -c Release -r win-x64` vyprodukuje spustiteľný AOT binár; aplikácia sa spustí na čistom stroji bez nainštalovaného .NET.
- ✅ **M0-03 – Logovanie a DI.** Composition root, `ILogger` so súborovým sinkom v `%LOCALAPPDATA%\WolfMark\logs`, globálny handler neošetrených výnimiek.
  *Stav:* `AppHost` (ADR 0002), `FileLoggerProvider`, `BackgroundRunner` – test: výnimka sa zaloguje. Globálne handlery v `App`.
  *AK:* Výnimka v background tasku sa zaloguje a nezhodí aplikáciu.
- ✅ **M0-04 – CI.** GitHub Actions: build, testy, AOT publish pre x64 (artefakt).
  *Stav:* `.github/workflows/ci.yml` beží na každý push (Linux testy, Windows build/test/AOT publish x64/arm64,
  Avalonia AOT linux-x64/osx-arm64), `release.yml` na tag. Občasný pád testov na win-x64 (4 z 36 behov):
  `Full_reindex_of_10000_notes_is_fast` mal na runneri s Defenderom príliš tesný limit 10 s → 30 s; anotácie potom
  ukázali aj `External_edit_raises_exactly_one_change` – skutočná chyba watchera na Windows, opravená (M1-05).
  Padnuté testy sa vypíšu ako anotácie (`tools/ci-test-annotations.py`), ktoré sú verejné, hoci logy jobov
  vyžadujú admin práva. Akcie na Node 24 (checkout v7, setup-dotnet v6, upload v7, download v8).
  *AK:* Zelený pipeline na push.
- ✅ **M0-05 – Jedna inštancia.** Druhé spustenie aktivuje existujúce okno (Windows App SDK `AppInstance`).
  *Stav:* `AppInstance.FindOrRegisterForKey` + redirect bez blokovania STA. Smoke test: druhé spustenie na rovnakých
  dátach odovzdá aktiváciu (log „Activation redirected…“) a skončí, iný dátový priečinok = vlastná inštancia.
  Overené smoke testom na Windows (2026-11-16); že sa okno naozaj dostane do popredia, smoke test nevidí.
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
  Windows občas doručí po debounce ešte jeden oneskorený `Changed` k tomu istému zápisu (zachytené na CI 2026-11-13) →
  externá zmena s obsahom, ktorý sme pre danú cestu už ohlásili, sa neohlási znova (po vlastnom uložení áno).
  *AK:* Úprava súboru v Notepade vyvolá presne jednu `NoteChanged`; vlastný save nevyvolá žiadnu.
- ✅ **M1-06 – Detekcia OneDrive konfliktných kópií.**
  *Stav:* `praca_1403-PC.md` → `ConflictOriginal`; v UI InfoBar + počítadlo konfliktov.
  *AK:* Súbor `praca_1403-PC.md` je rozpoznaný ako konflikt k `praca_1403.md`.
- ✅ **M1-07 – Tagy a farby.** Načítanie/zápis `.wolfmark/tags.json`, deterministické priradenie farby z palety, reakcia na externú zmenu súboru.
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
- ↓ **M2-05 – Embedding provider** – odložené, presunuté do M9 – Rozšírenia (2026-11-13). Vyhľadávanie
  zatiaľ beží len nad FTS5; hybridná vrstva (M2-04, M2-06) je pripravená a zapne sa s providerom.
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

- ✅ **M4-01 – Spike: voľba prístupu.** Porovnať `RichEditBox` + vlastné farbenie vs. vlastný control (Win2D/DirectWrite). Merať latenciu písania na 5 000 riadkoch, pamäť, AOT kompatibilitu. Výsledok zapísať do `docs/adr/0001-editor.md`.
  *Stav:* Merania v ADR 0001 (RichEdit: kláves → snímka p95 144 ms na 5 000 riadkoch) a ADR 0007 (Win2D: 2,5 ms p50, ~20 ms p95); aplikácia používa vlastný Win2D editor, RichEdit editor odstránený.
  *AK:* ADR s meraniami a rozhodnutím.
- ✅ **M4-02 – Inkrementálny markdown lexer.** Riadkový lexer so stavom (bloky kódu, front matter), výstup = rozsahy tokenov.
  *Stav:* Test: úprava riadku = 1 retokenizovaný riadok.
  *AK:* Úprava jedného riadku prepočíta len dotknuté riadky (test); benchmark < 1 ms na riadok.
- ✅ **M4-03 – Raw režim so zvýraznením.**
  *Stav:* Vlastný Win2D editor (ADR 0007); overené na Windows (kláves → snímka p50 2,5 ms na 5 000 riadkoch).
  *AK:* Písanie bez viditeľného oneskorenia; zvýraznenie dobehne asynchrónne.
- ✅ **M4-04 – Formátovaný (live preview) režim.** Skrývanie značiek mimo aktívneho riadku, klikateľné checkboxy, inline obrázky, tabuľky.
  *Stav:* Skrývanie značiek (na riadku s kurzorom ako čipy), checkbox klikom, obrázky ako blok, prepnutie `Ctrl+E` drží pohľad. Tabuľky (GFM, `MarkdownTable`): mimo kurzora mriežka (hlavička, zarovnanie stĺpcov, zalomenie buniek, čísla prvého a posledného riadku s bodkami), s kurzorom vnútri Markdown zdroj so stlmenými `|`; klik na bunku dá kurzor do jej zdroja; kopírovanie ako HTML tabuľka / text oddelený tabulátormi; vzdialené mriežky sa stavajú, keď je UI voľné. Overené na Windows aj Avalonia (2026-10-21, `tools/gen-table-note.py`).
  *AK:* Prepnutie `Ctrl+E` zachová kurzor a scroll; text sa nemení (round-trip bez strát).
- ✅ **M4-05 – Autosave a stav ukladania.** Debounce, uloženie pri strate fokusu/zatvorení, indikátor stavu.
  *Stav:* Debounce 800 ms, flush pri strate fokusu/deaktivácii/zatvorení, indikátor stavu. Test „zabitie procesu“
  (`AutosaveKillTests`): testovací exe spustí sám seba ako podproces, ten píše do `NoteEditorViewModel` (spoločný
  pre Windows aj Avalonia), po 1 s nečinnosti ho rodič zabije bez flushu – text je na disku. Overené, že pri
  dlhšom debounce (5 s) test padne; pod záťažou CPU 12/12 behov zelených.
  *AK:* Zabitie procesu po 1 s nečinnosti nestratí text.
- ✅ **M4-06 – Editovacie skratky.** Bold/italic/link, checkbox, odsadenie, pokračovanie zoznamu.
  *Stav:* Logika v `MarkdownEditing` + testy; v editore overené na Windows, kontextové menu s typom riadku a formátmi.
- ✅ **M4-07 – Vkladanie obrázkov** do `assets/` dňa.
  *Stav:* Schránka (bitmapa aj súbory), drag & drop (alt text = názov súboru s príponou), náhľad; riadok len s obrázkom je blok (kurzor pred/za, šípky ho preskočia, text vedľa ide na nový riadok), dvojklik otvorí originál, hover nápoveda; len relatívne cesty v koreni poznámok. Overené na Windows.
- 🟨 **M4-08 – Externé zmeny otvoreného súboru** (tichý reload / porovnanie pri konflikte).
  *Stav:* Tichý reload / InfoBar „Načítať z disku | Ponechať moje“; diff/merge UI nie.
- ✅ **M4-09 – Hlavné okno v1.** Hlavička poznámky (výber tagu s farbou, čas, prepínač režimu, `+`), `Ctrl+N`, otvorenie poslednej poznámky pri štarte, vytvorenie súboru až po prvom znaku.
  *Stav:* Hotové. Štart do písania meraný (log + `tools/smoke-windows.ps1 -Checks startup`): **~545–565 ms**
  vo vyváženom režime napájania (2026-11-16; úsporný 1,2–1,6 s). Optimalizácie a merania v ADR 0011: editor bez
  rozloženia v konštruktore, fokus hneď po otvorení poznámky, vyhľadávanie pri prvom otvorení; réžia WinUI 3
  ~360 ms, preto pôvodných 300 ms nereálnych.
  *AK:* Od spustenia po písanie < 600 ms (AOT, vyvážený režim napájania; pôvodne 300 ms – ADR 0011).
- ✅ **M4-10 – Veľkosť písma a farebné emotikony.** `Ctrl` `+` / `Ctrl` `−` / `Ctrl+0`, `Ctrl`+koliesko (podľa znaku na rozložení), uloží sa do nastavení; farebné emotikony v Nastaveniach (Windows).
  *Stav:* Windows aj Avalonia (v0.3.2). Rýchly zoom kolieskom padal (natívny pád WinUI); obídené – zoom sa sčíta na jeden relayout, chyby kreslenia sa logujú („Editor drawing failed“). Skutočná príčina neznáma, čaká na záznam v logu.
- ✅ **M4-11 – Celé okno** (`F11`, na macOS `Ctrl+Cmd+F`, tlačidlo pred `+`): len dátum, riadok s časmi a `×`, formátovanie a editor.
  *Stav:* Windows overené; Avalonia skompilované, na Macu neoverené.
- ⬜ **M4-12 – Prístupnosť editora** (UI Automation text pattern pre čítačky obrazovky) – nový Win2D editor ju zatiaľ nemá (známe obmedzenie v0.3.1).

## M5 – História záznamov 🟢

- ✅ **M5-01 – Virtualizovaný zoznam** pod editorom s posuvným oddeľovačom, farebný pruh tagu, dátum, čas, titulok, náhľad; stránkovanie z indexu.
  *Stav:* Overené na Windows (2026-10-21): 10 000 poznámok z `tools/gen-history.py`, scroll kolieskom, posuvníkom aj s filtrom tagu bez zaseknutia UI (`--trace-editor`: žiadny UiStall ≥ 20 ms počas scrollu; štyri len pri štarte).
  *AK:* Plynulý scroll cez 10 000 položiek.
- ✅ **M5-02 – Filtre** podľa tagu a dňa; živá aktualizácia pri nových/zmenených poznámkach.
- ✅ **M5-03 – Vyhľadávanie `Ctrl+K`** (command paleta nad `SearchAsync`), filtre `#tag`, `@YYYY-MM`.
- ✅ **M5-04 – Kontextové akcie:** zmena tagu, otvoriť v Prieskumníkovi, zmazať, kopírovať odkaz.
  *Stav:* Windows mal menu hotové; Desktop (macOS/Linux) ho pri poznámkach nemal vôbec – doplnené (aj „Otvoriť
  priečinok“ pri externých súboroch, potvrdenie zmazania). Logika spoločná v `MainViewModel` (`HistoryActionsTests`).
  Desktop skompilovaný, na macOS/Linux nevyskúšaný (2026-11-16).

## M6 – Kalendár v UI 🟢

- ✅ **M6-01 – Kompaktná týždenná lišta** pod title barom: dni s počtom záznamov a farebnými prúžkami tagov, navigácia týždňov, dnešok zvýraznený.
  *Stav:* Týždenný pás podľa ADR 0004 (hranaté bunky, šrafované minulé dni, rám dneška, štítok).
- ✅ **M6-02 – Plný kalendár (ročný pohľad)** podľa SPEC §5.2: mriežka mesiacov s číslami týždňov, bunky vyplnené farbami tagov, dnešok, legenda tagov s filtrovaním.
  *AK:* Plynulý scroll celým rokom; klik na deň otvorí deň.
  *Stav:* Nahradené rozhodnutím ADR 0004 (2026-11-16): namiesto ročného pohľadu týždenný pás a po rozbalení mesačná
  mriežka (čísla týždňov, farby tagov, počty, dnešok, štítky medzi týždňami, filter cez lištu tagov).
- ✅ **M6-03 – „Lupa“ na týždeň** a štítky pod týždňom.
  *Stav:* Štítky medzi týždňami sú; „lupu“ nahrádza týždenný pás (ADR 0004).
- ✅ **M6-04 – Mesačný pohľad** a prepínanie Rok/Mesiac.
  *Stav:* Mesačný pohľad je; prepínač Rok/Mesiac odpadol s ročným pohľadom (ADR 0004).
- 🟨 **M6-05 – Prístupnosť kalendára** (klávesnica, Narrator popisy buniek).
  *Stav:* Šípky/Enter/Esc, Narrator popisy buniek.
- 🟨 **M6-06 – Vizuálne doladenie** podľa referencie (papierové pozadie, karta, typografia dátumu, pilulky tagov), light/dark.
  *Farby kalendára (2026-10-21, ADR 0004):* v týždennom páse šrafy minulých dní `\` každé 4 px a farby tagov stlmené (svetlá 40 %, tmavá 25 %), mesačná mriežka plné farby bez šrafov, číslo minulého dňa so záznamom = plná farba prvého tagu o 60 % tmavšia / o 80 % svetlejšia. Zatiaľ OK, **ešte doladiť** (čitateľnosť čísla nad pásom iného tagu, malé čísla v mesačnej mriežke, tmavá téma); náhľady `temp_design/calendar-*.png`.
  *~~Známy problém~~ (vyriešené vo verzii 0.6.0) – preblikávanie pri zmene veľkosti okna:* farby/text sa pri resize striedajú („zasedne“ ↔ „silno čierne“), občas tak ostanú; položky histórie sa pri resize na chvíľu vykreslia dvojmo. Zistenia: bez system backdropu sa odkryté plochy pri resize kreslia čierne; Mica nastavená v konštruktore okna preblikávala, nastavená po aktivácii pomohla čiastočne; text pravdepodobne prepína ClearType ↔ grayscale podľa kompozície (Mica, ContentDialog, live resize) – v réžii WinUI/Windows App SDK (2.5.1), nie .NET. Experiment: Nastavenia → Diagnostika → „Vykresľovanie okna“ (Mica vs. bez backdropu + pozadie triedy okna v farbe papiera) – **nepomohla ani jedna varianta** (overené 2026-10-16). Neoverené hypotézy na ďalšie kolo: ClearType ↔ grayscale (porovnať zväčšené screenshoty textu), prekresľovanie `ItemsRepeater` histórie pri zmene šírky (pevná výška riadkov), opakované `OnThemeChanged`/`Palette.Apply` počas resize (zalogovať). Vrátiť sa po ďalšej verzii Windows App SDK; ak pretrvá, nahlásiť do microsoft-ui-xaml s minimálnou ukážkou. *2026-11-13:* **vyriešené** aktualizáciou na Windows App SDK 2.6.5 (overené na Windows); diagnostický prepínač „Vykresľovanie okna“ odstránený, Mica sa nastavuje po prvej aktivácii.

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
- ✅ **M7-05 – Spúšťanie:** pri štarte, periodicky, voliteľne watcher.
  *Stav:* `ClaudeCodeSyncScheduler`: štart, interval, voliteľný watcher; `SyncSchedulerTests` s falošným časom
  (2026-11-13). Pri tom opravené: „Synchronizovať teraz“ pri zapnutom watcheri čakalo 5 s; zápisy počas 5 s okna
  spustili hneď ďalší, zbytočný sync.
- ✅ **M7-06 – Filter citlivých údajov** a vylúčenie projektov.
- ✅ **M7-07 – Upozornenie na `cleanupPeriodDays`.**
  *Stav:* Kontrola + zápis do `settings.json` (test); InfoBar v UI.
- ⬜ **M7-08 – ⚪ Hook `UserPromptSubmit`** + malý CLI `wolfmark-cli capture` pre real-time zápis.

## M8 – Pohodlie a integrácia s Windows 🟡

- ✅ **M8-01 – Globálna skratka rýchleho zápisu** (malé okno, Enter uloží a skryje). ADR 0012.
  *Stav:* Hotové, ručne overené na Windows a smoke `quicknote` 4/4 na Windows (2026-11-16): samostatné okno „Rýchla poznámka“ (nová poznámka dnešného
  dňa bez tagu, výber tagu, ukladá priebežne, Esc / Ctrl+Enter skryje, „Pokračovať v hlavnom okne“ = celé okno),
  hlavné okno sa nemení. Windows: `RegisterHotKey`, Nastavenia → Všeobecné → Rýchla poznámka (predvolene vypnutá,
  `Win+Shift+N`, nahratie stlačením, obsadená skratka = oznámenie). macOS/Linux: `--quick-note` pre systémovú
  skratku (príkaz na skopírovanie v Nastaveniach); funguje aj na Windows. Testy `KeyChordTests`, smoke `quicknote`
  (Linux 4/4 nad buildom vo WSL, 2026-11-16).
  *AK:* skratka otvorí okno nad inou aplikáciou aj so skrytým hlavným oknom; napísaný text je po Esc v novej
  poznámke (súbor), prázdna poznámka nevznikne; opakovaná skratka s rozpísaným textom ho nezahodí; obsadená
  skratka sa ohlási; vypnutá skratka sa neregistruje.
- ✅ **M8-02 – Tray ikona**, minimalizácia do tray, štart s Windows.
  *Stav:* Hotové, ručne overené na Windows (2026-11-16): Nastavenia → Windows (štart = HKCU `Run` s `--autostart`;
  pri zapnutom tray štartuje skryto), zatvorenie okna skryje do oblasti oznámení (`Infrastructure/TrayIcon.cs`,
  Shell_NotifyIcon + subclass okna), menu Zobraziť / Nastavenia… / Ukončiť.
- ✅ **M8-03 – Jump list** a protokol `wolfmark://`. ADR 0013.
  *Stav:* Hotové, ručne overené na Windows (protokol aj v demo režime, jump list mimo dema; 2026-11-16): odkazy `wolfmark://today|day/…|note/…|new|search?q=…`
  (len navigujú; cesta musí ostať v strome poznámok), jump list Nová poznámka / Dnes / Hľadať (`ICustomDestinationList`
  cez CsWin32; nie v deme a prenosnom režime), protokol prepínačom v Nastaveniach → Markdown súbory, „Kopírovať odkaz
  wolfmark://“ v histórii. Linux: akcie desktop entry + `x-scheme-handler/wolfmark`; macOS: `CFBundleURLTypes`
  + ponuka v Docku. Testy `AppLinkTests`, smoke `links` (Linux 6/6 vo WSL, 2026-11-16).
  *AK:* jump list ukazuje tri úlohy a každá funguje so spustenou aj nespustenou aplikáciou; po zapnutí protokolu
  otvorí `wolfmark://…` z prehliadača/Win+R správny deň, poznámku či vyhľadávanie; neplatný odkaz sa ohlási; vypnutie
  protokol odstráni.
- ✅ **M8-04 – Nastavenia** (SPEC §4.6) vrátane zmeny koreňového adresára s presunom.
  *Stav:* Hotové, šírka textu ručne overená na Windows (2026-11-16): priečinok (presun/prepnutie), téma, backdrop, jazyk, editor
  (písmo, veľkosť, čísla riadkov, farebné emotikony, **šírka textu**), Claude Code, rebuild indexu; v titulku verzia,
  Diagnostika s kopírovaním. Šírka textu (2026-11-16): Nastavenia → Editor → „Šírka textu“ = Celé okno (predvolené)
  / ~60 / ~80 / ~100 / ~120 znakov (`EditorMaxLineLength`, popisok „pre široké monitory“); počet znakov podľa priemernej šírky znaku zvoleného písma
  a veľkosti, širšie okno stĺpec textu (aj s číslami riadkov) vycentruje, posuvník ostáva pri okraji. V oboch
  aplikáciách (`MarkdownEditor.MaxLineLength`, Avalonia `MarkdownTextEditor.MaxLineLength`). Voľba embedding modelu
  presunutá do M2-05. Skratka rýchleho zápisu: M8-01.
  *AK:* pri ~80 znakoch má riadok bežného textu v širokom okne ~80 znakov a je vycentrovaný; Ctrl +/− šírku stĺpca
  prepočíta; užšie okno zalamuje na šírku okna; „Celé okno“ = správanie ako predtým; klik a výber v posunutom texte
  trafí správny znak; obrázky a tabuľky sa zmestia do stĺpca.
- ✅ **M8-05 – Premenovanie a zlúčenie tagov** (hromadné premenovanie súborov s rollbackom pri chybe).
  Nastavenia → Tagy: zoznam tagov (aj bez poznámok, z `tags.json`), pridanie nového; pri každom tagu vyskakovacie okno
  s farbou, titulkami v kalendári, „Zobraziť poznámky“, premenovaním (na existujúci tag = zlúčenie) a odstránením
  (poznámky stratia tag). Výber tagu v hlavičke ponúka farbu a titulky len pri vytváraní nového tagu.
  Mení sa len názov súboru (`praca_1403.md` → `robota_1403.md`), tag je len tam (ADR 0010). Poznámky sa hľadajú v súboroch,
  nie v indexe (`TagRenamer`); pri chybe sa už premenované vrátia späť. Premenovanie prenesie farbu a štítky v `tags.json`,
  pri zlúčení ostane farba cieľa. `#ClaudeCode` sa premenovať nedá (patrí synchronizácii).
  *AK:* po premenovaní majú všetky poznámky nový tag v názve, kalendár a lišta ukazujú nový tag s pôvodnou farbou; otvorená
  poznámka sa ďalej ukladá do premenovaného súboru; zlúčenie v rovnakej minúte nestratí ani jednu poznámku; chyba
  pri premenovaní vráti súbory späť. *Stav:* Hotové, otestované (`TagRenamerTests`) a overené na Windows (2026-11-13).
- ✅ **M8-06 – Lokalizácia** sk/en.
  *Stav:* `.resw` sk/en generované z jednej tabuľky (`tools/gen-strings.py`). Kontrola 2026-11-16: chýbal len
  „Bez tagu“ v legende; natvrdo písané texty v UI žiadne. Nastavenie „Dátumy a čas v jazyku aplikácie“ (inak podľa
  regiónu systému) v oboch aplikáciách – predtým Windows vždy región, Avalonia vždy jazyk (`UiCulture`); a
  „24-hodinový čas“ (predvolene zapnutý, prebije 12-hodinový formát jazyka aj regiónu).
- 🟨 **M8-07 – Indikátor OneDrive stavu** a UI pre konflikty.
  *Stav:* Čiastočne: konflikty v UI, ikona pre placeholdery; stav synchronizácie OneDrive nie.
- 🟨 **M8-08 – WolfMark ako predvolený editor `.md`** (Windows, macOS, Linux), ADR 0008.
  *Stav:* Implementované, čaká na overenie: súbor ako argument (aj presmerovaný bežiacej inštancii – jedna
  na dátový priečinok), súbor zo stromu prepne poznámku, mimo stromu vlastné okno (Uložiť, otázka pri
  zatvorení, zmena na disku, kópia do dnešných poznámok, stavový riadok), externé súbory v histórii,
  registrácia v Nastaveniach (Windows HKCU + Predvolené aplikácie, macOS Launch Services, Linux xdg-mime).
  Smoke test (`-Checks files`): súbor mimo poznámok = vlastné okno, druhé spustenie ho odovzdá bežiacej inštancii,
  ten istý súbor znova = to isté okno, poznámka zo stromu = hlavný editor – na Windows 5/5 (2026-11-16).
  Registrácia na Windows (HKCU + Predvolené aplikácie) ručne overená 2026-11-16. Linux: `tools/smoke-linux.sh`
  (Xvfb, rovnaké kontroly) 10/10 nad buildom vo WSL (2026-11-16), v CI nad AOT binárkou. **Zostáva:** macOS
  a registrácia na Linuxe (xdg-mime) – ručne.
- ✅ **M8-09 – Prenosný režim** (ADR 0009): značka `WolfMark.portable.json` vedľa exe, dáta v `WolfMarkData`
  (`local/`, `notes/` – relatívne cesty), zapnutie (Kopírovať/Presunúť) a vypnutie (kopírovať späť) v Nastaveniach
  s upozornením, integrácia so systémom v prenosnom režime vypnutá.
  *Akceptácia:* po zapnutí a reštarte sú poznámky, vyhľadávanie a nastavenia z prenosného priečinka; po presune
  priečinka (iné písmeno disku) aplikácia funguje; zmazaná poznámka ide do `.wolfmark/trash`; poškodená značka
  zastaví štart s hlásením; vypnutie vráti dáta do profilu; z Program Files je voľba nedostupná s dôvodom.
  *Stav:* Hotové, overené na Windows (vydané vo verzii 0.5.0).

## M9 – Rozšírenia 🟡/⚪

- 🟨 **M9-01 – 🟡 Úlohy:** pohľad „Nedokončené“ naprieč dňami, počty v kalendári.
  *Stav:* Čiastočne: filter „Nedokončené“, progres úloh v týždennej lište.
- ⬜ **M9-02 – 🟡 Denný pohľad** (všetky poznámky dňa ako jedna stránka).
- 🟨 **M2-05 – 🟡 Embedding provider** (presunuté z M2 – odložené 2026-11-13, ID ostáva kvôli odkazom v kóde a ADR 0003).
  Rozhranie `IEmbeddingProvider`; implementácia ONNX Runtime + multilingválny model; stiahnutie modelu pri prvom použití s overením hashu; chunking; dávkové počítanie v pozadí s nízkou prioritou.
  *Stav:* Čiastočne: `IEmbeddingProvider`, chunking, `EmbeddingIndexer` (nízka priorita, zmena modelu = prepočet). **ONNX provider + model chýba.**
  Patrí sem aj **voľba embedding modelu v Nastaveniach → Index** (SPEC §4.6, presunuté z M8-04) – má zmysel až s prvým modelom.
  *AK:* Indexovanie embeddingov nezvýši latenciu UI (merať); AOT build bez trim warningov. Ak ONNX pod AOT zlyhá, zdokumentovať a navrhnúť alternatívu.
  *Postup:* najprv spike – AOT publish s `Microsoft.ML.OnnxRuntime` (win-x64/arm64, linux-x64, osx-arm64), tokenizér bez reflexie, model (napr. `multilingual-e5-small`) → ADR.
- ⬜ **M9-03 – 🟡 Podobné poznámky** pod editorom (vektory). *Závisí od M2-05.*
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
- ✅ **M10-04 – README a screenshoty.** Anglické README; `--demo <dir>` (izolované demo dáta: poznámky, Claude Code
  transkripty, nastavenia aj index v jednom priečinku, `DemoDataSeeder`) a `--screenshots <dir>` (RenderTargetBitmap → PNG
  do `docs/screenshots`, potom koniec). *Stav:* README so screenshotmi v `docs/screenshots` (2026-11-05). Smoke test ich generuje znova pri každom CI behu
  (artefakt `smoke-win-x64`), na Windows overené 2026-11-16 (8 obrázkov) – aktualizácia = skopírovať ich do `docs/screenshots`.

## M11 – Stabilita a parita 🟢

Z review 2026-11-17 (čítanie kódu, tri prieskumy). Poradie: najprv strata dát, potom spoľahlivosť, parita, CI a dokumentácia.

- 🟨 **M11-01 – Autosave neprepíše zmenu zvonka.** Uloženie otvorenej poznámky je podmienené hashom obsahu, ktorý editor
  naposledy videl (`DirectoryEngine.TrySaveAsync`); pri nezhode alebo nezodpovedanom „Načítať z disku / Ponechať moje“
  sa neukladá. Odchod z poznámky (iná poznámka, koniec aplikácie) s nezodpovedanou zmenou uloží text editora ako novú
  poznámku vedľa pôvodnej – nestratí sa ani jedna verzia.
  *AK:* zmena súboru iným programom počas písania sa neprepíše ani pred hlásením watchera; „Ponechať moje“ prepíše
  a ukladanie pokračuje; „Načítať z disku“ ukáže verziu z disku; zmazaný súbor sa ďalším uložením vytvorí znova.
  *Stav:* Implementované, testy `ExternalChangeTests`, `DirectoryEngineTests.TrySave_*`; čaká na overenie na Windows.
- 🟨 **M11-02 – Kôš nikdy nemaže natrvalo.** Kôš Windows len na pevných lokálnych diskoch (`WindowsRecycleBin.CanRecycle`);
  na sieťových a vymeniteľných diskoch `.wolfmark/trash` v koreni (ako v prenosnom režime). `FOF_WANTNUKEWARNING`: ak by
  Windows položku predsa zničil (Kôš vypnutý, položka väčšia než Kôš), najprv sa opýta.
  *AK:* koreň na sieťovom disku alebo USB: zmazaná poznámka je v `.wolfmark/trash`; koreň na `C:`: v Koši.
  *Stav:* Implementované, **čaká na ručné overenie na Windows** (z WSL sa overiť nedá).
- 🟨 **M11-03 – Presun dát zastaví import a watcher.** `ClaudeCodeSyncScheduler.DisposeAsync` zastaví slučku a počká
  na bežiaci import; presun koreňa ide v oboch aplikáciách cez `AppHost.MoveNotesAsync` (zastavenie, kópia, overenie,
  starý priečinok do koša), chyba sa ohlási v Nastaveniach a po úspechu sa aplikácia hneď reštartuje (do starého
  priečinka už nesmie nič zapísať). Desktop predtým posielal starý koreň do koša vnútri neho samého.
  *Stav:* Implementované, testy `SyncSchedulerTests.Dispose_*`; presun v UI čaká na overenie na Windows a Linuxe.
- 🟨 **M11-04 – Zlyhaný štart sa ohlási.** Ak sa nedajú načítať tagy alebo otvoriť index, hlavné okno ukáže chybu
  (`AppHost.StartupError`, `Error_Startup`) namiesto prázdnych zoznamov bez slova.
  *Stav:* Implementované, test `DemoHostTests.A_failed_start_is_reported`.
- 🟨 **M11-05 – `tags.json` a `settings.json`.** Nečitateľný alebo poškodený `tags.json` sa pred prvou zmenou odloží ako
  `tags.broken-<čas>.json` (`BrokenFile.KeepCopy`); zápisy `TagStore` idú po jednom a stav berú až vo svojom ťahu.
  Poškodený `settings.json` sa odloží ako `settings.broken-<čas>.json` a hlavné okno to ohlási (`Error_SettingsInvalid`)
  – platia predvolené hodnoty vrátane priečinka poznámok.
  *Stav:* Implementované, testy `TagStoreTests` (kópia, súbežné zmeny), `SettingsTests`; hlásenie v UI čaká na overenie.
- 🟨 **M11-06 – Premenovanie tagu a súborov.** Konfliktná kópia ostáva konfliktnou (`NoteName.WithTag` aj kolízne mená
  nesú značku); rollback vracia presné pôvodné mená (`DirectoryEngine.RenameAsync`); premenovanie líšiace sa len
  veľkosťou písmen na Linuxe už nenechá `.rename.tmp`; autosave čaká, kým editor premenuje svoj súbor.
  *Stav:* Implementované, testy `NoteNameTests`, `TagRenamerTests`.
- 🟨 **M11-07 – `NotesMover`:** kópia sa zapíše na disk (flush) a overuje sa obsahom, nie len veľkosťou.
  *Stav:* Implementované; existujúce testy presunu prechádzajú, nový test na poškodenú kópiu nie je.
- 🟨 **M11-08 – Index.** Po nedokončenej obnove sa writer otvorí znova pri ďalšom zápise (`ReopenWriter`);
  `ApplyAsync` čaká na bežiaci sken (starší text už neprepíše novší); súbor zmazaný počas skenu sa neindexuje;
  „nezmenený“ súbor bez riadku v DB sa pri ďalšom skene prečíta. `IOERR`/`READONLY` ostávajú dôvodom na obnovu
  zámerne – index je cache a zmazaný súbor DB sa inak neopraví.
  *Stav:* Implementované; bez nových testov (súbehy sa v teste nedajú spoľahlivo vyvolať).
- 🟨 **M11-09 – Watcher.** Presun poznámky do skrytého priečinka = zmazanie; A→B→C je jedno premenovanie A→C; zmazaný
  priečinok s bodkou v názve sa preskenuje; po chybe sa watcher vytvorí znova (aj opakovane, kým je priečinok preč);
  návrat súboru na skôr uložený obsah je zmena. Rast `SelfWriteRegistry` neriešený – je ohraničený počtom poznámok.
  *Stav:* Implementované, testy `NoteWatcherTests` (5 nových); odpojenie sieťového disku čaká na overenie na Windows.
- 🟨 **M11-10 – Claude Code sync.** Hotové: vylúčený projekt platí pre priečinok a jeho obsah, nie pre susedov
  s rovnakým začiatkom; zalomenie riadka v `session`/`branch` nepridá kľúče do front matter (testy).
  **Otvorené:** kurzor namiesto parsovania celého transkriptu (SPEC §4.7) – obnovené relácie kopírujú staršie správy
  do nového súboru, takže poradie v súbore nie je spoľahlivé; treba reálne vzorky a ADR. **Rozhodnuté 2026-11-17:** zahodená poznámka
  Claude Code sa pri ďalšom importe vráti – je to synchronizácia (záloha promptov), nie vlastná poznámka; vlastná
  verzia vzniká kópiou („Kopírovať do vlastnej poznámky“). Zmenu tagu týchto poznámok obe aplikácie nedovolia.
- 🟨 **M11-11 – Úniky a UI vlákno.** Desktop: `Palette.Changed` len kým je prvok na obrazovke / okno otvorené
  (`Ui.OnPaletteChanged`), zoznam písiem sa plní až po zobrazení Nastavení (stále na UI vlákne – `FontManager`
  mimo neho neoverený), `OnDrop` s ošetrením chyby. App: externé okno pri zatvorení ukončí kontrolu pravopisu
  (`MarkdownEditor.ReleaseSpelling`). `Debouncer` chyby loguje. Otvorené: `async void` lambda v `MainViewModel`.
  *Stav:* Implementované, len skompilované – čaká na overenie na Windows a Linuxe.
- ⬜ **M11-12 – Spoločná logika do `WolfMark.Presentation`:** nastavenia a kontrola reštartu, prenosný režim, rýchla
  poznámka, `OpenLinkAsync`, texty stavu uloženia, `SavePlacement`, `crash.txt` (~1 000 riadkov je v oboch aplikáciách).
- ⬜ **M11-13 – Parita Desktopu:** nastavenia Claude Code (vylúčené projekty, sledovanie, interval), Index a diagnostika,
  farebné emotikony, výber ikony, placeholder editora, kopírovanie rozšírené na Markdown, klávesnica v kalendári,
  čip filtra a synchronizácia „Nedokončené“, štart po prihlásení, `--screenshots`, `AutomationProperties`,
  ošetrenie chýb pri otvorení Nastavení a rýchlej poznámky. Kontrola pravopisu ostáva v ADR 0005.
- ⬜ **M11-14 – CI a release:** testy a smoke pred vydaním v `release.yml`, benchmark projekt (BenchmarkDotNet),
  `.editorconfig` a `.gitattributes`, verzia na jednom mieste, `THIRD-PARTY-NOTICES`, test parity kľúčov reťazcov.
- ⬜ **M11-15 – Dokumentácia:** README (roadmap, tabuľka funkcií, sémantické hľadanie, ktoré ešte nebeží), SPEC
  (Avalonia, zrušený ročný pohľad a backdrop, rozhodnuté otázky §9), BACKLOG M2-02 vs. ADR 0010, výnimka lexera
  na UI vlákne v `CLAUDE.md` (ADR 0007).

Návrhy funkcií z review (mimo existujúcich M9): hľadanie a nahradenie v poznámke (Ctrl+F/H), obnova z koša v UI,
záloha/verzie poznámok (SPEC §7), osnova dlhej poznámky, pripnuté poznámky.
