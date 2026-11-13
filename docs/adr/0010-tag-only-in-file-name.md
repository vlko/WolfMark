# ADR 0010 – Tag len v názve súboru

## Kontext
Podľa pôvodnej SPEC §3.4 niesol názov súboru primárny tag a ďalšie tagy sa zbierali z textu (`#slovo`) a z front
matter (`tags: [...]`). Filter histórie, vyhľadávanie `#tag` a výber tagu v hlavičke pracovali so všetkými.

V praxi `#` v texte často tag neznamená: číslo týždňa (`#39 https://…`), PR alebo issue (`#42`), `#TODO`, `C#`.
Výber tagu potom ponúkal tagy, ktoré používateľ nikdy nevytvoril a ktoré nevidel v lište ani v kalendári. Tag z textu
sa navyše nedá premenovať (M8-05) bez úpravy obsahu poznámok.

## Rozhodnutie
- **Poznámka má najviac jeden tag – ten v názve súboru** (`praca_1403.md`). Text ani front matter tagy nenesú.
- `NoteContentAnalyzer` tagy nezbiera, `note_tags` obsahuje len tag z názvu.
- **`#slovo` a `#39` v texte editor ďalej farebne zvýrazňuje** ako značky v rámci poznámky (token `Hashtag`, farba
  podľa názvu ako pri tagu). Sú to len značky na orientáciu v súbore – nezobrazujú sa vo výbere tagu, v lište, vo
  filtroch ani v kalendári.
- **Existujúci index** sa nevytvára nanovo (zmena verzie schémy by prepočítala aj embeddingy): pri otvorení sa z
  `note_tags` zmažú riadky, ktoré nezodpovedajú `primary_tag_key` poznámky. Je to idempotentné a lacné.
- Fulltext ďalej nájde `#slovo` ako bežné slovo v texte.

## Dôsledky
- Viac tém na jednu poznámku sa nedá vyjadriť tagom; ostáva fulltext (slovo v texte).
- Premenovanie a zlúčenie tagov mení len názvy súborov a `tags.json` – obsah poznámok sa nikdy neupravuje.
