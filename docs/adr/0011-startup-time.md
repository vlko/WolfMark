# ADR 0011 – Čas štartu: cieľ < 600 ms a čo ho určuje

- Stav: prijaté (2026-11-16)
- Backlog: M4-09

## Kontext
SPEC mala cieľ „studený štart do editovateľného editora < 300 ms (AOT)“. Meranie začalo až v novembri 2026:
obe aplikácie logujú `Ready to type <n> ms after the process started` (od vytvorenia procesu OS, nie od `Main`),
Windows aplikácia aj fázy (`Startup (ms since the process started)`) a značky po krokoch (`Startup marks`,
`WolfMark.Core.Diagnostics.StartupTimeline`). `tools/smoke-windows.ps1 -Checks startup` berie medián troch teplých
štartov na demo dátach (demo otvára dnešnú pracovnú poznámku ako používateľ svoju poslednú);
`-StartupExperiment <meno>` strieda tri štarty s `WOLFMARK_EXPERIMENT=<meno>` a tri bez – obe varianty vidia ten
istý stav stroja, rozdiely medzi behmi v inom čase sú inak väčšie než skúmané zmeny.

## Merania (AOT, win-x64)
- **Režim napájania rozhoduje:** úsporný 1,2–1,6 s, vyvážený ~590 ms pred optimalizáciou. Čísla nižšie sú
  z vyváženého režimu; rozptyl medzi štartmi ±40 ms.
- Po optimalizácii **~545–565 ms**. Rozpis (ms):
  štart XAML/Windows App SDK ~55, `XamlControlsResources` 5, dáta a index 5, editor (bez rozloženia) ~25,
  konštruktor `Window` ~50, `ExtendsContentIntoTitleBar` ~180 (tu WinUI prvýkrát inicializuje Direct3D), zvyšok okna
  ~30, téma ~45 (Win2D zariadenie editora), `Activate` ~62, prvá snímka ~83, otvorenie poznámky ~8.
- **Réžia WinUI 3 bez nášho kódu je ~360 ms**, takže 300 ms nie je dosiahnuteľných.

## Rozhodnutie
- Cieľ M4-09 a SPEC: **< 600 ms vo vyváženom režime napájania** (AOT, teplý štart). Smoke test má túto hranicu
  predvolene; v CI 3 000 ms (runner bez GPU) len ako poistka proti regresii.
- **Editor nerozkladá dokument v konštruktore** (−30 ms): kto sa prvý dotkne Direct3D, platí ~100 ms za jeho
  inicializáciu – teraz WinUI pri titulkovom pruhu; Win2D zariadenie potom stojí ~35 ms namiesto ~119 ms.
- **Fokus a „ready“ hneď po otvorení poznámky**, týždeň, história, kalendár a počty sa dočítajú potom (obe aplikácie).
- **Vyhľadávacia paleta sa stavia pri prvom otvorení.**
- **Zamietnuté:** zahriatie Win2D na pozadí z `Main` (zariadenie + DirectWrite) – −35 ms v editore, +22 ms
  v titulkovom pruhu a `Activate` (súbeh), netto −17 ms = šum, za cenu ďalšieho vlákna.

## Dôsledky
Ďalšie zrýchlenie by vyžadovalo zásah do toho, čo robí WinUI (napr. bez rozšírenia obsahu do titulkového pruhu),
alebo by bolo pod úrovňou šumu. Regresie ukáže smoke test (fázy a značky sú v logu každého štartu).
