#!/usr/bin/env python3
"""Writes a note full of Markdown tables for trying the table grid (BACKLOG M4-04).

Usage: python3 tools/gen-table-note.py <notes-root> [copies]
The note goes to <notes-root>/YYYY/MM/DD/tabulky_0600.md (today), e.g. the demo notes: <demo>/notes.
[copies] repeats the "many tables" section to try scrolling a long note with tables (default 1).
"""
import datetime
import pathlib
import sys

CASES = """# Tabuľky – test

Odsek pred tabuľkou. Šípkou dole vojdi do tabuľky – ukáže sa jej zdroj, šípkou ďalej von – znova mriežka.

## Zarovnanie stĺpcov

| Vľavo | Na stred | Vpravo | Predvolené |
|:------|:--------:|-------:|------------|
| a | b | 1 | x |
| dlhší text | stred | 1 234,50 | y |
| ľščťžýáíé | ÚÄÔ | 99 | z |

## Formátovanie v bunkách

| Formát | Ukážka |
|---|---|
| Tučné | **dôležité** |
| Kurzíva | *poznámka* |
| Prečiarknuté | ~~zrušené~~ |
| Kód | `MarkdownTable.Find` |
| Odkaz | [WolfMark](https://github.com/vlko/WolfMark) |
| Tag | #praca a #projekt-x |
| Emoji | ✅ 🚀 🐺 |
| Zmes | **tučné** a `kód` a *kurzíva* |

## Široká tabuľka (zúž okno – bunky sa zalomia)

| Krátky | Stredne dlhý stĺpec | Veľmi dlhý stĺpec |
|---|---|---|
| 1 | Toto je stredne dlhý text | Toto je veľmi dlhý text, ktorý by sa mal pri užšom okne zalomiť do viacerých riadkov v jednej bunke a riadok tabuľky má potom byť vyšší. |
| 2 | Ďalší | Krátky |

## Bez krajných zvislých čiar

Meno | Rola | Od
--- | --- | ---
Anna | vývoj | 2024
Boris | test | 2025

## Chýbajúce a nadbytočné bunky

| A | B | C |
|---|---|---|
| len jedna |
| dve | bunky |
| štyri | bunky | v | riadku |

## Prázdne bunky a escapovaná čiara

| Príkaz | Popis |
|---|---|
| `a \\| b` | čiara v kóde cez \\| |
|  | prázdna prvá bunka |
| prázdna druhá |  |

## Jeden stĺpec

| Úlohy na dnes |
|---|
| Ranná káva |
| Review |

## Hneď za odsekom (bez prázdneho riadku)
Text tesne nad tabuľkou.
| X | Y |
|---|---|
| 1 | 2 |
Riadok bez čiary tabuľku ukončí – toto už je odsek.

## Dve tabuľky za sebou

| Prvá | tabuľka |
|---|---|
| 1 | 2 |

| Druhá | tabuľka |
|---|---|
| 3 | 4 |

## Toto NIE sú tabuľky

Kód (zostane kódom):

```
| A | B |
|---|---|
| 1 | 2 |
```

Odsek so zvislou čiarou a | v texte, bez oddeľovacieho riadku.

| Iný počet stĺpcov | v hlavičke |
|---|---|---|

Nadpis podčiarknutý pomlčkami (nie oddeľovací riadok, chýba čiara)
---

## Úlohy a obrázok okolo tabuľky

- [ ] úloha nad tabuľkou
- [x] hotová úloha

| Stav | Počet |
|---|--:|
| otvorené | 3 |
| hotové | 12 |

- [ ] úloha pod tabuľkou
"""

MANY = """
## Veľa tabuliek ({n})

| # | Položka | Hodnota |
|--:|---|--:|
| {n}.1 | jablká | 1,20 |
| {n}.2 | **hrušky** | 2,40 |
| {n}.3 | `slivky` | 0,90 |

Text medzi tabuľkami {n}.
"""


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    root = pathlib.Path(sys.argv[1])
    copies = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    today = datetime.date.today()
    folder = root / f"{today.year:04}" / f"{today.month:02}" / f"{today.day:02}"
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / "tabulky_0600.md"
    text = CASES + "".join(MANY.format(n=n) for n in range(1, copies + 1)) + "\nPosledný riadok poznámky.\n\n| Tabuľka | na konci |\n|---|---|\n| posledná | bunka |"
    path.write_text(text, encoding="utf-8", newline="\n")
    print(path)


if __name__ == "__main__":
    main()
