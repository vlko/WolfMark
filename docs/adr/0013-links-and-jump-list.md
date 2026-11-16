# ADR 0013 – Odkazy `wolfmark://` a jump list (M8-03)

## Kontext
SPEC §8: jump list v taskbare (Nová poznámka, Dnes, Vyhľadať) a protokol `wolfmark://` na otvorenie konkrétneho
dňa alebo poznámky z iných aplikácií. Aplikácia je nebalená (bez MSIX, M10-01) a beží pod Native AOT.

## Rozhodnutie
- **Jeden formát odkazov** `AppLink` (Core) pre protokol, jump list aj príkazový riadok:
  `wolfmark://today`, `wolfmark://day/2026-11-16`, `wolfmark://note/2026/11/16/praca_1403` (cesta poznámky bez
  `.md`, segmenty URL-escapované), `wolfmark://new` (= `--quick-note`, okno rýchlej poznámky z M8-01),
  `wolfmark://search?q=…`. Parsuje sa ručne (nie `System.Uri`, ktorý mení veľkosť písmen hostiteľa a toleruje `\`).
- **Odkazy len navigujú.** Odkaz môže otvoriť ktorákoľvek webstránka, preto nič nezapisujú ani nemažú.
  Cesta poznámky musí byť platná `NotePath` vnútri stromu (žiadne `..`, `.`, skryté ani interné priečinky,
  žiadne escapované `/`, `\`, `:`). Neexistujúca poznámka sa ohlási, nič sa nevytvorí. Neplatný odkaz sa
  ohlási a nikdy sa nepovažuje za súbor.
- **Odkaz ide cez príkazový riadok** (`WolfMark.exe "wolfmark://…"`), takže bežiaca inštancia ho dostane ako
  každé ďalšie spustenie: na Windows presmerovaná aktivácia (M0-05), na macOS/Linuxe socket jednej inštancie
  (posiela odkaz namiesto súboru; `--quick-note` sa posiela ako `wolfmark://new`). Na macOS prichádza odkaz
  ako Apple Event (`ProtocolActivatedEventArgs`).
- **Windows – protokol:** HKCU `Software\Classes\wolfmark` (`URL Protocol`, príkaz tohto exe, v deme
  s `--demo`). **Prepínač v Nastaveniach** (Markdown súbory → Odkazy wolfmark://), predvolene vypnutý.
  Schému môže vlastniť len jedna kópia – tá, ktorá ju zapla posledná; vypnutie zmaže kľúč len ak patrí tomuto exe.
- **Windows – jump list:** `ICustomDestinationList` cez CsWin32 (štruktúrne COM rozhrania, `allowMarshaling:
  false` – bez vstavaného COM marshallingu, ktorý Native AOT nemá). WinRT `JumpList` potrebuje identitu balíka.
  Úlohy sú shell linky na exe s odkazom, prepisujú sa pri každom štarte v aktuálnom jazyku (exe sa mohol
  presunúť). Na vlastnom MTA vlákne mimo UI. Vždy zapnutý, okrem dema a prenosného režimu.
- **Linux:** akcie desktop entry (`Actions=new-note;today;search;`) – ponuka ikony v spúšťači/docku – v entry,
  ktorú zapisuje priradenie `.md`, aj v balíku (`tools/package-desktop.sh`). Protokol: skrytá entry
  `wolfmark-<hash>-links.desktop` (`%u`, nie `%f`) ako `x-scheme-handler/wolfmark` cez xdg-mime, prepínačom
  v Nastaveniach.
- **macOS:** `CFBundleURLTypes` v `Info.plist` – schému registruje balík sám (Nastavenia to len vysvetlia);
  ponuka v Docku cez `NativeDock`.
- **Kopírovanie odkazu:** ponuka v histórii „Kopírovať odkaz wolfmark://“ vedľa odkazu `[[…]]`.
- Prenosný režim (ADR 0009): pri zapnutí sa protokol tejto kópie aj jump list odstránia a nastavenia ich neponúkajú.

## Dôsledky
- Shell link v jump liste nesie cestu k exe – po presune aplikácie platí až po ďalšom štarte.
- Jump list sa zobrazí pod AUMID, ktorý Windows odvodí z exe; ak by ho Windows App SDK nastavil inak, ponuka sa
  neukáže (overené ručne na Windows).
- Smoke testy (`links`) overujú odkazy z príkazového riadka v bežiacej aj novej inštancii; registráciu protokolu
  ani jump list nemenia (smoke test nesmie meniť počítač).
