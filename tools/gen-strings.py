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
 "History_External": ("external", "externý"),
 "Menu_Open": ("Open", "Otvoriť"),
 "Menu_OpenLink": ("Open link in browser", "Otvoriť odkaz v prehliadači"),
 "Menu_OpenFolder": ("Open folder", "Otvoriť priečinok"),
 "Menu_RemoveFromHistory": ("Remove from history", "Odstrániť z histórie"),
 "ExtFile_Created": ("Created {0}", "Vytvorený {0}"),
 "ExtFile_Modified": ("Modified {0}", "Zmenený {0}"),
 "ExtFile_Words": ("{0} words", "{0} slov"),
 "ExtFile_OpenFailed": ("{0} could not be opened", "Súbor {0} sa nepodarilo otvoriť"),
 "ExtFile_SaveFailed": ("{0} could not be saved", "Súbor {0} sa nepodarilo uložiť"),
 "ExtFile_Missing": ("The file {0} does not exist", "Súbor {0} neexistuje"),
 "ExtFile_Save": ("Save (Ctrl+S)", "Uložiť (Ctrl+S)"),
 "ExtFile_SaveMac": ("Save (⌘S)", "Uložiť (⌘S)"),
 "ExtFile_Badge": ("External file · not saved on its own", "Externý súbor · neukladá sa sám"),
 "ExtFile_CopyMessage": ("This file is outside your notes folder. A copy in today's notes behaves like every note: it saves on its own, has a tag and shows in the calendar. The original file stays as it is.", "Tento súbor je mimo priečinka poznámok. Kópia v dnešných poznámkach sa správa ako každá poznámka: ukladá sa sama, má tag a je v kalendári. Pôvodný súbor zostane tak, ako je."),
 "ExtFile_CopyButton": ("Copy to my notes", "Skopírovať do mojich poznámok"),
 "ExtFile_CloseTitle": ("Save changes to {0}?", "Uložiť zmeny v súbore {0}?"),
 "ExtFile_DontSave": ("Don't save", "Neuložiť"),
 "ExtFile_DeletedTitle": ("The file was deleted or moved", "Súbor bol zmazaný alebo presunutý"),
 "ExtFile_DeletedMessage": ("Save writes it again to the same place.", "Uložením sa zapíše znova na to isté miesto."),
 "Settings_Files": ("Markdown files", "Markdown súbory"),
 "Settings_General": ("General", "Všeobecné"),
 "Settings_IndexDiagnostics": ("Index & diagnostics", "Index a diagnostika"),
 "Settings_FileAssociation": ("Open .md files in WolfMark", "Otvárať .md súbory vo WolfMark"),
 "Settings_FileAssociationHint": ("Windows asks you to confirm: choose WolfMark for .md in Default apps.", "Windows chce voľbu potvrdiť: v Predvolených aplikáciách vyber pre .md WolfMark."),
 "Settings_FileAssociationHintDesktop": ("WolfMark becomes the default app for Markdown files.", "WolfMark sa stane predvolenou aplikáciou pre Markdown súbory."),
 "Settings_FileAssociationDefault": ("Default apps…", "Predvolené aplikácie…"),
 "Settings_FileAssociationFailed": ("The .md file association could not be changed: {0}", "Priradenie .md súborov sa nepodarilo zmeniť: {0}"),
 "Settings_Portable": ("Portable mode", "Prenosný režim"),
 "Settings_PortableTitle": ("Portable", "Prenosná verzia"),
 "Settings_PortableOff": ("Off – the data is stored in your user profile.", "Vypnutý – dáta sú uložené v profile používateľa."),
 "Settings_PortableOn": ("On – all data is in {0}", "Zapnutý – všetky dáta sú v {0}"),
 "Settings_PortableEnable": ("Switch to portable mode…", "Prepnúť na prenosný režim…"),
 "Settings_PortableDisable": ("Leave portable mode…", "Ukončiť prenosný režim…"),
 "Settings_PortableEnableWarning": (
  "Notes, settings and the search index are copied next to the app:\n{0}\n\n"
  "• From then on the app reads and writes only there. With Copy the original notes stay where they are but are no longer updated – you will have two diverging copies.\n"
  "• With Move the original notes folder goes to the Recycle Bin once the copy is verified. The settings in your user profile stay.\n"
  "• Notes in the portable folder are not synchronised by OneDrive.\n"
  "• Start at sign-in and the .md file association are removed – they are not available in portable mode.\n"
  "• Deleted notes go to .wolfmark/trash in the portable folder, not to the system Recycle Bin.\n"
  "• Deleting WolfMark.portable.json next to the app returns to the previous data, without the changes made in portable mode.\n\n"
  "The app restarts afterwards.",
  "Poznámky, nastavenia a index vyhľadávania sa skopírujú vedľa aplikácie:\n{0}\n\n"
  "• Odvtedy aplikácia číta a zapisuje len tam. Pri Kopírovať pôvodné poznámky ostanú na mieste, ale už sa nebudú aktualizovať – vzniknú dve rozbiehajúce sa kópie.\n"
  "• Pri Presunúť pôjde pôvodný priečinok poznámok po overení kópie do koša. Nastavenia v profile používateľa ostanú.\n"
  "• Poznámky v prenosnom priečinku nesynchronizuje OneDrive.\n"
  "• Spúšťanie po prihlásení a priradenie .md súborov sa zrušia – v prenosnom režime nie sú dostupné.\n"
  "• Zmazané poznámky idú do .wolfmark/trash v prenosnom priečinku, nie do systémového koša.\n"
  "• Zmazaním WolfMark.portable.json vedľa aplikácie sa vrátiš k pôvodným dátam, bez zmien z prenosného režimu.\n\n"
  "Aplikácia sa potom reštartuje."),
 "Settings_PortableDisableWarning": (
  "Notes, settings and the search index are copied back to this computer. Notes:\n{0}\n\n"
  "• A note that differs in that folder goes to the Recycle Bin and the portable version takes its place. Notes deleted in portable mode stay there.\n"
  "• The portable folder {1} is kept – delete it yourself when you no longer need it.\n"
  "• Start at sign-in and the .md file association stay off; you can switch them on again.\n\n"
  "The app restarts afterwards.",
  "Poznámky, nastavenia a index vyhľadávania sa skopírujú späť do tohto počítača. Poznámky:\n{0}\n\n"
  "• Poznámka, ktorá sa v tom priečinku líši, pôjde do koša a nahradí ju verzia z prenosného režimu. Poznámky zmazané v prenosnom režime tam ostanú.\n"
  "• Prenosný priečinok {1} ostane – zmaž ho sám, keď ho už nebudeš potrebovať.\n"
  "• Spúšťanie po prihlásení a priradenie .md súborov ostanú vypnuté; môžeš ich znova zapnúť.\n\n"
  "Aplikácia sa potom reštartuje."),
 "Settings_PortableCopy": ("Copy", "Kopírovať"),
 "Settings_PortableMove": ("Move", "Presunúť"),
 "Settings_PortableCopyBack": ("Copy back", "Skopírovať späť"),
 "Settings_PortableProgress": ("Copying… {0} files", "Kopíruje sa… {0} súborov"),
 "Settings_PortableFailed": ("Portable mode could not be switched: {0}", "Prenosný režim sa nepodarilo prepnúť: {0}"),
 "Settings_PortableCleanupFailed": ("Portable mode is on, but the original notes could not be moved to the Recycle Bin: {0}", "Prenosný režim je zapnutý, ale pôvodné poznámky sa nepodarilo presunúť do koša: {0}"),
 "Settings_PortableNotWritable": ("Not available: the app folder {0} is not writable. Copy the app to a folder you can write to, e.g. a USB drive.", "Nedostupné: do priečinka aplikácie {0} sa nedá zapisovať. Skopíruj aplikáciu do zapisovateľného priečinka, napr. na USB kľúč."),
 "Settings_PortableAppBundle": ("Not available for an app installed in Applications.", "Nedostupné pre aplikáciu nainštalovanú v Applications."),
 "Settings_PortableUnavailable": ("Not available in portable mode – it writes to this computer's system settings.", "V prenosnom režime nie je dostupné – zapisuje do systému tohto počítača."),
 "Settings_TagsIntro": ("A note's tag is in its file name. Renaming a tag renames the files of all its notes; the text of the notes is not changed.", "Tag poznámky je v názve jej súboru. Premenovanie tagu premenuje súbory všetkých jeho poznámok; text poznámok sa nemení."),
 "Settings_TagsNone": ("No tags yet.", "Zatiaľ žiadne tagy."),
 "Settings_TagsRename": ("Rename", "Premenovať"),
 "Settings_TagsSame": ("The new name is the same as the old one.", "Nový názov je rovnaký ako pôvodný."),
 "Settings_TagsReserved": ("#ClaudeCode belongs to the Claude Code sync and cannot be renamed.", "#ClaudeCode patrí synchronizácii Claude Code, nedá sa premenovať."),
 "Settings_TagsRenameWarning": ("Notes with #{0} ({2}) will be renamed to #{1}, e.g. {0}_1403.md → {1}_1403.md. The colour stays. If a file cannot be renamed, the notes already renamed go back.", "Poznámky s #{0} ({2}) sa premenujú na #{1}, napr. {0}_1403.md → {1}_1403.md. Farba ostane. Ak sa niektorý súbor nedá premenovať, už premenované poznámky sa vrátia späť."),
 "Settings_TagsMergeWarning": ("#{1} already exists ({3}). The notes with #{0} ({2}) will join it and take its colour; #{0} disappears. If a file cannot be renamed, the notes already renamed go back.", "#{1} už existuje ({3}). Poznámky s #{0} ({2}) sa k nemu pridajú a dostanú jeho farbu; #{0} zmizne. Ak sa niektorý súbor nedá premenovať, už premenované poznámky sa vrátia späť."),
 "Settings_TagsNewPlaceholder": ("New tag", "Nový tag"),
 "Settings_TagsAdd": ("Add", "Pridať"),
 "Settings_TagsExists": ("#{0} already exists.", "#{0} už existuje."),
 "Settings_TagsAdded": ("#{0} added – pick it for a note in the tag picker.", "#{0} je pridaný – poznámke ho priradíš vo výbere tagu."),
 "Settings_TagsUnused": ("no notes", "bez poznámok"),
 "Settings_TagsMergeNote": ("Renaming a tag to one that already exists merges them: the notes get its name and colour.", "Premenovanie tagu na tag, ktorý už existuje, ich zlúči: poznámky dostanú jeho názov a farbu."),
 "Settings_TagsMenu": ("Colour and rename", "Farba a premenovanie"),
 "Settings_TagsRenameAction": ("Rename…", "Premenovať…"),
 "Settings_TagsShowNotes": ("Show notes", "Zobraziť poznámky"),
 "Settings_TagsDelete": ("Remove tag…", "Odstrániť tag…"),
 "Settings_TagsDeleteWarning": ("The notes with #{0} ({1}) stay, they only lose the tag ({0}_1403.md → 1403.md). Its colour is forgotten. If a file cannot be renamed, the notes already renamed go back.", "Poznámky s #{0} ({1}) ostanú, len stratia tag ({0}_1403.md → 1403.md). Farba tagu sa zabudne. Ak sa niektorý súbor nedá premenovať, už premenované poznámky sa vrátia späť."),
 "Settings_TagsDeleteUnused": ("#{0} has no notes; it disappears from the list and its colour is forgotten.", "#{0} nemá žiadne poznámky; zmizne zo zoznamu a jeho farba sa zabudne."),
 "Settings_TagsConfirmDelete": ("Remove", "Odstrániť"),
 "Settings_TagsDeleted": ("#{0} removed. Notes that lost the tag: {1}.", "#{0} je odstránený. Poznámky, ktoré stratili tag: {1}."),
 "Settings_TagsConfirmRename": ("Rename", "Premenovať"),
 "Settings_TagsConfirmMerge": ("Merge", "Zlúčiť"),
 "Settings_TagsProgress": ("Renamed: {0}", "Premenované: {0}"),
 "Settings_TagsDone": ("#{0} is now #{1}. Notes renamed: {2}.", "#{0} je teraz #{1}. Premenované poznámky: {2}."),
 "Settings_TagsFailed": ("Renaming failed: {0} The notes were renamed back.", "Premenovanie zlyhalo: {0} Poznámky sa vrátili späť."),
 "Settings_TagsRollbackFailed": ("Renaming failed: {0} Notes that could not be renamed back and keep #{2}: {1}. Details are in the log.", "Premenovanie zlyhalo: {0} Poznámky, ktoré sa nepodarilo vrátiť a majú #{2}: {1}. Podrobnosti sú v logu."),
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
def lit(v):
    """C# string literal body: backslash, quote and line breaks escaped."""
    return v.replace(chr(92), chr(92)*2).replace(chr(34), chr(92)+chr(34)).replace("\n", "\\n")
fallback = "\n".join(f'        [R.{k}] = "{lit(v[0])}",' for k, v in S.items())
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
