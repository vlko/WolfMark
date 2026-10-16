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

## Merania

| Meranie | Výsledok | Prostredie |
|---|---|---|
| Úprava 1 riadku v 5 000-riadkovom dokumente (split + diff + retokenizácia) | < 50 ms limit v teste, typicky jednotky ms | Linux, .NET 11 RC2, Debug (`MarkdownDocumentModelTests.Tokenizing_is_fast`) |
| Retokenizované riadky pri úprave 1 riadku | presne 1 | unit test |
| Latencia písania v RichEditBox, 5 000 riadkov | **nezmerané** – vyžaduje Windows | TODO |

## Dôsledky / riziká

- Ak sa ukáže, že `ITextRange.CharacterFormat` je pri veľkých dokumentoch pomalé alebo `Hidden` text
  robí problémy s kurzorom, ďalší krok je vlastný control nad Win2D; lexer, model, undo a editovacie
  príkazy sú v Core a prenesú sa bez zmeny.
- Inline obrázky a tabuľky vo formátovanom režime zatiaľ nie sú (M4-04 čiastočne).
