# ADR 0001 – Markdown editor: RichEditBox + background incremental lexer

- Stav: prijaté (v1), meranie na Windows otvorené
- Backlog: M4-01 … M4-06

## Kontext

SPEC §4.4 požaduje dva režimy (raw / formátovaný) nad jedným textovým modelom, latenciu písania
< 16 ms aj pri 5 000 riadkoch a zvýrazňovanie, ktoré nikdy neblokuje písanie. Kandidáti:
`RichEditBox` (RichEdit/TOM) s vlastným farbením, alebo vlastný control nad Win2D/DirectWrite.

## Rozhodnutie

**`RichEditBox` + vlastný inkrementálny lexer na pozadí** (`WolfMark.Editor.MarkdownEditor`).

- Text je jediný model (`GetText(UseLf)`), formátovanie je len prezentácia – round-trip je bezstratový
  aj vo formátovanom režime (značky sú `Hidden`, nie odstránené).
- `MarkdownDocumentModel` (Core) drží stav lexera na začiatku každého riadku; po úprave prepočíta len
  od prvého zmeneného riadku po konvergenciu stavu (bloky kódu, front matter). Beží na pozadí,
  na UI vlákno idú hotové rozsahy po dávkach 150 riadkov s nízkou prioritou; zastaraná verzia sa zahodí.
- **Natívne undo RichEditu je vypnuté** (`UndoLimit = 0`): zmeny formátu by sa inak miešali do histórie.
  `UndoHistory` (Core) ukladá textové diffy a zlučuje súvislé písanie.
- Editovacie skratky sú čisté textové funkcie (`MarkdownEditing`) vracajúce `TextEdit`, ktorý sa
  aplikuje na podrozsah (zachová kurzor/scroll) – všetko pokryté unit testami.
- Vkladanie: vždy plain text; obrázok zo schránky → PNG → `assets/` dňa → `![](assets/HHmm-NN.png)`.
- `Ctrl+K` s označeným textom vytvorí odkaz, bez výberu otvorí vyhľadávanie (SPEC má Ctrl+K pre oboje).
- **Formátovaný režim neodkrýva značky ani na riadku s kurzorom** (odkrytie posúvalo text). Namiesto
  toho: tlačidlá v toolbare (H1–H3, B, I, S, kód, odkaz, úloha) ukazujú formát na pozícii kurzora
  a stlačenie aktívneho formát odstráni (`MarkdownEditing.Unwrap`); značky riadku s kurzorom sa kreslia
  ako malé čipy v overlay `Canvas` v horných rohoch pred a za textom (`ITextRange.GetRect`).
- Kontextové menu (pravý klik) dopĺňa štandardný `TextCommandBarFlyout` o zmenu typu riadku
  (`MarkdownEditing.SetBlock`: odsek, H1–H3, citácia, zoznamy, úloha) a inline formáty – kopírovanie
  a návrhy pravopisu ostávajú.
- Kontrola pravopisu sa zapína/vypína tlačidlom v toolbare (`LocalSettings.SpellCheckEnabled`).
- Zvýrazňovanie: riadky odoslané na UI, ktoré UI ešte neaplikovalo (zastaraná dávka sa zahodí), sa
  prenášajú cez `MarkdownUpdate.MapLine` do ďalšej dávky – rýchle písanie po vložení viacerých
  riadkov nenechá riadky bez farbenia.
- **Otvorenie poznámky je jeden `SetText` s RTF** (`RtfDocument`): celý naformátovaný text naraz namiesto
  tisícov volaní formátu po rozsahoch. Rovnaké pravidlá tokenov (`StyleTokens`) píšu buď do rozsahu RichEditu,
  alebo do RTF; veľkosti sa zaokrúhľujú na pol bodu (RTF `\fs`) v oboch cestách. Text sa po načítaní porovná
  s modelom – pri rozdiele sa vráti plain text a formátuje sa po dávkach. Kontrola pravopisu sa zapína nad
  prázdnym dokumentom tesne pred jediným `SetText` s RTF (kontrola vidí len finálny text, dlhá poznámka sa
  nastavuje raz); text sa vymieňa znova len pri rozdiele, a to s vypnutou kontrolou – výmena textu počas
  behu kontroly padala vo WinUIEdit.dll.
- **Prepnutie režimu a témy** ide rovnakou cestou ako otvorenie (jeden `SetText` s RTF). Formát po rozsahoch
  v dávkach po 150 riadkoch trval na 5 000-riadkovej poznámke 8–15 s na dávku.
- **Farbenie riadku po úprave**: formát každého znaku sa vypočíta ako pre RTF a na RichEdit sa nastaví raz
  na úsek rovnakých znakov (klon `ITextCharacterFormat` → `range.CharacterFormat`), nie vlastnosť po
  vlastnosti – každé nastavenie vlastnosti je zmena dokumentu (~136 ms na jeden riadok v dlhej poznámke).
- **`TextChanged` len pri zmene obsahu**: RichEdit ho vyvoláva aj pre vlastné zmeny formátu (~20× na dávku
  farbenia); celý text sa číta len keď `TextChanging` hlási `IsContentChanging` alebo sa zmenila dĺžka
  dokumentu (poistka).
- **Meranie** (`--trace-editor`, `EditorTrace` v Core): časy fáz editora a pomalé volania (≥ 50 ms) s kontextom
  do `logs/editor-trace.txt`; testovacia poznámka `tools/gen-big-note.py`.

## Merania

| Meranie | Výsledok | Prostredie |
|---|---|---|
| Úprava 1 riadku v 5 000-riadkovom dokumente (split + diff + retokenizácia) | < 50 ms limit v teste, typicky jednotky ms | Linux, .NET 11 RC2, Debug (`MarkdownDocumentModelTests.Tokenizing_is_fast`) |
| Retokenizované riadky pri úprave 1 riadku | presne 1 | unit test |
| Písanie, 5 000 riadkov, kláves → snímka (pred opravou `TextChanged`) | p50 17,8 ms, p95 85 ms, p99 158 ms | Windows, v0.2.2 AOT |
| Čítanie celého textu na `TextChanged` (pred opravou) | 2 742 volaní na 110 skutočných zmien, Ø 1,2 ms, max 10 ms | dtto |
| Otvorenie 5 000-riadkovej poznámky (`LoadFormatted`, pred opravou: 2× `SetText`) | 1 376 ms | dtto |
| Dávka farbenia | p50 5,7 ms, max 9,5 s (pravdepodobne celé prefarbenie / prvý layout) | dtto |
| Prepnutie do raw, 5 000 riadkov (pred opravou: dávky po 150 riadkoch) | 8–15 s na dávku (minúty spolu) | dtto |
| Farbenie 1 riadku pri písaní (pred opravou: vlastnosť po vlastnosti) | ~136 ms → kláves → snímka ~145 ms | dtto |
| Čísla riadkov / čipy značiek | 2,9 ms (~31 `GetRect`) / 0,26 ms | dtto |

## Dôsledky / riziká

- Ak sa ukáže, že `ITextRange.CharacterFormat` je pri veľkých dokumentoch pomalé alebo `Hidden` text
  robí problémy s kurzorom, ďalší krok je vlastný control nad Win2D; lexer, model, undo a editovacie
  príkazy sú v Core a prenesú sa bez zmeny.
- Inline obrázky a tabuľky vo formátovanom režime zatiaľ nie sú (M4-04 čiastočne).
