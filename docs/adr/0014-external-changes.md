# ADR 0014 – Zmena otvorenej poznámky zvonka: podmienené uloženie, pri odchode obe verzie (M11-01)

- Stav: prijaté (2026-11-17)
- Backlog: M4-08, M11-01

## Kontext
Poznámky ležia v OneDrive a upravuje ich aj iný počítač alebo iný editor. Pôvodne editor pri zmene zvonka
s rozpísaným textom len zobrazil výzvu „Načítať z disku / Ponechať moje“ – autosave (800 ms po poslednom znaku)
však bežal ďalej a verziu z disku prepísal skôr, než používateľ odpovedal. Druhé okno bolo ešte pred výzvou:
watcher hlási zmenu až po 300 ms debounce a uloženie v tom čase prepísalo zmenu, o ktorej editor nevedel.
Princíp 4 špecifikácie („bez straty dát“) tým neplatil.

## Rozhodnutie
- **Uloženie je podmienené.** Editor si pamätá hash bajtov súboru, ako ho naposledy prečítal alebo zapísal
  (`DirectoryEngine.ReadSnapshotAsync`). `DirectoryEngine.TrySaveAsync` pred zápisom súbor prečíta a zapíše len vtedy,
  ak má stále tento hash (alebo už obsah, ktorý sa ide zapísať). Inak nezapíše nič a editor prejde do stavu
  „zmena zvonka čaká“ – rovnako, ako keby ju ohlásil watcher.
- **Kým výzva čaká, neukladá sa.** Autosave ani uloženie pri strate fokusu súbor neprepíšu.
- **Ponechať moje** prepíše bez podmienky; **Načítať z disku** zahodí úpravy editora.
- **Odchod bez odpovede zachová obe verzie.** Ak používateľ prejde na inú poznámku alebo ukončí aplikáciu, text
  editora sa uloží ako nová poznámka toho istého dňa a tagu (meno podľa bežných kolíznych pravidiel) a súbor na disku
  ostane, ako je. Zatvorenie do oblasti oznámení nie je odchod – výzva počká.
- **Zmazaný súbor sa ďalším uložením vytvorí znova** (ako doteraz).
- Kontrola nie je atomická (čítanie a zápis sú dva kroky), okno sa však zmenšilo zo stoviek milisekúnd na čas
  jedného čítania; súborový systém s OneDrive viac nedovolí.

## Dôsledky
- Každé uloženie číta súbor navyše. Poznámky sú malé a beží to mimo UI vlákna; na štart ani písanie to nemá vplyv
  (Windows smoke: 399 ms do písania po zmene, rozpočet 600 ms).
- Pri odchode môže vzniknúť poznámka, o ktorú používateľ výslovne nežiadal. Je viditeľná v histórii vedľa pôvodnej
  a zapíše sa do logu; alternatíva by bola stratiť jednu z verzií.
- Watcher po ohlásení cudzej zmeny zabudne hash posledného vlastného zápisu – návrat súboru na skorší obsah je
  znova zmena (M11-09).
- Testy: `ExternalChangeTests`, `DirectoryEngineTests.TrySave_*`; smoke `external` na Windows aj Linuxe.

## Zvažované alternatívy
- **Zámok súboru počas úprav:** OneDrive a iné editory ho nerešpektujú alebo kvôli nemu zlyhajú.
- **Trojcestné zlúčenie:** pre denné poznámky neúmerne zložité; M4-08 ho necháva ako možné rozšírenie (diff UI).
- **Porovnanie podľa mtime:** OneDrive pri synchronizácii mení čas aj bez zmeny obsahu a naopak.
