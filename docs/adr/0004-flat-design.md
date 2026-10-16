# ADR 0004 – Flat design, Barlow typography, calendar panel and tag rail

- Stav: prijaté
- Backlog: M6-01 … M6-06

## Pravidlá

1. **Flat.** Jedna papierová plocha pre celé okno (bez kariet, tieňov a zaoblených plôch). Rohy majú
   len okno (OS) a vyhľadávacia paleta. Bunky kalendára sú štvorcové a na seba nadväzujú.
2. **Tlačidlá a tagy** sú len mierne zaoblené (`Radius.Control` = 3 px, `Radius.Tag` = 4 px); globálne
   `ControlCornerRadius`/`OverlayCornerRadius` sú prepísané v `App`, takže sa tomu prispôsobia aj
   štandardné WinUI ovládacie prvky.
   **Hover/stlačenie:** výplň sa nemení. Stav ukazuje jemný vnútorný „glow“ (2 px poloprie-
   hľadný okraj, v pokoji priehľadný, pri stlačení plný) a zmena farby textu – vždy v jednej z troch
   farieb: čierna, biela, akcentová modrá. Neutrálne tlačidlá: modrá. Farebné tlačidlá (tagy, + NOVÁ,
   vzorky farieb): glow aj text dostanú tú z troch farieb, ktorá nie je farbou textu v pokoji a má
   najvyšší kontrast (WCAG) k výplni (`Ui.HoverColor`) – napr. + NOVÁ a ClaudeCode čierna. Tlačidlá v toolbaroch majú jednu výšku (`Metrics.ButtonHeight`), RAW/FORMÁT
   pevnú šírku.
   Každé tlačidlo ide cez `Ui.StyleButton` (WinUI lightweight styling); texty a ikony dedia farbu tlačidla.
   Ak sa výplň mení (vybraný riadok histórie), tlačidlo má vlastný štetec a mení sa jeho farba.
3. **Farby** (`Theming/Palette.cs`): výraznejšia paleta tagov (`TagPalette`), modrý akcent, ružová a žltá
   vrstva pre „riso“ hlavné čísla, dvojfarebný čiarkovaný rám dneška (žltá pod čiernou).
4. **Typografia** (`Theming/Typo.cs`, fonty v `Assets/Fonts`, SIL OFL):
   - Barlow Condensed Light – hlavné čísla (dátum, časy v ľavom stĺpci editora a histórie),
   - Barlow Condensed Medium/SemiBold – hlavičky a popisky („OKTÓBER 2026“, „STREDA“, „HISTÓRIA ———“),
   - Barlow ExtraBold – čísla dní v kalendári,
   - text poznámok ostáva Segoe UI Variable.
   Každá rodina má fallback na Segoe UI Variable, ak sa font nepodarí načítať.
5. **Ovládacie prvky nemenia polohu podľa dynamického obsahu.** Šípky, „Dnes“ a prepínač kalendára sú
   pred titulkom s pevnými šírkami; tlačidlo tagu má pevnú šírku; ostatné prvky sú ukotvené k pravému okraju.

## Kalendár

Kalendár nie je vyskakovacie okno. `CalendarPanel` je súčasťou hlavného okna: zbalený je to týždenný
pás v hranatých zátvorkách (minulé dni šrafované, štítok pod pásom), rozbalený (Ctrl+Shift+C, stav sa
pamätá) mesačná mriežka s bublinami dní, čísiel týždňov a štítkami medzi týždňami. Šípky posúvajú
týždeň resp. mesiac.

## Tagy

`TagRail` – zvislé záložky pri pravom okraji. Výber tagu = aktívny tag (filter histórie, stlmenie
ostatných dní, tag nových poznámok); aktívna záložka vyčnieva smerom k obsahu, lišta sa nezbaľuje.

## Editor

Ľavý stĺpec editora: čas vzniku poznámky hore, pod ním bodkovaná deliaca čiara k textu; na poslednom
viditeľnom riadku čas poslednej úpravy a pri pretečení textu odkaz „pokračuje ⌄“ (posunie na koniec).
