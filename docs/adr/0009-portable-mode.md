# ADR 0009 – Prenosný (portable) režim

## Kontext
WolfMark ukladá poznámky do `OneDrive|Documents\WolfMark` a nastavenia, index a logy do
`%LOCALAPPDATA%\WolfMark`. Používateľ chce aplikáciu nosiť so sebou (USB kľúč, synchronizovaný priečinok)
aj so všetkými dátami a spúšťať ju na rôznych počítačoch bez inštalácie a bez stôp v systéme.

## Rozhodnutie
- **Značka vedľa exe rozhoduje o režime.** `WolfMark.portable.json` (`{ "version": 1, "dataDirectory":
  "WolfMarkData" }`) v priečinku aplikácie zapne prenosný režim; relatívna cesta sa rieši voči priečinku exe,
  absolútna je povolená (ručná úprava). Číta sa raz na začiatku procesu (`PortableMode.DataDirectory`,
  `LocalPaths.Default`), všetky cesty sú potom pevné – prepnutie vyžaduje reštart.
- **Rozloženie** ako demo: `<data>/local/` (= `LocalPaths`: settings, index, models, logy, cache) a
  `<data>/notes/` (koreň poznámok). `RootDirectory` v nastaveniach je relatívny k dátovému priečinku
  (`"notes"`), takže iné písmeno disku na inom počítači nevadí. Vybraný priečinok mimo dátového sa ukladá
  absolútne.
- **Poškodená značka = chyba štartu**, nie návrat do štandardného režimu: inak by sa na cudzom počítači
  ukázala prázdna aplikácia a poznámky by sa písali mimo prenosného priečinka.
- **Zapnutie** (Nastavenia → Všeobecné → Prenosný režim): `PortableMigrator.EnableAsync` – cieľ musí byť
  prázdny; skopíruje poznámky (overenie obsahu), index (`VACUUM INTO` cez čítacie spojenie – konzistentné
  aj s WAL, bez druhého writera), modely a nastavenia (s relatívnym koreňom); **značku zapíše posledná**.
  Pri chybe sa čiastočná kópia odstráni (obsahuje len práve vytvorené kópie, originály sú nedotknuté)
  a nič sa neprepne. Používateľ volí *Kopírovať* (originál ostane, ďalej sa neaktualizuje) alebo *Presunúť*
  (pôvodný priečinok poznámok po overení do koša). `%LOCALAPPDATA%\WolfMark` ostáva – index je otvorený
  a je to len cache.
- **Vypnutie**: `PortableMigrator.DisableAsync` skopíruje poznámky do pôvodného koreňa (z nastavení v profile,
  inak predvolený), index, modely a nastavenia do `%LOCALAPPDATA%\WolfMark` a zmaže značku. Poznámka, ktorá
  sa v cieli líši, ide do koša a nahradí ju prenosná verzia (nič sa neprepisuje na mieste). **Len kopírovanie**:
  prenosný priečinok ostane – na vymeniteľnom disku nie je systémový kôš, presun by mazal natrvalo
  (CLAUDE.md, pravidlo 5).
- **Kôš v prenosnom režime** je vždy `<root>/.wolfmark/trash` (`FolderRecycleBin`) – Windows na
  vymeniteľných diskoch maže natrvalo.
- **Žiadna integrácia so systémom**: spúšťanie po prihlásení (HKCU Run), priradenie `.md` (HKCU Classes,
  Capabilities, RegisteredApplications) a predvolená aplikácia na macOS/Linuxe (`.desktop`, mimeapps,
  Launch Services) sú v prenosnom režime vypnuté s vysvetlením. Pri zapnutí sa zrušia registrácie tejto
  kópie (Run hodnota len ak ukazuje na tento exe), aby v systéme neostali stopy, ktoré by sa už nedali
  vypnúť z UI. Po vypnutí ostávajú vypnuté.
- Prepnutie je dostupné len ak sa dá do priečinka aplikácie zapisovať (nie Program Files) a nie pre macOS
  `.app` balík; v demo režime nie.
- Režim je viditeľný v titulku nastavení („Prenosná verzia“), v sekcii Prenosný režim (cesta) a v Diagnostike.

## Dôsledky
- Kľúč jednej inštancie (`InstanceIdentity`) je z dátového priečinka – prenosná a nainštalovaná kópia bežia
  nezávisle. Pri vypínaní sa index zapisuje do `%LOCALAPPDATA%\WolfMark`; ak tam práve beží nainštalovaná
  kópia, jej index sa prepíše pod rukami (je to cache – v najhoršom prípade sa prebuduje).
- Watcher a import Claude Code sa pred kopírovaním zastavia; po prepnutí (aj po chybe) treba reštart.
- Absolútne cesty v nastaveniach (vlastný `.claude` priečinok, história externých súborov) na inom počítači
  nemusia existovať – sync vtedy nič neimportuje, externý súbor hlási, že chýba.
- Vypnutie neodstráni poznámky, ktoré boli v pôvodnom priečinku a v prenosnom režime sa zmazali.
