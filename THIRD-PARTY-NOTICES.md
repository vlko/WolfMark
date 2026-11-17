# Third-party notices

WolfMark itself is MIT-licensed ([LICENSE](LICENSE)). The downloadable builds also contain the components below,
each under its own licence. The list is taken from the packages the two apps reference
(`Directory.Packages.props` and what they pull in); the licence named is the one the package declares. The full
texts are in the packages and at the projects' pages.

## Both apps

| Component | Licence | Project |
|---|---|---|
| .NET runtime and libraries (Native AOT compiled in), `Microsoft.Extensions.Logging`, `System.IO.Hashing` | MIT | https://github.com/dotnet/runtime |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | Public domain | https://sqlite.org/copyright.html |
| sqlite-vec | MIT or Apache-2.0 | https://github.com/asg017/sqlite-vec |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Barlow and Barlow Condensed fonts | SIL Open Font License 1.1 (`OFL-Barlow.txt` next to the app) | https://github.com/jpt/barlow |

## Windows app

| Component | Licence | Project |
|---|---|---|
| Windows App SDK (WinUI 3, DWrite, Foundation, Interactive Experiences, Runtime) | Microsoft Software License Terms – `license.txt` in the packages | https://github.com/microsoft/WindowsAppSDK |
| Microsoft Edge WebView2 (comes with the Windows App SDK) | Microsoft Software License Terms – `LICENSE.txt` in the package | https://aka.ms/webview |
| Win2D | MIT (the NuGet package carries Microsoft's Win2D licence terms) | https://github.com/microsoft/Win2D |

## macOS and Linux app

| Component | Licence | Project |
|---|---|---|
| Avalonia, Avalonia.Themes.Fluent, Avalonia.Desktop and their platform back ends | MIT | https://github.com/AvaloniaUI/Avalonia |
| AvaloniaEdit | MIT | https://github.com/AvaloniaUI/AvaloniaEdit |
| SkiaSharp | MIT | https://github.com/mono/SkiaSharp |
| Skia (native library in SkiaSharp) | BSD-3-Clause | https://skia.org |
| HarfBuzzSharp | MIT | https://github.com/mono/SkiaSharp |
| HarfBuzz (native library in HarfBuzzSharp) | MIT ("Old MIT") | https://github.com/harfbuzz/harfbuzz |
| MicroCom.Runtime | MIT | https://github.com/kekekeks/MicroCom |
| Tmds.DBus.Protocol | MIT | https://github.com/tmds/Tmds.DBus |

## Build and test only (not in the downloads)

Microsoft.Windows.CsWin32 and the Win32 metadata packages, Windows SDK build tools, xunit.v3, BenchmarkDotNet,
Microsoft.Extensions.TimeProvider.Testing.

---

When a package is added or updated, this file is updated with it. To list what a build references:
`dotnet list src/WolfMark.App package --include-transitive` (and the same for `src/WolfMark.Desktop`).
