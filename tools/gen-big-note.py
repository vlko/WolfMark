#!/usr/bin/env python3
"""Writes a large, realistic markdown note for measuring the editor (docs/adr/0001-editor.md, --trace-editor).

Usage: python3 tools/gen-big-note.py <notes-root> [lines]
The note goes to <notes-root>/YYYY/MM/DD/0500.md (today), e.g. the demo notes: <demo>/notes.
"""
import datetime
import pathlib
import random
import sys

WORDS = ("poznámka stretnutie projekt úloha nasadenie test výkon editor riadok značka index hľadanie "
         "release build commit review návrh dátum kalendár súbor obrázok odkaz zoznam kód").split()


def sentence(rng: random.Random, n: int) -> str:
    words = [rng.choice(WORDS) for _ in range(n)]
    for i in rng.sample(range(n), k=min(2, n)):
        words[i] = rng.choice(["**{}**", "*{}*", "`{}`", "~~{}~~", "[{}](https://example.com)"]).format(words[i])
    return " ".join(words).capitalize() + "."


def generate(lines: int) -> list[str]:
    rng = random.Random(42)
    out: list[str] = []
    section = 0
    while len(out) < lines:
        section += 1
        out += [f"## Sekcia {section}", ""]
        kind = section % 5
        if kind == 0:
            out += ["```csharp"] + [f"var x{i} = Compute({i}); // {rng.choice(WORDS)}" for i in range(8)] + ["```", ""]
        elif kind == 1:
            out += [f"- [{'x' if rng.random() < 0.4 else ' '}] {sentence(rng, 6)}" for _ in range(10)] + [""]
        elif kind == 2:
            out += [f"{i + 1}. {sentence(rng, 8)}" for i in range(6)] + [""]
        elif kind == 3:
            out += ["> " + sentence(rng, 14), ""]
            out += [" ".join(sentence(rng, 12) for _ in range(6)), ""]  # long line that wraps
        else:
            out += [f"### Podsekcia {section}", ""] + [sentence(rng, 10) for _ in range(5)] + [""]
    return out[:lines]


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    root = pathlib.Path(sys.argv[1])
    lines = int(sys.argv[2]) if len(sys.argv) > 2 else 5000
    today = datetime.date.today()
    target = root / f"{today:%Y}" / f"{today:%m}" / f"{today:%d}" / "0500.md"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text("\n".join(generate(lines)) + "\n", encoding="utf-8")
    print(f"{target} ({lines} lines)")


if __name__ == "__main__":
    main()
