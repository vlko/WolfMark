# Návrhy ikony aplikácie

Generuje `tools/gen-icon-proposals.py` (čistý Python, fonty Barlow z `Assets/Fonts`). Pri každom návrhu
je PNG 256 px a hotové `.ico` (256, 64, 48, 32, 24, 16 px). Prehľad všetkých: `sheet.png`.

| # | Návrh | Popis | Čitateľnosť 16 px |
|---|---|---|---|
| 1 | `1-week-band` | týždenný pás v hranatých zátvorkách s dátumami: minulý deň šrafovaný, dnešok s čiarkovaným rámom | slabá |
| 2 | `2-riso-wm` | monogram WolfMark v riso štýle: W v pôvodnej veľkosti, za ním „M“ v 2/3 veľkosti (otočené W so skrátenými strednými nožičkami), zarovnané na spodnú hranu | dobrá |
| 3 | `3-notebook-calendar` | týždenný pás s dátumami nad tromi riadkami zápisu (čas, bodkovaná čiara, text) | slabá |
| 4 | `4-calendar-page` | trhací kalendár (listy, papierové krúžky, riso „16“, ohnutý roh vo farbe tagu); ceruzka z aplikácie píše šestku | dobrá |
| 5 | `5-wolf` | vlk v plochom geometrickom štýle (vzor „wolfdeck“), bez pozadia | dobrá |
| 6 | `6-calendar-pencil` | 2×2 dni kalendára na celú plochu, do prázdneho dňa ceruzka z aplikácie píše zoznam s odrážkami | dobrá |
| 7 | `7-wolf-mark` | Wolf + Mark: „M“ (otočené W so skrátenými strednými nožičkami) na celú výšku, hlava vlka (≈ 1/4 ikony, vlastné farby, riso posuny) pri spodku ľavej nožičky | dobrá |

Používateľ si ikonu okna a panela úloh vyberá v aplikácii: Nastavenia → Vzhľad → Ikona aplikácie
(`LocalSettings.AppIcon`, zoznam v `Theming/AppIcons.cs`; `.ico` a `?-*.png` sa kopírujú do výstupu).
Predvolená je `2-riso-wm`, ktorá je skopírovaná aj ako `Assets/WolfMark.ico` – ikona samotného `.exe`
súboru (`ApplicationIcon`) sa určuje pri builde. Po zmene návrhov: `python3 tools/gen-icon-proposals.py`
a pri zmene predvolenej ikony skopírovať jej `.ico` na `Assets/WolfMark.ico`.
