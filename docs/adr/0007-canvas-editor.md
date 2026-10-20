# ADR 0007 – Vlastný editor nad Win2D (prototyp)

- Stav: prototyp na vetve `canvas-editor`, rozhodnutie po meraní
- Súvisí: ADR 0001 (RichEdit), ADR 0006 (veľkosť buildu)

## Kontext
Po optimalizáciách (ADR 0001) je RichEdit na bežných poznámkach plynulý, ale jeho náklady rastú s veľkosťou
celého dokumentu: na 5 000 riadkoch (255 k znakov) stojí Enter ~140 ms (`KeyToText`), layout po zmene formátu
~128 ms a po písaní prichádzajú pauzy 300–330 ms bez našej práce; na 1 000 riadkoch je to ~4× menej.
Skryté značky, obrázky v texte a čipy sú nad RichEditom obchádzky (RTF, overlay, `GetRect`).

Zvažované: WinUIEdit/Scintilla (jedna výška riadku pre celý dokument, bez obrázkov – nevyhovuje formátovanému
režimu), WinUIRichEditor (Win2D, rich text editor s vlastným formátom, nie Markdown – vhodný ako predloha),
CodeMirror 6 vo WebView2 (najviac hotového, ale proti natívnemu zámeru).

## Rozhodnutie (prototyp)
Vlastný editor `WolfMark.Editor.Win2D.CanvasMarkdownEditor`, zapnutý prepínačom `--canvas-editor`; RichEdit
ostáva predvolený. Oba spĺňajú `IMarkdownEditor`, zvyšok aplikácie sa nemení.

- **Text a tokeny v jednom modeli**: `MarkdownDocumentModel` (Core) drží riadky; `Replace(start, length, text)`
  prestavia len dotknuté riadky a retokenizuje po konvergenciu stavu. Úpravy idú priamo na UI vlákne – trvajú
  zlomky ms, bez lexera na pozadí a zahadzovania zastaraných dávok.
- **Zobrazenie riadku**: `LinePresentation` (Core) vynechá vo formátovanom režime značky (`Syntax`, `LinkUrl`)
  a mapuje stĺpce zdroj ↔ zobrazenie. Kurzor medzi zobrazenými znakmi stojí za skrytými znakmi pred ním
  (na začiatku „**tučné**“ vnútri, na konci za „**“). Šípky a mazanie idú po zobrazených znakoch; Backspace na
  začiatku nadpisu/zoznamu zmaže skrytú značku (riadok sa stane odsekom).
- **Layout**: každý riadok má vlastný `CanvasTextLayout` (DirectWrite) so štýlmi tokenov; layouty sa držia len
  pre nedávno kreslené riadky (najviac 400). Pri otvorení, zmene režimu, šírky či témy sa hneď zmerajú riadky
  okolo zobrazenia a kurzora, ostatné dostanú odhad podľa dĺžky a presne sa zmerajú paralelne na pozadí
  (bez farieb; výsledok sa priradí podľa inštancie textu riadku, novšie prerozloženie ho zahodí). Prvý
  viditeľný riadok ostáva na mieste (aj pri prepnutí režimu).
- **Kreslenie**: `CanvasVirtualControl` v `ScrollViewer`; kreslia sa len riadky v invalidovaných oblastiach.
  Session v pixeloch so škálou v transformácii (celopixelový pôvod – poznatok z WinUIRichEditor).
- **IME**: `CoreTextEditContext`, buffer = riadok s kurzorom v zdrojových stĺpcoch, podčiarknutie kompozície,
  okno kandidátov pri kurzore (`ClientToScreen`).
- **Vstup**: `KeyDown` (navigácia, mazanie, Enter s pokračovaním zoznamu, skratky), `CharacterReceived`
  (text vrátane mŕtvych kláves), AltGr cez `ToUnicodeEx` (CsWin32), myš (klik, ťahanie, Shift+klik, dvojklik
  na slovo), schránka ako plain text, undo cez `UndoHistory.RecordEdit`.
- **Pravopis**: Windows Spell Checking API (`ISpellChecker`, COM cez CsWin32) na vlastnom MTA vlákne. Kontroluje
  sa len próza riadku (`MarkdownSpelling.ProseOnly` – kód, URL, tagy a značky sú nahradené medzerami, stĺpce
  ostávajú), výsledok sa pamätá podľa tohto textu, takže dĺžka poznámky nehrá rolu (na rozdiel od RichEditu
  netreba limit pre dlhé poznámky). Viac jazykov naraz (jazyk klávesnice, UI, sk-SK, en-US): slovo je chybné,
  len ak ho označia všetky. Rozpísané slovo sa neoznačí, kým z neho kurzor neodíde. Kontextové menu: návrhy,
  Pridať do slovníka, Ignorovať, Vystrihnúť/Kopírovať/Prilepiť/Vybrať všetko.
- **Čísla riadkov a čipy**: kreslia sa v tom istom prechode ako text z layoutov riadkov (číslo na účiarí prvého
  riadku odseku, `↪` na zalomených riadkoch z `LineMetrics`, podklad stĺpca); čipy skrytých značiek riadku s
  kurzorom v horných rohoch ako v RichEdit editore – bez dopytov na polohu (`GetRect`).
- **Alokácie**: zobrazený text riadku, maska pre pravopis a úpravy modelu pracujú so `Span` (`string.Create`,
  `stackalloc`/`ArrayPool`); polia tokenov sa nekopírujú (model ich po vytvorení nemení).
- **Meranie**: `--trace-editor` – `KeyToFrame`, `CanvasEdit`, `CanvasDraw`, `CanvasMeasure`.

## Zatiaľ chýba (pred prípadným prechodom)
Obrázky v texte, formátovacie príkazy v kontextovom menu, prístupnosť (UIA text pattern),
vkladanie obrázkov, drag & drop, automatický posun pri ťahaní.

## Merania (Windows, 5 000 riadkov / 255 k znakov, prvý prototyp)

| | RichEdit | Win2D |
|---|---|---|
| Kláves → snímka p50 / p95 / max | 20 / 144 / 172 ms | 3,2 / 17 / 18 ms |
| Úprava (model + layout) / prekreslenie | – | 0,4 ms / 0,6 ms |
| Otvorenie / prepnutie režimu (1. verzia, všetko na UI vlákne) | ~2 s / 0,74 s | 0,34 s / 0,28–0,33 s |
| Otvorenie / prepnutie režimu (meranie na pozadí) | | ~0,09 s / ~7 ms na UI vlákne, pozadie 52–93 ms |

Písanie s diakritikou, AltGr a emoji panel (IME) overené na Windows – znaky sa nezdvojujú.

Veľkosť (win-x64 Native AOT publish s Win2D, bez `.pdb`): 75,7 MB rozbalené, **28,6 MB zip**
(v0.2.1: 52,8 MB). Win2D pridáva `Microsoft.Graphics.Canvas.dll` 1,65 MB (0,59 MB v zipe); `WolfMark.exe`
14,3 MB. Varovania AOT publishu z výstupu nevidno – overiť v logu buildu (CI).

## Kritériá rozhodnutia
Na 5 000-riadkovej poznámke (`tools/gen-big-note.py`): kláves → snímka p95 ≤ 1–2 snímky aj pri Enter, otvorenie
a prepnutie režimu do ~0,5 s, Native AOT build bez varovaní, prírastok veľkosti zipu (Win2D) zapísaný.
