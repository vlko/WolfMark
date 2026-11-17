# Ikony aplikácie

Generuje `tools/gen-icon-proposals.py` (čistý Python, fonty Barlow z `Assets/Fonts`). Pri každej ikone
je PNG 256 px a hotové `.ico` (256, 64, 48, 32, 24, 16 px); názov súboru je id ikony. Prehľad všetkých:
`sheet.png` (číslo pod ikonou je jej poradie vo výbere, vedľa veľkosti 32 a 16 px na svetlom aj tmavom podklade).

| # | Id | Popis | Čitateľnosť 16 px |
|---|---|---|---|
| 1 | `riso-white` | **predvolená** – papierovo biela dlaždica na celú plochu, modré W, ružový a žltý posun (riso) | dobrá |
| 2 | `riso-blue` | inverzia `riso-white`: modrá dlaždica, papierové W, ružový a žltý posun | dobrá |
| 3 | `tag-strokes` | W zo štyroch ťahov vo farbách tagov na atramentovej dlaždici | dobrá |
| 4 | `calendar-w` | celá dlaždica ako list kalendára: modrá hlavička s dierkami, riso W namiesto dátumu | dobrá |
| 5 | `pencil-w` | ceruzka vo farbách ikony (atrament, papier, žltá) píše W jedným ťahom na žltej dlaždici | dobrá |
| 6 | `wolf` | vlk v plochom geometrickom štýle (vzor „wolfdeck“) s papierovým obrysom, bez pozadia | dobrá |
| 7 | `wolf-pencil` | ten istý vlk bez obrysu nesie v papuli ceruzku, bez pozadia | stredná |
| 8 | `week-band` | týždenný pás na celú dlaždicu: minulý deň šrafovaný, dnešok s čiarkovaným rámom, budúci deň modrý | stredná |
| 9 | `notebook-calendar` | atramentová dlaždica: hore pás dní s dátumami, pod ním tri riadky zápisu | stredná |
| 10 | `calendar-page` | trhací kalendár na tyrkysovej dlaždici (listy, krúžky, riso „16“, ohnutý roh); ceruzka píše šestku | dobrá |
| 11 | `calendar-pencil` | 2×2 dni kalendára ako celá dlaždica, do prázdneho dňa ceruzka píše zoznam s odrážkami | dobrá |
| 12 | `wolf-ink` | znak vlka: jednofarebná papierová hlava na atramente, uši a čelo čítané ako „M“ | dobrá |
| 13 | `wolf-check` | zelené zaškrtávacie políčko zoznamu úloh s ušami, bez pozadia | dobrá |

Používateľ si ikonu okna a panela úloh vyberá v aplikácii: Nastavenia → Vzhľad → Ikona aplikácie
(`LocalSettings.AppIcon`, zoznam a poradie v `Theming/AppIcons.cs`; `.ico` a `.png` sa kopírujú do výstupu).
Staršie nastavenia majú id s číslom na začiatku (`5-wolf`) – `AppIcons.Normalize` číslo odstráni a ikonu,
ktorá už vo výbere nie je, nahradí predvolenou.

Predvolená je `riso-white`; generátor ju kopíruje aj do `Assets/WolfMark.ico` (ikona samotného `.exe`
súboru, `ApplicationIcon`, sa určuje pri builde) a `Assets/WolfMark-256.png` (ikona Desktop aplikácie).
Po zmene: `python3 tools/gen-icon-proposals.py` (alebo len vybrané: `python3 tools/gen-icon-proposals.py wolf riso-blue`).
Pri zmene predvolenej ikony upraviť `DEFAULT` v generátore a `AppIcons.Default`; pri pridaní ikony ju doplniť
do `ICONS` v generátore, do `AppIcons.All` a jej názov do `tools/gen-strings.py` (`Icon_*`).
