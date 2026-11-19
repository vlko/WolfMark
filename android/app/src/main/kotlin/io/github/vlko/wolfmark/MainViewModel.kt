package io.github.vlko.wolfmark

import android.app.Activity
import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkInfo
import androidx.work.WorkManager
import io.github.vlko.wolfmark.core.CalendarModel
import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.NotePath
import io.github.vlko.wolfmark.core.NoteText
import io.github.vlko.wolfmark.core.TagColors
import io.github.vlko.wolfmark.core.TagName
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.YearMonth
import java.time.ZoneId

enum class Screen { LOADING, SIGN_IN, FOLDER, NOTES, SETTINGS }

/** The calendar above the history: the week band with the open note, or the whole month without it. */
enum class CalendarView { WEEK, MONTH }

data class OpenNote(val note: MirrorNote, val markdown: String?)

data class FolderPicker(val path: String, val folders: List<String>?, val loading: Boolean)

/** A computer that receives shares, for Settings. */
data class DeviceLine(val name: String, val address: String)

data class UiState(
    val screen: Screen = Screen.LOADING,
    val configured: Boolean = true,
    val account: String? = null,
    val vault: String = WolfMarkApp.DEFAULT_VAULT,
    val notes: List<MirrorNote> = emptyList(),
    val calendar: CalendarModel = CalendarModel(emptyList()),
    val colors: TagColors = TagColors(),
    /** Tags that have notes, most used first (the rail). */
    val tags: List<String> = emptyList(),
    /** The key of the tag the rail filters by; null = all. */
    val tagFilter: String? = null,
    val today: LocalDate = LocalDate.now(),
    val selectedDay: LocalDate = LocalDate.now(),
    /** The history shows only [selectedDay]. */
    val dayFilter: Boolean = false,
    val view: CalendarView = CalendarView.WEEK,
    /** Monday of the week in the band. */
    val week: LocalDate = CalendarModel.monday(LocalDate.now()),
    val month: YearMonth = YearMonth.now(),
    /** Only the note: calendar, history and rail are hidden. */
    val fullNote: Boolean = false,
    val open: OpenNote? = null,
    val syncing: Boolean = false,
    val syncProgress: Int = 0,
    val lastSync: LocalTime? = null,
    val error: String? = null,
    val pendingShares: Int = 0,
    val picker: FolderPicker = FolderPicker("", null, false),
    val theme: String = "system",
    val textScale: Float = 1f,
    val split: Float = 0.55f,
    val devices: List<DeviceLine> = emptyList(),
    val version: String = "",
) {
    /** The history: newest change first, narrowed by the tag of the rail and, when asked, to the selected day. */
    val history: List<MirrorNote>
        get() = notes.asSequence()
            .filter { tagFilter == null || it.path.tag?.let(TagName::key) == tagFilter }
            .filter { !dayFilter || it.day() == selectedDay }
            .sortedByDescending { it.modifiedMs }
            .toList()
}

/**
 * A note picked to read (from the history, in either view): the week view with the note, on the note's week, its
 * day selected – the same on the desktop (M6-08).
 */
fun UiState.opened(note: MirrorNote): UiState {
    val day = note.day()
    return copy(
        open = OpenNote(note, open?.takeIf { it.note.path == note.path }?.markdown),
        view = CalendarView.WEEK,
        selectedDay = day,
        week = CalendarModel.monday(day),
    )
}

/**
 * "Today": how the app starts – today selected, its week and month shown, no tag or day filter. Only the calendar
 * stays as it is, week or month.
 */
fun UiState.atToday(today: LocalDate): UiState = copy(
    today = today,
    selectedDay = today,
    week = CalendarModel.monday(today),
    month = YearMonth.from(today),
    tagFilter = null,
    dayFilter = false,
)

/** What the screens can ask for – the view model in the app, nothing in a screenshot test. */
interface Actions {
    fun signIn() {}
    fun signOut() {}
    fun refresh() {}
    fun sendPending() {}
    fun openSettings() {}
    fun closeSettings() {}
    fun setTheme(theme: String) {}
    fun setTextScale(scale: Float, save: Boolean) {}
    fun setSplit(split: Float, save: Boolean) {}
    fun selectTag(tag: String?) {}
    fun selectDay(day: LocalDate) {}
    fun clearDayFilter() {}
    fun step(forward: Boolean) {}
    fun goToday() {}
    fun setView(view: CalendarView) {}
    fun setFullNote(full: Boolean) {}
    fun open(note: MirrorNote) {}
    fun chooseFolder() {}
    fun browse(path: String) {}
    fun useFolder(path: String) {}
    fun cancelFolder() {}
}

/** State of the one window. All I/O runs off the main thread. */
class MainViewModel(application: Application) : AndroidViewModel(application), Actions {
    private val app = application as WolfMarkApp
    private val _state = MutableStateFlow(
        UiState(
            configured = app.auth.isConfigured,
            theme = app.theme,
            textScale = app.textScale,
            split = app.split,
            version = BuildConfig.VERSION_NAME,
        ),
    )
    val state: StateFlow<UiState> = _state
    private var syncJob: Job? = null

    init {
        start()
        // A share that has just been sent: show it, and take it off the "waiting" count.
        viewModelScope.launch {
            var running = false
            WorkManager.getInstance(app).getWorkInfosForUniqueWorkFlow(DeliveryWorker.WORK).collect { infos ->
                val now = infos.any { it.state == WorkInfo.State.RUNNING }
                if (running && !now) onForeground()
                running = now
            }
        }
    }

    private fun start() = viewModelScope.launch {
        val signedIn = withContext(Dispatchers.IO) { app.auth.isSignedIn() }
        if (!signedIn) {
            _state.update { it.copy(screen = Screen.SIGN_IN, pendingShares = pendingCount()) }
            return@launch
        }
        val account = withContext(Dispatchers.IO) { app.auth.accountName() }
        _state.update { it.copy(account = account, vault = app.vault) }
        if (app.vaultConfirmed) {
            showNotes()
        } else {
            // The desktop's default folder, if it is there; otherwise the user says where the notes are.
            val found = withContext(Dispatchers.IO) { runCatching { app.graph().children(app.vault) != null }.getOrDefault(false) }
            if (found) {
                app.vault = app.vault
                showNotes()
            } else {
                _state.update { it.copy(screen = Screen.FOLDER) }
                browse("")
            }
        }
    }

    private suspend fun showNotes() {
        loadNotes()
        _state.update { it.copy(screen = Screen.NOTES) }
        refresh()
    }

    private suspend fun pendingCount(): Int = withContext(Dispatchers.IO) { app.queue.pending().size }

    private suspend fun loadNotes() {
        class Loaded(val notes: List<MirrorNote>, val colors: TagColors, val pending: Int, val devices: List<DeviceLine>)

        val loaded = withContext(Dispatchers.IO) {
            Loaded(
                app.mirror.notes(),
                TagColors.parse(app.mirror.tagsJson()),
                app.queue.pending().size,
                app.devices.load().map { DeviceLine(it.name, it.addresses.joinToString(", ") + ":" + it.port) },
            )
        }
        val calendar = withContext(Dispatchers.Default) { CalendarModel(loaded.notes) }
        val tags = loaded.notes.mapNotNull { it.path.tag }.groupBy(TagName::key)
            .map { (_, names) -> names.min() to names.size }
            .sortedWith(compareByDescending<Pair<String, Int>> { it.second }.thenBy { it.first })
            .map { it.first }
        _state.update { s ->
            s.copy(
                notes = loaded.notes,
                calendar = calendar,
                colors = loaded.colors,
                tags = tags,
                tagFilter = s.tagFilter?.takeIf { key -> tags.any { TagName.key(it) == key } },
                pendingShares = loaded.pending,
                devices = loaded.devices,
                today = LocalDate.now(),
            )
        }
        // Something is always open, as on the desktop: the note changed last.
        val s = _state.value
        val open = s.open
        when {
            // Shown without moving the calendar: this is a refresh, not the user picking a note.
            open == null -> loaded.notes.maxByOrNull { it.modifiedMs }?.let { latest ->
                _state.update { it.copy(open = OpenNote(latest, null)) }
                load(latest)
            }
            loaded.notes.none { it.path == open.note.path } -> _state.update { it.copy(open = null) }
            else -> loaded.notes.first { it.path == open.note.path }.let { changed ->
                if (changed != open.note) {
                    _state.update { it.copy(open = OpenNote(changed, open.markdown)) }
                    load(changed)
                }
            }
        }
    }

    /** Opens the Microsoft sign-in page from [from]. (The plain [Actions.signIn] cannot: it has no window.) */
    fun signIn(from: Activity) {
        _state.update { it.copy(error = null) }
        app.auth.signIn(from) { error ->
            when {
                error == null -> {
                    DeliveryWorker.schedule(app) // shares made before signing in
                    start()
                }
                error.isNotEmpty() -> _state.update { it.copy(error = app.getString(R.string.sign_in_failed, error)) }
            }
        }
    }

    override fun signOut() {
        viewModelScope.launch {
            syncJob?.cancel()
            withContext(Dispatchers.IO) { app.auth.signOut() }
            _state.update {
                UiState(
                    screen = Screen.SIGN_IN, configured = app.auth.isConfigured, pendingShares = it.pendingShares,
                    theme = it.theme, textScale = it.textScale, split = it.split, version = it.version,
                )
            }
        }
    }

    /** The window is in front again, or a delivery finished: the waiting shares are counted and the notes refreshed. */
    fun onForeground() {
        if (_state.value.screen != Screen.NOTES && _state.value.screen != Screen.SETTINGS) return
        viewModelScope.launch { _state.update { it.copy(pendingShares = pendingCount(), today = LocalDate.now()) } }
        refresh()
    }

    /** Brings the phone's copy up to date and looks for computers that receive shares. */
    override fun refresh() {
        if (syncJob?.isActive == true) return
        syncJob = viewModelScope.launch {
            _state.update { it.copy(syncing = true, syncProgress = 0, error = null) }
            // The list fills while the notes arrive – the first run of a large notebook takes minutes.
            val filling = launch {
                while (true) {
                    delay(2000)
                    loadNotes()
                }
            }
            val error = withContext(Dispatchers.IO) {
                runCatching {
                    val graph = app.graph()
                    app.mirror.sync(graph, app.vault) { n -> _state.update { it.copy(syncProgress = n) } }
                    runCatching {
                        app.devices.refresh(graph, app.vault)
                        app.devicesRefreshedAt = System.currentTimeMillis()
                    }
                }.exceptionOrNull()
            }
            filling.cancel()
            loadNotes()
            _state.update {
                it.copy(
                    syncing = false,
                    lastSync = if (error == null) LocalTime.now() else it.lastSync,
                    error = error?.let { e -> app.getString(R.string.sync_failed, e.message ?: e.javaClass.simpleName) },
                )
            }
        }
    }

    override fun sendPending() {
        DeliveryWorker.schedule(app)
        viewModelScope.launch {
            delay(3000)
            _state.update { it.copy(pendingShares = pendingCount()) }
        }
    }

    // Settings.
    override fun openSettings() = _state.update { if (it.screen == Screen.NOTES) it.copy(screen = Screen.SETTINGS) else it }

    override fun closeSettings() = _state.update { if (it.screen == Screen.SETTINGS) it.copy(screen = Screen.NOTES) else it }

    override fun setTheme(theme: String) {
        app.theme = theme
        _state.update { it.copy(theme = theme) }
    }

    override fun setTextScale(scale: Float, save: Boolean) {
        val value = scale.coerceIn(MIN_TEXT_SCALE, MAX_TEXT_SCALE)
        if (save) app.textScale = value
        _state.update { it.copy(textScale = value) }
    }

    override fun setSplit(split: Float, save: Boolean) {
        val value = split.coerceIn(0.2f, 0.85f)
        if (save) app.split = value
        _state.update { it.copy(split = value) }
    }

    // Calendar and tags.
    override fun selectTag(tag: String?) = _state.update { s ->
        val key = tag?.let(TagName::key)
        s.copy(tagFilter = if (key == s.tagFilter) null else key)
    }

    /** A day of the calendar: the history narrows to it and its newest note opens. Tapped again, the history widens. */
    override fun selectDay(day: LocalDate) {
        val s = _state.value
        if (s.dayFilter && s.selectedDay == day) return clearDayFilter()
        _state.update { it.copy(selectedDay = day, dayFilter = true, week = CalendarModel.monday(day)) }
        if (s.view == CalendarView.WEEK) {
            s.notes.filter { it.day() == day }.maxByOrNull { it.modifiedMs }?.let(::open)
        }
    }

    override fun clearDayFilter() = _state.update { it.copy(dayFilter = false) }

    /** A week on in the week view, a month in the month view. */
    override fun step(forward: Boolean) = _state.update { s ->
        val n = if (forward) 1L else -1L
        if (s.view == CalendarView.WEEK) s.copy(week = s.week.plusWeeks(n)) else s.copy(month = s.month.plusMonths(n))
    }

    override fun goToday() {
        _state.update { it.atToday(LocalDate.now()) }
        // As after a start: the note changed last – shown without leaving the month view, if that is where we are.
        _state.value.notes.maxByOrNull { it.modifiedMs }?.let { latest ->
            if (_state.value.open?.note?.path != latest.path) {
                _state.update { it.copy(open = OpenNote(latest, null)) }
                load(latest)
            }
        }
    }

    override fun setView(view: CalendarView) = _state.update { s ->
        // Each view opens where the other one was looking.
        if (view == CalendarView.MONTH) s.copy(view = view, month = CalendarModel.monthOfWeek(s.week), fullNote = false)
        else s.copy(view = view, week = CalendarModel.monday(if (YearMonth.from(s.selectedDay) == s.month) s.selectedDay else s.month.atDay(1)))
    }

    override fun setFullNote(full: Boolean) = _state.update { it.copy(fullNote = full) }

    override fun open(note: MirrorNote) {
        _state.update { it.opened(note) }
        load(note)
    }

    /** Reads the note's text into the open note (if it is still the open one when the text is there). */
    private fun load(note: MirrorNote) {
        viewModelScope.launch {
            val markdown = withContext(Dispatchers.IO) { app.mirror.read(note.path)?.let { prepare(note.path, NoteText.body(it)) } }
            _state.update { if (it.open?.note?.path == note.path) it.copy(open = OpenNote(note, markdown ?: "")) else it }
        }
    }

    /**
     * Images of the note are files in OneDrive, not web addresses: each is fetched into the copy (once) and the
     * link is pointed at that file. Links to the web are left alone – the renderer loads nothing from them.
     */
    private fun prepare(note: NotePath, markdown: String): String {
        val graph = runCatching { app.graphOrNull() }.getOrNull()
        return IMAGE.replace(markdown) { match ->
            val link = match.groupValues[2]
            val file = runCatching { app.mirror.asset(graph, note, link) }.getOrNull()
            if (file == null) match.value else "![${match.groupValues[1]}](${android.net.Uri.fromFile(file)})"
        }
    }

    // The notes folder.
    override fun chooseFolder() {
        _state.update { it.copy(screen = Screen.FOLDER) }
        browse(app.vault.substringBeforeLast('/', ""))
    }

    override fun browse(path: String) {
        viewModelScope.launch {
            _state.update { it.copy(picker = FolderPicker(path, null, true), error = null) }
            val result = withContext(Dispatchers.IO) {
                runCatching { app.graph().children(path).orEmpty().filter { it.isFolder && !it.name.startsWith(".") }.map { it.name }.sorted() }
            }
            _state.update {
                it.copy(
                    picker = FolderPicker(path, result.getOrNull(), false),
                    error = result.exceptionOrNull()?.let { e -> app.getString(R.string.sync_failed, e.message ?: e.javaClass.simpleName) },
                )
            }
        }
    }

    override fun useFolder(path: String) {
        if (path.isEmpty()) return
        viewModelScope.launch {
            syncJob?.cancel()
            app.vault = path
            app.devicesRefreshedAt = 0
            _state.update { it.copy(vault = app.vault, notes = emptyList(), tags = emptyList(), tagFilter = null, open = null, calendar = CalendarModel(emptyList())) }
            showNotes()
        }
    }

    override fun cancelFolder() {
        if (app.vaultConfirmed) _state.update { it.copy(screen = Screen.NOTES) }
    }

    companion object {
        const val MIN_TEXT_SCALE = 0.7f
        const val MAX_TEXT_SCALE = 2.5f
        private val IMAGE = Regex("""!\[([^\]]*)]\(([^)\s]+)[^)]*\)""")

        /** When a note was last changed, on this phone's clock. */
        fun MirrorNote.modifiedTime(): LocalTime = Instant.ofEpochMilli(modifiedMs).atZone(ZoneId.systemDefault()).toLocalTime()

        fun MirrorNote.modifiedDay(): LocalDate = Instant.ofEpochMilli(modifiedMs).atZone(ZoneId.systemDefault()).toLocalDate()
    }
}
