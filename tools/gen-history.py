#!/usr/bin/env python3
"""Writes many small notes over past days for testing the history list (BACKLOG M5-01: smooth scroll over 10,000 items).

Usage: python3 tools/gen-history.py <notes-root> [count]
Notes go to <notes-root>/YYYY/MM/DD/[tag_]HHmm.md, about nine a day back from yesterday; existing files are kept.
Use an own demo folder: start `WolfMark.exe --demo <dir>` once (it seeds <dir>/notes), close it, then run this on
<dir>/notes – the index picks the new files up on the next start.
"""
import datetime
import os
import pathlib
import random
import sys

TAGS = ["praca", "praca", "projekt-x", "osobne", "citanie", "nakupy", "zdravie", None, None]
SUBJECTS = ("stretnutie s tímom, release 0.4, review pull requestu, plánovanie šprintu, výkon editora, nákupný zoznam, "
            "kniha o návrhu softvéru, tréning, nápad na víkend, telefonát s klientom, oprava buildu, zálohy fotiek").split(", ")
WORDS = ("poznámka úloha termín dohoda otázka riešenie chyba test dáta index kalendár zoznam odkaz obrázok "
         "rýchlo neskôr zajtra dôležité overiť dokončiť poslať pripraviť").split()


def body(rng: random.Random) -> str:
    title = rng.choice(SUBJECTS).capitalize()
    lines = [f"# {title}", ""]
    for _ in range(rng.randint(1, 4)):
        lines.append(" ".join(rng.choice(WORDS) for _ in range(rng.randint(6, 18))).capitalize() + ".")
    if rng.random() < 0.4:
        lines += [""] + [f"- [{'x' if rng.random() < 0.5 else ' '}] {rng.choice(WORDS)} {rng.choice(WORDS)}" for _ in range(rng.randint(2, 5))]
    return "\n".join(lines) + "\n"


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    root = pathlib.Path(sys.argv[1])
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 10_000
    rng = random.Random(7)
    day = datetime.date.today() - datetime.timedelta(days=1)
    written = 0
    while written < count:
        minutes = sorted(rng.sample(range(7 * 60, 22 * 60), k=min(count - written, rng.randint(5, 13))))
        folder = root / f"{day.year:04}" / f"{day.month:02}" / f"{day.day:02}"
        folder.mkdir(parents=True, exist_ok=True)
        for m in minutes:
            tag = rng.choice(TAGS)
            name = f"{tag + '_' if tag else ''}{m // 60:02}{m % 60:02}.md"
            path = folder / name
            if path.exists():
                continue
            path.write_text(body(rng), encoding="utf-8", newline="\n")
            edited = datetime.datetime.combine(day, datetime.time(m // 60, m % 60)) + datetime.timedelta(minutes=rng.randint(1, 50))
            os.utime(path, (edited.timestamp(), edited.timestamp()))
            written += 1
        day -= datetime.timedelta(days=1)
    print(f"{written} notes, back to {day + datetime.timedelta(days=1)}")


if __name__ == "__main__":
    main()
