# ADR 0015 – Kôš podľa typu disku (M11-02)

- Stav: prijaté (2026-11-17); správanie na sieťovom a vymeniteľnom disku čaká na ručné overenie na Windows
- Backlog: M1-06, M8-09, M11-02

## Kontext
Pravidlo 5 (`CLAUDE.md`): poznámky sa nikdy nemažú natrvalo. Na Windows sa mazalo cez `SHFileOperation`
s `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT`. Na zväzku bez Koša – sieťové zdieľanie, väčšina vymeniteľných
diskov – však Windows s týmito príznakmi súbor zmaže natrvalo a bez opýtania. Prenosný režim to už riešil vlastným
priečinkom (ADR 0009), bežný koreň na NAS-e alebo USB nie. Rovnakou cestou ide pri presune aj celý starý koreň
poznámok.

## Rozhodnutie
- **Kôš Windows len na pevnom lokálnom disku** (`WindowsRecycleBin.CanRecycle`: `DriveType.Fixed`, nie UNC cesta).
  Všade inde – sieťový a vymeniteľný disk, prenosný režim, demo, macOS, Linux – ide poznámka do
  `<koreň>/.wolfmark/trash` (`FolderRecycleBin`); celý priečinok pri presune do `.wolfmark-trash` vedľa neho.
- **`FOF_WANTNUKEWARNING`** pri mazaní cez Windows: ak by systém položku predsa zničil (Kôš pre disk vypnutý,
  položka väčšia než Kôš), najprv sa opýta; odmietnutie = chyba, nič sa nezmaže.
- Rozhoduje sa pri štarte podľa koreňa poznámok; presun dát a vypnutie prenosného režimu sa rozhodujú podľa
  priečinka, ktorý sa odkladá.

## Dôsledky
- Na sieťovom a vymeniteľnom disku sa kôš neprázdni sám a synchronizuje sa s poznámkami (leží v koreni).
  Obnova z koša v UI zatiaľ nie je (návrh v BACKLOG.md).
- Typ disku nehovorí všetko: pevný disk s vypnutým Košom pokrýva až `FOF_WANTNUKEWARNING`, ktorého dialóg
  sa nedá overiť automaticky.
- Z WSL ani v CI sa toto správanie overiť nedá (demo má vždy vlastný kôš) – ostáva ručný test.

## Zvažované alternatívy
- **`SHQueryRecycleBin`:** pre sieťové disky vracia chybu nespoľahlivo a o vypnutom Koši nehovorí nič.
- **Vždy vlastný priečinok:** používateľ na bežnom disku čaká zmazané poznámky v Koši Windows, kde ich vie obnoviť.
- **`IFileOperation` s `FOFX_RECYCLEONDELETE`:** COM rozhranie pod Native AOT bez vstavaného marshallingu je
  výrazne viac kódu pre ten istý výsledok.
