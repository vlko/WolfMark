# CLAUDE.md – pravidlá pre prácu na projekte WolfMark

Čítaj najprv `SPEC.md` (čo a prečo) a `BACKLOG.md` (poradie práce). Pracuj vždy na jednej položke backlogu (`M2-04` a pod.), po dokončení over akceptačné kritériá a odškrtni ju.

## Stack v skratke
- .NET 11 (`global.json`, rollForward `latestFeature`), C# 15, WinUI 3 / Windows App SDK, **Native AOT od prvého commitu**.
- Doménová logika (Core, Storage, Index, Sync) nesmie referencovať WinUI – musí byť testovateľná v obyčajnom xUnit projekte.

## Tvrdé pravidlá
1. **AOT:** žiadna reflexia, `Activator.CreateInstance`, `Type.GetType` zo stringu, `dynamic`, reflexná JSON serializácia. JSON len cez `System.Text.Json` source generation (`JsonSerializerContext`). AOT a trim warningy sú chyby. Pred pridaním NuGet balíka over jeho AOT kompatibilitu (`IsAotCompatible`, testovací AOT publish).
2. **Bez EF Core** – `Microsoft.Data.Sqlite` s ručným mapovaním.
3. **UI vlákno je posvätné:** žiadne I/O, DB, embeddingy ani parsing na UI vlákne. Žiadne `.Result`, `.Wait()`, `GetAwaiter().GetResult()`. `CancellationToken` vo všetkých async API. Výsledky do UI cez `DispatcherQueue`.
4. **Súbory sú zdroj pravdy**, SQLite je cache – každá funkcia musí fungovať aj po zmazaní `index.db`.
5. **Zápisy na disk sú atomické** (temp + replace). Nikdy natrvalo nemazať poznámky – len do koša.
6. **Do DB zapisuje len jeden writer** (kanál). Čítanie z oddelených spojení.
7. Interop cez **CsWin32**, nie ručné `DllImport`.
8. MVVM cez **CommunityToolkit.Mvvm** source generátory.

## Konvencie
- Identifikátory a komentáre v kóde po anglicky, UI texty cez `.resw` (sk, en).
- `Nullable` zapnuté, file-scoped namespaces, `sealed` triedy predvolene.
- Každá netriviálna logika (parser názvov, lexer, indexer, Claude Code parser) má unit testy; výkonovo citlivé časti benchmark (BenchmarkDotNet).
- Architektonické rozhodnutia zapisuj ako ADR do `docs/adr/NNNN-nazov.md`.

## Príkazy
```powershell
dotnet build WolfMark.sln -p:Platform=x64
dotnet test --solution WolfMark.CrossPlatform.slnf          # všetky testy (bez WinUI projektov)
dotnet publish src/WolfMark.App -c Release -r win-x64 -p:Platform=x64      # AOT build
dotnet publish src/WolfMark.App -c Release -r win-arm64 -p:Platform=ARM64
python3 tools/gen-strings.py src/WolfMark.App/Strings      # po zmene UI textov (sk/en .resw + R.cs)
```
Na Linuxe/WSL: testy cez `WolfMark.CrossPlatform.slnf`; `dotnet build WolfMark.sln -p:Platform=x64`
typovo skontroluje aj WinUI projekty (PRI/manifest kroky sú mimo Windows vypnuté), spustiť sa nedajú.

## UI
- UI je v C# bez XAML (ADR 0002): väzby cez `Ui.Bind`, zoznamy cez `ItemsRepeater` + `ElementFactory`,
  farby cez `Theming/Palette`. View modely (`partial`, CommunityToolkit.Mvvm) nesmú robiť I/O na UI vlákne –
  volania služieb obaľ do `Task.Run`.

## Pri neistote
- Ak sa API Windows App SDK / .NET 11 líši od tvojich predpokladov, over v dokumentácii (odkazy v `SPEC.md` §10) – .NET 11 bolo v čase písania špecifikácie vo fáze RC.
- Ak niečo nejde pod AOT, zastav sa, zapíš problém a navrhni alternatívu namiesto obchádzania analyzérov.
