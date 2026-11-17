# ADR 0017 – Zdieľanie z Androidu: najprv lokálna sieť, inak OneDrive

- Stav: prijaté (2026-11-17, rozhodnutie autora)
- Backlog: M12

## Kontext
Z telefónu treba cez systémové „Zdieľať“ poslať odkaz, text alebo obrázok do poznámok (tag `Share`) a poznámky si
v telefóne prezerať. Poznámky ležia v OneDrive, tag je len v názve súboru (ADR 0010) a súbory sú zdroj pravdy –
kto vie do priečinka zapísať `YYYY/MM/DD/Share_HHmm.md`, vytvoril poznámku; desktop ju nájde watcherom alebo pri
štarte. Desktopové aplikácie doteraz nemali žiadny sieťový kód.

Autor chce doručenie okamžite, keď počítač beží v tej istej sieti, a spoľahlivo aj vtedy, keď nebeží.

## Rozhodnutie
- **Dve cesty, o ktorých rozhoduje telefón.** Na Wi-Fi skúsi počítač priamo (spojenie do ~1,5 s); keď sa spojenie
  nenadviaže, nahrá poznámku cez Microsoft Graph rovno na jej miesto v priečinku poznámok. Bez siete ostáva zdieľanie
  vo fronte telefónu. Žiadny „inbox“, žiadne spracovanie na počítači: cesta cez OneDrive funguje aj s vypnutým
  WolfMarkom a s desktopom, ktorý o zdieľaní nič nevie.
- **Príjem na počítači je vypnutý, kým ho používateľ nezapne** (Nastavenia → Zdieľanie z telefónu). Až potom
  aplikácia počúva na porte (predvolene 47821).
- **Párovanie a hľadanie počítača cez priečinok poznámok.** Desktop zapíše `.wolfmark/share/<id>.json`: meno
  počítača, IPv4 adresy, port a náhodný 256-bitový kľúč. Telefón súbor prečíta cez Graph a pamätá si ho. Kto súbor
  vie prečítať, vie prečítať aj poznámky, takže kľúč nepridáva nové tajomstvo, ktoré by bolo treba chrániť inak.
  Súbor sa prepíše len pri zmene (adresy po zmene siete, nový kľúč); po vypnutí príjmu sa zmaže.
- **Vlastný rámec nad TCP, šifrovaný AES-256-GCM** (`ShareFrame`): `"WMS1" | druh | dĺžka | nonce | šifrovaný text |
  tag`, hlavička je overená ako pridružené dáta. Jedno spojenie = jedna požiadavka a jedna odpoveď. Kto nemá kľúč,
  nedostane žiadnu odpoveď. Stačí `TcpListener` a `AesGcm` z BCL – žiadny nový balík, nič pre AOT.
- **Obsah skladá telefón**, rovnaký pre obe cesty: hotový Markdown bez front matter. Pri priamej ceste idú obrázky
  ako prílohy a v texte sú zástupné odkazy `wolfmark-attachment:N`; desktop ich uloží do `assets` dňa
  (`SaveAssetAsync`) a odkazy doplní. Prijímajú sa len typy obrázkov, ktoré aplikácie zobrazujú.
- **Názov dáva ten, kto súbor vytvára.** Na počítači `DirectoryEngine.CreateAsync` (bežné kolízne pravidlá), cez
  Graph telefón s `conflictBehavior=fail` a rovnakým poradím (`HHmm` → `HHmmss` → `-2`, `-3`…). Deň a čas sú
  hodiny telefónu v okamihu zdieľania.
- **Duplicity:** každé zdieľanie má UUID. Desktop si pamätá posledných 256 a na opakovanie odpovie „už mám“.
  Telefón prejde na OneDrive len vtedy, keď sa spojenie nenadviazalo, alebo keď počítač odpovedal odmietnutím.
- **Prehrané rámce:** rámec nesie čas odoslania; desktop odmietne rámec starší alebo novší než 10 minút oproti
  svojim hodinám (telefón ho potom doručí cez OneDrive).
- **Spoločné testovacie vektory** `testdata/share-frames.json` (vyrobené nezávislou implementáciou AES-GCM)
  a `testdata/note-names.json` kontroluje xUnit aj testy Android aplikácie.
- **Android aplikácia** je Kotlin + Jetpack Compose v `android/` (rýchly štart pri zdieľaní, oficiálny MSAL);
  pravidlá názvov a protokol sú prenesené do Kotlinu a držané pri sebe spoločnými vektormi.

## Dôsledky
- SPEC §8 „žiadne sieťové volania“ má novú voliteľnú výnimku: po zapnutí aplikácia prijíma spojenia z lokálnej
  siete. Windows sa pri prvom počúvaní opýta na bránu firewall.
- Kľúč leží v OneDrive v čitateľnej podobe. Je to vedomé: chráni pred cudzími zariadeniami v sieti, nie pred tým,
  kto má prístup k poznámkam.
- Po reštarte aplikácie je pamäť prijatých UUID prázdna – rámec zachytený v sieti by sa dal do 10 minút prehrať
  a vznikla by druhá kópia tej istej poznámky. Nič iné tým útočník nezíska; prijaté riziko.
- Neoverený volajúci môže obsadiť najviac 4 spojenia na 60 s; telo rámca sa číta do pamäte, ktorá rastie len
  s tým, čo naozaj prišlo (limit 32 MB).
- Adresy sú všetky IPv4 adresy aktívnych rozhraní, teda aj VPN (Tailscale, ZeroTier): mimo domu to funguje bez
  ďalšieho kódu, ak používateľ takú sieť má.
- Poznámky musia byť v OneDrive, inak telefón súbor s adresou nenájde (a nemá kam zapisovať).
- Osirelé obrázky po neúspešnom vytvorení poznámky sa neriešia (ako v ADR 0016).

## Zvažované alternatívy
- **QR kód + mDNS/DNS-SD** (pôvodný návrh): QR knižnica v dvoch UI, skener v telefóne a multicast, ktorý siete
  často blokujú. Súbor v OneDrive rieši párovanie aj hľadanie naraz.
- **Kestrel / HTTP + TLS s pripnutým certifikátom:** ASP.NET Core vo WinUI aj Avalonia aplikácii pod AOT, správa
  certifikátov, na Androide výnimky pre nešifrovanú prevádzku. Pre jednu správu tam a späť zbytočné.
- **Relay v cloude** (ntfy, Web PubSub): funguje všade bez VPN, ale pridáva službu, o ktorú sa treba starať, a obsah
  ide cez cudzí server. OneDrive už sprostredkovateľom je.
- **Len OneDrive:** najmenej kódu; odmietnuté kvôli oneskoreniu synchronizácie na počítači.
- **Inbox priečinok spracovaný desktopom:** zbytočný medzikrok – názov súboru je celý kontrakt.
- **.NET for Android / MAUI:** zdieľal by `WolfMark.Core`, ale štartuje pomalšie, čo je pri zdieľaní cítiť.
