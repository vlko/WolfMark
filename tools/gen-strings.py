# Generates src/WolfMark.App/Strings/*/Resources.resw, R.cs + the English fallback in src/WolfMark.Presentation
# and the Slovak table of src/WolfMark.Desktop.
# Usage: python3 tools/gen-strings.py src/WolfMark.App/Strings
# key: (en, sk)
S = {
 "AppTitle": ("WolfMark", "WolfMark"),
 "Search_Placeholder": ("Search…   #tag   @2026-10", "Hľadať…   #tag   @2026-10"),
 "Search_NoResults": ("No results", "Žiadne výsledky"),
 "Search_Semantic": ("similar", "podobné"),
 "Search_Hint": ("↑↓ to move · Enter to open · Esc to close", "↑↓ výber · Enter otvoriť · Esc zavrieť"),
 "Toolbar_NewNote": ("New note (Ctrl+N)", "Nová poznámka (Ctrl+N)"),
 "Toolbar_Calendar": ("Calendar (Ctrl+Shift+C)", "Kalendár (Ctrl+Shift+C)"),
 "Toolbar_Search": ("Search (Ctrl+K)", "Hľadať (Ctrl+K)"),
 "Toolbar_Settings": ("Settings", "Nastavenia"),
 "Toolbar_PreviousWeek": ("Previous week", "Predchádzajúci týždeň"),
 "Toolbar_NextWeek": ("Next week", "Nasledujúci týždeň"),
 "Mode_Raw": ("Raw", "Raw"),
 "Mode_Formatted": ("Formatted", "Formát"),
 "Mode_Tooltip": ("Switch editor mode (Ctrl+E)", "Prepnúť režim editora (Ctrl+E)"),
 "Tag_None": ("No tag", "Bez tagu"),
 "Tag_NewPlaceholder": ("Tag name, Enter to apply", "Názov tagu, Enter potvrdí"),
 "Tag_Invalid": ("Letters, digits and '-' only", "Len písmená, číslice a '-'"),
 "Tag_Color": ("Tag colour", "Farba tagu"),
 "Tag_ShowLabels": ("Show titles in calendar", "Zobraziť titulky v kalendári"),
 "Save_Saved": ("Saved", "Uložené"),
 "Save_Saving": ("Saving…", "Ukladá sa…"),
 "Save_Pending": ("Edited", "Upravené"),
 "Save_Error": ("Save failed", "Chyba ukladania"),
 "Save_New": ("New – created on first keystroke", "Nová – vznikne prvým znakom"),
 "Save_ReadOnly": ("Read only", "Len na čítanie"),
 "Editor_Placeholder": ("Start writing…", "Začni písať…"),
 "ReadOnly_ClaudeTitle": ("Imported from Claude Code", "Importované z Claude Code"),
 "ReadOnly_ClaudeMessage": ("The sync regenerates this note, so it is read-only.", "Poznámku prepisuje synchronizácia, preto je len na čítanie."),
 "ReadOnly_CopyButton": ("Copy to my note", "Skopírovať do vlastnej poznámky"),
 "External_Title": ("Changed outside the app", "Zmenené mimo aplikácie"),
 "External_Message": ("The file changed on disk while you were editing.", "Súbor sa zmenil na disku počas tvojich úprav."),
 "External_Reload": ("Load from disk", "Načítať z disku"),
 "External_Keep": ("Keep mine", "Ponechať moje"),
 "Deleted_Title": ("Note was deleted", "Poznámka bola zmazaná"),
 "Deleted_Message": ("The file no longer exists. Typing will create it again.", "Súbor už neexistuje. Ďalšou úpravou sa vytvorí znova."),
 "Conflict_Title": ("OneDrive conflict copy", "Konfliktná kópia OneDrive"),
 "Conflict_Message": ("This is a conflicting copy of {0}.", "Toto je konfliktná kópia súboru {0}."),
 "Conflict_OpenOriginal": ("Open original", "Otvoriť originál"),
 "Conflict_Discard": ("Move copy to Recycle Bin", "Presunúť kópiu do koša"),
 "Conflicts_Count": ("OneDrive conflicts: {0}", "Konflikty OneDrive: {0}"),
 "NonStandard_Title": ("Non-standard file name", "Neštandardný názov súboru"),
 "NonStandard_Message": ("Time is taken from the file; tag cannot be changed.", "Čas je podľa súboru; tag sa nedá zmeniť."),
 "History_Title": ("HISTORY", "HISTÓRIA"),
 "History_Today": ("TODAY", "DNES"),
 "History_Yesterday": ("YESTERDAY", "VČERA"),
 "History_Empty": ("No notes here yet", "Zatiaľ tu nie sú poznámky"),
 "History_OpenTasks": ("Open tasks", "Nedokončené"),
 "History_ClearFilter": ("Clear filter", "Zrušiť filter"),
 "Menu_ChangeTag": ("Change tag", "Zmeniť tag"),
 "Menu_OpenInExplorer": ("Show in Explorer", "Zobraziť v Prieskumníkovi"),
 "Menu_CopyLink": ("Copy link", "Kopírovať odkaz"),
 "Menu_Delete": ("Move to Recycle Bin", "Presunúť do koša"),
 "Delete_ConfirmTitle": ("Move this note to the Recycle Bin?", "Presunúť poznámku do koša?"),
 "Common_Cancel": ("Cancel", "Zrušiť"),
 "Common_Save": ("Save", "Uložiť"),
 "Common_Close": ("Close", "Zavrieť"),
 "Common_Today": ("Today", "Dnes"),
 "Common_Yes": ("Yes", "Áno"),
 "Calendar_Year": ("Year", "Rok"),
 "Calendar_Month": ("Month", "Mesiac"),
 "Calendar_Close": ("Close calendar (Esc)", "Zavrieť kalendár (Esc)"),
 "Calendar_AllTags": ("All tags", "Všetky tagy"),
 "Calendar_Previous": ("Previous", "Predchádzajúci"),
 "Calendar_Next": ("Next", "Nasledujúci"),
 "Notes_One": ("{0} note", "{0} poznámka"),
 "Notes_Few": ("{0} notes", "{0} poznámky"),
 "Notes_Many": ("{0} notes", "{0} poznámok"),
 "Notes_None": ("no notes", "bez poznámok"),
 "Untagged": ("untagged", "bez tagu"),
 "Indexing_Progress": ("Indexing {0}/{1}", "Indexujem {0}/{1}"),
 "Index_Ready": ("Index ready", "Index pripravený"),
 "Claude_CleanupTitle": ("Claude Code deletes old transcripts", "Claude Code maže staré prepisy"),
 "Claude_CleanupMessage": ("cleanupPeriodDays is {0}; older prompts cannot be imported.", "cleanupPeriodDays je {0}; staršie prompty sa nedajú naimportovať."),
 "Claude_CleanupFix": ("Keep 365 days", "Uchovávať 365 dní"),
 "Settings_Title": ("Settings", "Nastavenia"),
 "Settings_Notes": ("Notes folder", "Priečinok poznámok"),
 "Settings_Browse": ("Change…", "Zmeniť…"),
 "Settings_MoveTitle": ("Change notes folder", "Zmena priečinka poznámok"),
 "Settings_MoveMessage": ("Move existing notes to the new folder, or just switch to it?", "Presunúť existujúce poznámky do nového priečinka, alebo len prepnúť?"),
 "Settings_Move": ("Move notes", "Presunúť poznámky"),
 "Settings_Switch": ("Just switch", "Len prepnúť"),
 "Settings_RestartRequired": ("Restart the app to apply this change.", "Zmena sa prejaví po reštarte aplikácie."),
 "Settings_Restart": ("Restart now", "Reštartovať"),
 "Settings_Appearance": ("Appearance", "Vzhľad"),
 "Settings_Theme": ("Theme", "Téma"),
 "Settings_ThemeSystem": ("System", "Systém"),
 "Settings_ThemeLight": ("Light", "Svetlá"),
 "Settings_ThemeDark": ("Dark", "Tmavá"),
 "Settings_Language": ("Language", "Jazyk"),
 "Settings_LanguageSystem": ("System", "Podľa systému"),
 "Settings_Editor": ("Editor", "Editor"),
 "Settings_FontFamily": ("Font", "Písmo"),
 "Editor_DropImage": ("Insert image", "Vložiť obrázok"),
 "Editor_OpenImage": ("Double-click to open", "Dvojklikom otvoríte"),
 "Settings_Monospace": ("Monospaced font for all text", "Písmo s pevnou šírkou pre celý text"),
 "Settings_LineNumbers": ("Line numbers", "Čísla riadkov"),
 "Settings_MonoFont": ("Monospaced font", "Písmo s pevnou šírkou"),
 "MonoFont_Filter": ("Filter fonts", "Filtrovať písma"),
 "MonoFont_Loading": ("Loading installed fonts…", "Načítavam nainštalované písma…"),
 "Settings_FontSize": ("Font size (Ctrl + / Ctrl −)", "Veľkosť písma (Ctrl + / Ctrl −)"),
 "Settings_ColorEmoji": ("Colour emoji", "Farebné emotikony"),
 "Settings_DefaultMode": ("Default mode", "Predvolený režim"),
 "Settings_ClaudeCode": ("Claude Code import", "Import z Claude Code"),
 "Settings_ClaudeEnabled": ("Import prompts", "Importovať prompty"),
 "Settings_ClaudePath": (".claude folder (empty = default)", "Priečinok .claude (prázdne = predvolený)"),
 "Settings_ClaudeExcluded": ("Excluded projects – one path prefix per line", "Vylúčené projekty – jedna cesta na riadok"),
 "Settings_ClaudeRedact": ("Redact secrets (API keys, passwords)", "Skryť citlivé údaje (API kľúče, heslá)"),
 "Settings_ClaudeInterval": ("Sync interval (minutes)", "Interval synchronizácie (minúty)"),
 "Settings_ClaudeWatch": ("Watch transcripts in real time", "Sledovať prepisy v reálnom čase"),
 "Settings_ClaudeSyncNow": ("Sync now", "Synchronizovať teraz"),
 "Settings_Index": ("Index", "Index"),
 "Settings_IndexRebuild": ("Rebuild index", "Znovu postaviť index"),
 "Settings_IndexSemanticOn": ("Semantic search: on", "Sémantické vyhľadávanie: zapnuté"),
 "Settings_IndexSemanticOff": ("Semantic search: off – no embedding model installed", "Sémantické vyhľadávanie: vypnuté – chýba embedding model"),
 "Settings_Tags": ("Tags", "Tagy"),
 "Settings_Saved": ("Settings saved", "Nastavenia uložené"),
 "Status_Synced": ("Claude Code: {0} new, {1} updated", "Claude Code: {0} nových, {1} aktualizovaných"),
 "Week_Label": ("Week {0}", "Týždeň {0}"),
 "Calendar_Expand": ("Show the whole month (Ctrl+Shift+C)", "Zobraziť celý mesiac (Ctrl+Shift+C)"),
 "Calendar_Collapse": ("Show the week (Ctrl+Shift+C)", "Zobraziť týždeň (Ctrl+Shift+C)"),
 "Rail_All": ("All", "Všetko"),
 "Rail_Show": ("Show all tags", "Zobraziť všetky tagy"),
 "Editor_Modified": ("edited {0}", "upravené {0}"),
 "FullView_Enter": ("Full view (F11)", "Celé okno (F11)"),
 "FullView_Exit": ("Leave full view (F11, Esc)", "Ukončiť celé okno (F11, Esc)"),
 "FullView_EnterMac": ("Full view (⌃⌘F)", "Celé okno (⌃⌘F)"),
 "FullView_ExitMac": ("Leave full view (⌃⌘F, Esc)", "Ukončiť celé okno (⌃⌘F, Esc)"),
 "FullView_Title": ("Full view", "Celé okno"),
 "FullView_Created": ("created {0}", "vytvorené {0}"),
 "Editor_Continues": ("continues", "pokračuje"),
 "Toolbar_NewNoteShort": ("New", "Nová"),
 "Menu_Cut": ("Cut", "Vystrihnúť"),
 "Menu_Copy": ("Copy", "Kopírovať"),
 "Menu_Paste": ("Paste", "Prilepiť"),
 "Menu_SelectAll": ("Select all", "Vybrať všetko"),
 "Format_Bold": ("Bold (Ctrl+B)", "Tučné (Ctrl+B)"),
 "Format_Italic": ("Italic (Ctrl+I)", "Kurzíva (Ctrl+I)"),
 "Format_Strike": ("Strikethrough", "Prečiarknuté"),
 "Format_Code": ("Code", "Kód"),
 "Format_Link": ("Link (select text + Ctrl+K)", "Odkaz (označ text + Ctrl+K)"),
 "Format_Task": ("Task (Ctrl+Shift+L)", "Úloha (Ctrl+Shift+L)"),
 "Format_Paragraph": ("Paragraph", "Odsek"),
 "Format_H1": ("Heading 1", "Nadpis 1"),
 "Format_H2": ("Heading 2", "Nadpis 2"),
 "Format_H3": ("Heading 3", "Nadpis 3"),
 "Format_Quote": ("Quote", "Citácia"),
 "Format_Bullet": ("Bulleted list", "Odrážkový zoznam"),
 "Format_Numbered": ("Numbered list", "Číslovaný zoznam"),
 "Proofing_Toggle": ("Spell check", "Kontrola pravopisu"),
 "Spell_AddToDictionary": ("Add to dictionary", "Pridať do slovníka"),
 "Spell_Ignore": ("Ignore", "Ignorovať"),
 "Spell_NoSuggestions": ("No suggestions", "Žiadne návrhy"),
 "History_Created": ("created {0}", "vznik {0}"),
 "Copy_Plain": ("Copy as plain text", "Kopírovať ako text"),
 "Copy_Html": ("Copy as HTML", "Kopírovať ako HTML"),
 "Status_ClaudeOff": ("Claude Code: off", "Claude Code: vypnuté"),
 "Status_ClaudeMissing": ("Claude Code: .claude folder not found", "Claude Code: priečinok .claude neexistuje"),
 "Status_ClaudeWaiting": ("Claude Code: waiting for the first sync", "Claude Code: čaká na prvú synchronizáciu"),
 "Status_ClaudeSynced": ("Claude Code · {0}", "Claude Code · {0}"),
 "Status_ClaudeChanges": ("{0} new, {1} updated", "{0} nových, {1} aktualizovaných"),
 "Status_ClaudeFailed": ("Claude Code: sync failed at {0}", "Claude Code: synchronizácia zlyhala o {0}"),
 "Status_ClaudeSyncNow": ("Sync now", "Synchronizovať teraz"),
 "Status_NotesFolder": ("Notes folder", "Priečinok poznámok"),
 "Settings_Diagnostics": ("Diagnostics", "Diagnostika"),
 "Settings_CopyDiagnostics": ("Copy diagnostic info", "Kopírovať diagnostické údaje"),
 "Settings_TestNotice": ("Show test notice", "Zobraziť testovacie upozornenie"),
 "TestNotice_Title": ("Test notice", "Testovacie upozornenie"),
 "TestNotice_Message": ("Notices appear here, above the status bar. Close it with ✕.", "Upozornenia sa zobrazujú tu, nad stavovou lištou. Zavrieš ho cez ✕."),
 "TestNotice_Action": ("Action", "Akcia"),
 "Settings_WindowRendering": ("Window rendering (restart)", "Vykresľovanie okna (reštart)"),
 "Settings_RenderingMica": ("Mica under the paper", "Mica pod papierom"),
 "Settings_RenderingClass": ("No backdrop + window background", "Bez backdropu + pozadie okna"),
 "Settings_AppIcon": ("App icon", "Ikona aplikácie"),
 "Icon_WeekBand": ("Week band", "Týždenný pás"),
 "Icon_RisoWm": ("WolfMark (W + M)", "WolfMark (W + M)"),
 "Icon_NotebookCalendar": ("Notebook with calendar", "Zápisník s kalendárom"),
 "Icon_CalendarPage": ("Tear-off calendar", "Trhací kalendár"),
 "Icon_Wolf": ("Wolf", "Vlk"),
 "Icon_CalendarPencil": ("Calendar with pencil", "Kalendár s ceruzkou"),
 "Icon_WolfMark": ("Wolf + Mark", "Wolf + Mark"),
 "Error_Title": ("Something went wrong", "Niečo sa pokazilo"),
 "Error_Settings": ("Settings could not be opened: {0}", "Nastavenia sa nepodarilo otvoriť: {0}"),
 "Week_NoTag": ("No tag", "No tag"),
 "Settings_System": ("Windows", "Windows"),
 "Settings_StartWithWindows": ("Start with Windows", "Spustiť pri štarte Windows"),
 "Settings_CloseToTray": ("Closing the window keeps WolfMark running in the notification area", "Zatvorenie okna nechá WolfMark bežať v oblasti oznámení"),
 "Settings_StartupFailed": ("Start with Windows could not be changed: {0}", "Spúšťanie pri štarte Windows sa nepodarilo zmeniť: {0}"),
 "Tray_Show": ("Show WolfMark", "Zobraziť WolfMark"),
 "Tray_Settings": ("Settings…", "Nastavenia…"),
 "Tray_Exit": ("Exit", "Ukončiť"),
}

import os, sys, html
out = sys.argv[1]
# R.cs and the fallback are shared by the Windows and the macOS/Linux app.
SHARED = os.path.join(out, "..", "..", "WolfMark.Presentation", "Infrastructure")
for lang, idx in (("en-US", 0), ("sk-SK", 1)):
    rows = "\n".join(
        f'  <data name="{k}" xml:space="preserve">\n    <value>{html.escape(v[idx], quote=False)}</value>\n  </data>' for k, v in S.items())
    xml = f'''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
{rows}
</root>
'''
    os.makedirs(os.path.join(out, lang), exist_ok=True)
    open(os.path.join(out, lang, "Resources.resw"), "w", encoding="utf-8").write(xml)

# Keys class
keys = "\n".join(f"    public const string {k} = nameof({k});" for k in S)
cs = f'''namespace WolfMark.App.Infrastructure;

/// <summary>Resource keys of <c>Strings/*/Resources.resw</c> (generated from one table so sk/en stay in sync).</summary>
internal static class R
{{
{keys}
}}
'''
open(os.path.join(SHARED, "R.cs"), "w", encoding="utf-8").write(cs)
# English fallback (used when resources.pri is missing, e.g. tests/dev hosts)
fallback = "\n".join(f'        [R.{k}] = "{v[0].replace(chr(92), chr(92)*2).replace(chr(34), chr(92)+chr(34))}",' for k, v in S.items())
cs2 = f'''namespace WolfMark.App.Infrastructure;

internal static partial class Strings
{{
    private static readonly Dictionary<string, string> Fallback = new(StringComparer.Ordinal)
    {{
{fallback}
    }};
}}
'''
open(os.path.join(SHARED, "Strings.Fallback.cs"), "w", encoding="utf-8").write(cs2)
# Slovak table of the macOS/Linux app (no .resw/.pri off Windows; English comes from the fallback).
desktop = os.path.join(out, "..", "..", "WolfMark.Desktop", "Infrastructure")
if os.path.isdir(desktop):
    def lit(v):
        return v.replace(chr(92), chr(92)*2).replace(chr(34), chr(92)+chr(34))
    sk = "\n".join(f'        [R.{k}] = "{lit(v[1])}",' for k, v in S.items())
    cs3 = f'''using WolfMark.App.Infrastructure;

namespace WolfMark.Desktop.Infrastructure;

internal static partial class DesktopStrings
{{
    private static readonly Dictionary<string, string> Slovak = new(StringComparer.Ordinal)
    {{
{sk}
    }};
}}
'''
    open(os.path.join(desktop, "Strings.Slovak.cs"), "w", encoding="utf-8").write(cs3)
print(len(S), "strings")
