# ADR 0016 – Jeden kôš na všetkých platformách, správa v Nastaveniach

- Stav: prijaté (2026-11-17, rozhodnutie autora); nahrádza výber koša z ADR 0015
- Backlog: M9-13, M11-02

## Kontext
ADR 0015 posielalo zmazané poznámky do Koša Windows (pevný disk) alebo do `.wolfmark/trash` (všade inde).
Obnova zmazanej poznámky z aplikácie tak nebola možná práve tam, kde beží najčastejšie: Kôš Windows sa nedá
rozumne vypísať ani z neho obnoviť bez COM rozhraní shellu, čo je pod Native AOT veľa krehkého kódu. Dva koše
zároveň znamenali dve správania a dve cesty kódu.

## Rozhodnutie
- **Jeden kôš: `<koreň>/.wolfmark/trash`** na Windows, macOS aj Linuxe, v bežnom aj prenosnom režime (`NoteTrash`).
  Poznámka si v ňom drží priečinok, ktorý mala; meno nesie čas zmazania v UTC
  (`praca_1403.20261117-135357.md`, pri zhode v tej istej sekunde `-2`, `-3`…). Nič nie je v databáze – kôš prežije
  zmazanie indexu a cestuje s poznámkami (OneDrive, prenosný disk), takže poznámku zmazanú na jednom počítači
  možno obnoviť na druhom.
- **Správa v Nastavenia → Kôš** (nie filter histórie): zoznam s titulkom, pôvodným dňom, tagom a časom zmazania,
  **Obnoviť**, **Odstrániť**, **Vyprázdniť kôš** a doba uchovávania. Logika je v `TrashModel` (spoločná pre obe
  aplikácie).
- **Obnova vracia poznámku na pôvodné miesto** (`DirectoryEngine.RestoreAsync`). Ak meno medzitým obsadila iná
  poznámka, dostane ďalšie voľné podľa bežných kolíznych pravidiel – nič sa neprepíše. Obnovená poznámka sa otvorí.
- **Aplikácia sama nič neodstraňuje.** Doba uchovávania je predvolene „Nikdy“; používateľ si môže zvoliť 30, 90
  alebo 365 dní (kontroluje sa pri štarte).
- **Odstránenie z koša** (jednej poznámky, vyprázdnenie, uchovávanie) ide do Koša Windows tam, kde pre poznámky
  existuje (`WindowsRecycleBin.CanRecycle`, nie v prenosnom režime ani v deme) – dá sa teda vrátiť aj to. Inde je
  odstránenie konečné: jediné miesto, kde aplikácia maže natrvalo, vždy na pokyn používateľa a po potvrdení
  (tlačidlo sa zmení na otázku). Výnimka z pravidla 5 je zapísaná v `CLAUDE.md`.
- **Poznámky Claude Code** idú do koša ako ostatné. Pri ďalšom importe sa vrátia (rozhodnutie z 2026-11-17: import je
  záloha promptov), kópia v koši ostáva.
- Presun celého koreňa poznámok (zmena priečinka, prenosný režim) sa nemení: starý priečinok ide do Koša Windows
  alebo do `.wolfmark-trash` vedľa neho (ADR 0015).

## Dôsledky
- Zmazané poznámky zaberajú miesto v priečinku poznámok (a v OneDrive), kým ich používateľ neodstráni alebo
  nenastaví uchovávanie.
- Poznámky zmazané staršími verziami na Windows ostávajú v Koši Windows; aplikácia ich nevidí. Súbory, ktoré staršie
  verzie uložili do `.wolfmark/trash` bez času v mene, sa zobrazia s časom poslednej zmeny.
- Obrázky zmazanej poznámky ostávajú v `assets` jej dňa – po obnove ich nájde. Osirelé obrázky sa neriešia.
- Kôš leží v skrytom priečinku: index, watcher ani vyhľadávanie ho nevidia.
- Testy: `NoteTrashTests`, `TrashModelTests`; stránka Nastavení overená na snímke Avalonia aplikácie.

## Zvažované alternatívy
- **Filter „Kôš“ v histórii** (pôvodný návrh): menej nového UI, ale miešal by zmazané poznámky s bežnými
  a správa (vyprázdnenie, uchovávanie) by aj tak potrebovala miesto v Nastaveniach.
- **Kôš Windows + tlačidlo „Otvoriť Kôš“:** obnova by na Windows z aplikácie nefungovala vôbec.
- **Čas zmazania v indexe:** stratil by sa so zmazaním `index.db` (pravidlo 4).
