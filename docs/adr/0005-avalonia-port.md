# ADR 0005 – macOS a Linux cez Avalonia

## Kontext
WinUI 3 (UI) a RichEdit (editor) bežia len na Windows. Doménové knižnice (Core, Storage, Index, Sync)
sú od začiatku `net11.0` a testujú sa na Linuxe. Cieľ: natívna (Native AOT) aplikácia pre macOS a Linux
bez zmeny správania Windows aplikácie.

## Rozhodnutie
- **Avalonia 12 + AvaloniaEdit 12** (vetva `avalonia-port`). Uno Platform zachová WinUI API, ale na
  Skia nemá textový editor – editor by sa písal znova tak či tak; AvaloniaEdit má čísla riadkov, vlastné
  farbenie (`DocumentColorizingTransformer`) aj inline prvky a Native AOT Avalonie je zrelý.
- **`WolfMark.Presentation`** (`net11.0`, bez UI frameworku): view modely, `AppHost`, `LaunchOptions`,
  `Debouncer`, generované `R` a anglický fallback. Menné priestory ostali (`WolfMark.App.*`), WinUI kód
  sa nemenil. Dva háčiky: `IUiDispatcher` pre `UiThread` (WinUI `DispatcherQueue` / Avalonia `Dispatcher`)
  a `Strings.Source` (MRT na Windows, generovaná slovenská tabuľka inde).
- **`WolfMark.Desktop`**: UI v C# ako na Windows (ADR 0002). Výnimka: `App.axaml` len s témami –
  `StyleInclude` témy AvaloniaEdit je AOT-bezpečný iba ako kompilovaný XAML (v kóde `IL2026` a za behu
  „No precompiled XAML found“).
- Jedna inštancia na dátový priečinok: lock súbor + Unix socket (namiesto `AppInstance`).

## AOT
- Overené: `linux-x64` publish bez AOT/trim varovaní, ~29 MB binárka + `libSkiaSharp`, `libHarfBuzzSharp`,
  `libe_sqlite3`, `vec0` (sémantické vyhľadávanie má natívnu knižnicu aj na Linuxe).
- **Publish v dvoch krokoch**: `dotnet build -c Release -r linux-x64` a potom `dotnet publish … --no-build`.
  Priamy `dotnet publish` (.NET 11 RC2 + Avalonia 12.1) dá ILC nepatchnutú zostavu bez skompilovaného XAML
  a aplikácia spadne pri štarte.
- macOS (`osx-arm64`) sa dá AOT skompilovať len na Macu (Apple toolchain) – CI job na `macos-latest`.

## Dôsledky
- Farby a typografia (`Theming/Palette.cs`, `Typo.cs`) sú v Desktop zatiaľ kópiou hodnôt z Windows aplikácie.
- Kontrola pravopisu mimo Windows zatiaľ chýba (na Windows ju dáva Windows Spell Checking API, ADR 0007); neskôr Hunspell po AOT overení.
