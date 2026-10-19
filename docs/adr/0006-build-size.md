# ADR 0006 – Veľkosť a rýchlosť AOT buildov

## Kontext
Vydanie v0.2.1: Windows zip 52,8 MB (131 MB rozbalené), Linux/macOS ~19 MB. Windows aplikácia je
self-contained (`WindowsAppSDKSelfContained`), takže nesie celý Windows App SDK runtime.

## Rozhodnutie
- **Komponenty Windows App SDK namiesto metabalíka** `Microsoft.WindowsAppSDK`: WinUI, Foundation,
  InteractiveExperiences, DWrite, Base (+ Runtime len kvôli kontrolám SDK mimo Windows, nepridáva DLL).
  Metabalík pribalil AI, ML (`onnxruntime.dll` 20,7 MB, `DirectML.dll` 17,8 MB), Search a Widgets –
  42 súborov, 54,9 MB rozbalené / 22,4 MB v zipe, aplikácia ich nepoužíva.
- **Jazyky WinUI**: po publishi ostanú len `en-us` a `sk-SK` `.mui` (cieľ `TrimWinUILanguages`),
  ~1,7 MB v zipe. Pri inom jazyku systému a jazyku „podľa systému“ sú aj vstavané texty WinUI anglicky,
  rovnako ako texty aplikácie.
- **AOT na rýchlosť + Runtime Async** (`Directory.Build.props`): `OptimizationPreference=Speed`
  (ILC `--Ot`, statické PGO dáta a vxsort pre GC) a `Features=runtime-async=on` (async metódy bez
  stavových automatov kompilátora – v `WolfMark.Storage` z 26 ostali 2, ďalšie sú async iterátory).
  Cena: ~+3 MB binárky (+10 %); štart sa nezmenil, prínos je v priepustnosti (lexer, indexer, async I/O).
  Runtime Async je v .NET 11 preview – pri problémoch vypnúť pre projekt `<UseRuntimeAsync>false</UseRuntimeAsync>`.
  Meranie na `linux-x64` (binárka `WolfMark.Desktop`, štart = po zobrazenie okna pod Xvfb, 6 behov):

  | variant | binárka | gzip | štart | RSS |
  |---|---|---|---|---|
  | predvolený | 28,80 MB | 12,63 MB | ~515 ms | ~152 MB |
  | `OptimizationPreference=Size` | −1,9 % | −1,2 % | bez zmeny | bez zmeny |
  | `Features=runtime-async=on` | −0,3 % | −0,1 % | bez zmeny | bez zmeny |
  | Size + runtime-async | −2,2 % | −1,4 % | bez zmeny | bez zmeny |
  | `OptimizationPreference=Speed` (PGO, vxsort) | +10,5 % | +11,7 % | bez zmeny | bez zmeny |
  | **Speed + runtime-async (zvolené)** | 31,75 MB, +10,2 % | | bez zmeny | |

  Štart určuje inicializácia UI frameworku (Avalonia/Skia, WinUI), nie generovaný kód. Variant Size by
  ušetril len ~0,5 MB, preto prednosť dostala rýchlosť kódu.

## Zámerne nepoužité
- `StackTraceSupport=false`, `UseSystemResourceKeys=true` – logy pádov by stratili mená metód a správy.
- `InvariantGlobalization` – slovenská kultúra (dátumy, triedenie).
- `IlcInstructionSet=x86-x64-v3` – vylúči CPU bez AVX2, zisk pre túto aplikáciu zanedbateľný.
- `WindowsAppSDKSelfContained=false` – ~17 MB menší zip, ale vyžaduje nainštalovaný Windows App Runtime.
- Mazanie `.winmd`/`.pri` – WinUI ich potrebuje na aktiváciu a zdroje.

## Dôsledky
- Novú funkciu Windows App SDK (napr. notifikácie sú vo Foundation, AI nie) treba pridať ako komponent
  do `Directory.Packages.props`; verzie komponentov držať podľa metabalíka danej verzie.
- Po vydaní .NET 11 GA zopakovať meranie a overiť, že Runtime Async ostáva stabilné.
