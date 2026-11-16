# ADR 0012 – Rýchla poznámka a globálna skratka (M8-01)

## Kontext
SPEC §8 chce globálnu skratku (napr. `Win+Shift+N`) na rýchly zápis: „malé okno, napíšem, Enter, zmizne“.
Zvažovali sme dve podoby: skratka otvorí **hlavné okno** v režime celého okna (F11) s novou poznámkou, alebo
**samostatné malé okno**. Skratka má zmysel hlavne vtedy, keď je používateľ v inej aplikácii; hlavné okno je
aj tak na Alt+Tab a `NewNoteAsync` v ňom prepne deň, otvorenú poznámku aj históriu – kto si čítal starší
záznam, stratil by ho.

## Rozhodnutie
- **Samostatné okno** `QuickNoteWindow` (Windows aj Avalonia) s vlastným `NoteEditorViewModel`: nová poznámka
  dnešného dňa, **predvolene bez tagu** (výber tagu priamo v okne, `TagPicker` zdieľaný s hlavným oknom).
  Ukladá sa priebežne ako v hlavnom okne; prázdna poznámka nevznikne (rovnaké pravidlo ako všade). Hlavné
  okno sa nemení – históriu a týždeň obnoví index ako pri každej novej poznámke.
- **Esc alebo Ctrl+Enter (⌘↩) okno skryje** (po uložení). Holý Enter nie – poznámky sú viacriadkové
  a Enter pokračuje zoznamy. Editor preto Ctrl+Enter nespracúva (nechá ho oknu).
- **Okno sa skrýva, nezatvára**: ďalšia skratka ho ukáže hneď. Ak je otvorené a má text, skratka ho len
  vytiahne dopredu (nezahodí rozpísaný text); inak dostane novú poznámku. Zatvorí sa natrvalo až s aplikáciou
  (inak by skryté okno držalo proces pri živote).
- **„Pokračovať v hlavnom okne“** – poznámka sa otvorí v hlavnom okne v režime celého okna (pôvodná predstava
  ako druhý krok).
- **Umiestnenie**: stred monitora s kurzorom myši (Windows; Avalonia: monitor hlavného okna), horná tretina,
  640 × 380 DIP podľa DPI cieľového monitora.
- **Windows: `RegisterHotKey`** (CsWin32) na HWND hlavného okna, `WM_HOTKEY` cez subclass ako tray ikona –
  funguje aj so skrytým oknom v oblasti oznámení. `MOD_NOREPEAT`. Skratka je v nastaveniach
  (`QuickNote.HotkeyEnabled`, **predvolene vypnutá**; `QuickNote.Hotkey`, predvolene `Win+Shift+N`) a nahráva
  sa stlačením v Nastaveniach → Všeobecné. Počas nahrávania je registrácia pozastavená. `Ctrl+Alt+…` nie je
  predvolená: na slovenskej klávesnici je to AltGr (`AltGr+N` = `}`).
- **`KeyChord`** (Core) – text ↔ modifikátory + kláves: písmená, číslice, F1–F24, medzerník (rovnaký
  virtual-key kód na každom rozložení). Aspoň jeden z Ctrl/Alt/Win; samotný Shift by vzal znak všetkým
  ostatným aplikáciám. Neplatná hodnota v nastaveniach = predvolená skratka.
- **Obsadená skratka** (iná aplikácia ju už registrovala) → oznámenie v hlavnom okne s výzvou zvoliť inú.
- **macOS/Linux: `--quick-note`** namiesto vlastnej globálnej skratky. Wayland aplikáciám globálne skratky
  nedovolí (portál `GlobalShortcuts` nie je všade a vyžaduje D-Bus), X11 `XGrabKey` by fungoval len na X11
  a macOS Carbon `RegisterEventHotKey` tu nevieme otestovať. Príkaz priradí používateľ systémovej skratke
  (GNOME/KDE vlastné skratky, macOS Skratky); Nastavenia ho ukážu s tlačidlom Kopírovať. Spustenie s
  `--quick-note` pošle bežiacej inštancii požiadavku cez socket jednej inštancie (súbor je vždy plná cesta,
  takže sa s prepínačom nezamení); nebeží → aplikácia sa spustí a okno otvorí. Na Windows funguje rovnako
  (presmerovaná aktivácia) – hodí sa pre jump list (M8-03).
- V demo režime sa skratka neregistruje (demo by ju vzalo bežiacej inštancii); `--quick-note` funguje.

## Dôsledky
- Dva editory nad rôznymi poznámkami naraz. Poznámku z rýchleho okna otvorenú zároveň v hlavnom okne (cez
  históriu, kým je rýchle okno otvorené) by si mohli prepisovať – lokálne uloženia sa medzi editormi
  nehlásia. Pokračovanie v hlavnom okne preto rýchle okno najprv uloží a skryje.
- Skratka funguje len kým WolfMark beží; zatvorenie okna bez tray ikony aplikáciu ukončí.
- Smoke testy (`tools/smoke-windows.ps1 -Checks quicknote`, `tools/smoke-linux.sh`) overujú `--quick-note`
  (okno v bežiacej inštancii, opakovane jedno okno, prvé spustenie); samotnú skratku nestláčajú.
