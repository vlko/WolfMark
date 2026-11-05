# ADR 0008 – Externé .md súbory, jedna inštancia, predvolená aplikácia

## Kontext
WolfMark má byť predvolenou aplikáciou na otváranie `.md` súborov – na Windows, macOS aj Linuxe. Súbor zo
stromu poznámok je poznámka. Súbor mimo stromu (README projektu, poznámka z iného nástroja) poznámkou nie je,
ale používateľ ho chce upraviť v tom istom editore, vidieť ho v histórii a prípadne si ho skopírovať medzi
poznámky. Na testovanie musí ísť to isté spraviť z príkazového riadku a vedľa nainštalovanej verzie musí ísť
spustiť vývojový build.

## Rozhodnutie
- **Otvorenie súboru = argument.** Prvý argument, ktorý nie je prepínač, je súbor (`LaunchOptions.OpenFile`,
  prevedený na plnú cestu). Takto ho spúšťa aj asociácia v systéme, takže príkazový riadok a dvojklik idú
  tou istou cestou. Na macOS posiela Finder súbor ako Apple Event – Avalonia ho dá cez `IActivatableLifetime`
  (`FileActivatedEventArgs`).
- **Jedna inštancia na dátový priečinok** (`InstanceIdentity`): kľúč je hash cesty k dátam (settings, index).
  Ďalšie spustenie s tými istými dátami – aj z inej kópie aplikácie – odovzdá súbor bežiacej inštancii
  a skončí. Dva procesy tak nikdy nezapisujú do toho istého `index.db` (CLAUDE.md, jeden writer). Vývojový
  build beží vedľa nainštalovaného s inými dátami (`--demo <dir>`). Miesto inštalácie rozhoduje o registrácii:
  každá kópia aplikácie sa registruje pod vlastným menom (hash svojho priečinka), takže dev build neprepíše
  nainštalovaný. Windows: `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync` (argumenty idú ako
  jeden reťazec, delí ich `CommandLine.Split`). macOS/Linux: zámok + Unix socket v dátovom priečinku, cesta
  k súboru ide cez socket.
- **Súbor zo stromu** otvorí hlavné okno ako každú poznámku (`NotesTree.TryGetNotePath`; súbory v `assets`
  a skrytých priečinkoch sú externé). **Súbor mimo stromu** dostane vlastné okno (jedno na súbor): priečinok
  a názov namiesto dňa, formátovanie, Raw/Formát, Uložiť, odznak v rohu editora s kópiou do dnešných poznámok
  (ako pri importovanej Claude Code poznámke), stavový riadok
  (vytvorený, zmenený, slová, kódovanie a konce riadkov).
- **Externý súbor sa neukladá sám.** Je cudzí – autosave by ho menil pri každom ťuknutí a iné nástroje
  (git, synchronizácia) by videli rozpracované stavy. Uložiť je povolené len pri zmene, zatvorenie sa pri
  zmenách pýta. Uloženie zachová BOM aj konce riadkov (nezmenený súbor = rovnaké bajty) a je atomické.
  Zmenu na disku načíta, ak editor nemá úpravy; inak sa pýta (Načítať / Ponechať moje).
- **Vkladanie obrázkov je v externom okne vypnuté** – aplikácia nezakladá priečinky vedľa cudzích súborov.
  Relatívne obrázky a odkazy sa riešia voči priečinku súboru.
- **Externé súbory v histórii sú v `settings.json`** (`ExternalFiles`: cesta + čas otvorenia/uloženia,
  najviac 50), nie v indexe: index je cache stromu poznámok a musí sa dať zmazať (CLAUDE.md, súbory sú zdroj
  pravdy). História ich zaradí podľa času medzi poznámky; filter tagu a úloh ich vynechá, filter dňa ich
  ukáže v deň otvorenia. Názov a náhľad sa čítajú zo začiatku súboru (64 kB) mimo UI vlákna, cache podľa
  času zmeny.
- **Registrácia v systéme** je voľba v nastaveniach, stav v systéme je zdroj pravdy:
  - Windows: HKCU (bez admin práv) – ProgID s príkazom, `.md\OpenWithProgids`, Capabilities
    a `RegisteredApplications`; potom `SHChangeNotify` a nastavenia Predvolené aplikácie. Windows nedovolí
    aplikácii nastaviť sa za predvolenú, používateľ to potvrdí.
  - macOS: `CFBundleDocumentTypes` v Info.plist (Finder ponúkne WolfMark v Otvoriť s) a
    `LSSetDefaultRoleHandlerForContentType` cez `LibraryImport`. Len pre aplikáciu v balíku `WolfMark.app`;
    bundle ID je jedno pre všetky kópie, ktorú vyberie Launch Services, rozhoduje systém.
  - Linux: vlastný `.desktop` súbor kópie v `~/.local/share/applications` s `MimeType=text/markdown`
    a `xdg-mime default`.

## Dôsledky
- Relatívna cesta v argumente sa na Windows pri presmerovaní vyhodnotí voči priečinku bežiacej inštancie
  (aktivácia nenesie pracovný priečinok druhého procesu) – z príkazového riadku treba plnú cestu; Prieskumník
  ju dáva vždy. Na macOS/Linuxe cestu doplní spúšťajúci proces.
- Ukončenie aplikácie z hlavného okna najprv zavrie externé okná (s otázkou pri neuložených zmenách).
