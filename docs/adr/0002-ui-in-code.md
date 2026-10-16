# ADR 0002 – WinUI UI v C# (bez XAML) a ručný composition root

- Stav: prijaté
- Backlog: M0-02, M0-03, M4-09, M5, M6

## Kontext

- Pravidlá projektu: Native AOT, žiadna reflexia (`{Binding}` je reflexný), warningy = chyby.
- XAML kompilátor (`XamlCompiler.exe`) a PRI nástroje bežia len na Windows. Doménové knižnice sa
  vyvíjajú a testujú aj na Linuxe (CI job `domain`, vývoj vo WSL).
- Studený štart < 300 ms.

## Rozhodnutie

1. **Celé UI je postavené v C#** (`WolfMark.App/Views`, helpery v `Ui/Ui.cs`): žiadne `.xaml` súbory.
   `App` implementuje `IXamlMetadataProvider` delegovaním na `XamlControlsXamlMetaDataProvider`.
   - Väzby sú explicitné: `element.Bind(vm, nameof(Prop), () => …)` – bez reflexie, AOT-safe.
   - Zoznamy sú `ItemsRepeater` s vlastným `IElementFactory` (`ElementFactory<TItem,TView>`) –
     virtualizácia a recyklácia bez `DataTemplate`.
   - Dizajnové tokeny (`Theming/Palette.cs`) sú zdieľané `SolidColorBrush`, ktorým sa pri zmene témy
     mení farba na mieste.
   - Texty z `.resw` (`Strings/sk-SK`, `Strings/en-US`) cez MRT Core `ResourceLoader`; súbory aj
     `R.cs` generuje `tools/gen-strings.py` z jednej tabuľky.
2. **Composition root bez DI kontajnera** (`AppHost`): konštruktory, žiadna reflexia, nulová réžia pri štarte.
3. Na ne-Windows hostoch `Directory.Build.props` vypne PRI a self-contained manifest kroky, takže
   `dotnet build WolfMark.sln -p:Platform=x64` na Linuxe **typovo skontroluje celé UI** (vrátane
   CsWinRT AOT analyzérov). Skutočný build/publish je na Windows (CI job `windows`).

## Dôsledky

- Bez XAML designeru a Hot Reload pre XAML. Vizuálne ladenie prebieha spustením aplikácie.
- Prechod späť na XAML je možný po jednotlivých view – view modely sú nezávislé.
