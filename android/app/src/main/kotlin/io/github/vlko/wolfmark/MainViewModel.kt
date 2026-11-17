package io.github.vlko.wolfmark

import android.app.Activity
import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkInfo
import androidx.work.WorkManager
import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.NotePath
import io.github.vlko.wolfmark.core.NoteText
import io.github.vlko.wolfmark.core.TagName
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class Screen { LOADING, SIGN_IN, FOLDER, NOTES }

data class TagCount(val tag: String?, val count: Int)

data class OpenNote(val note: MirrorNote, val markdown: String?)

data class FolderPicker(val path: String, val folders: List<String>?, val loading: Boolean)

data class UiState(
    val screen: Screen = Screen.LOADING,
    val configured: Boolean = true,
    val account: String? = null,
    val vault: String = WolfMarkApp.DEFAULT_VAULT,
    val notes: List<MirrorNote> = emptyList(),
    val tags: List<TagCount> = emptyList(),
    /** The tag key shown; [NO_FILTER] = all notes, null = notes without a tag. */
    val filter: String? = NO_FILTER,
    val syncing: Boolean = false,
    val syncProgress: Int = 0,
    val error: String? = null,
    val pendingShares: Int = 0,
    val open: OpenNote? = null,
    val picker: FolderPicker = FolderPicker("", null, false),
) {
    val visibleNotes: List<MirrorNote>
        get() = if (filter == NO_FILTER) notes else notes.filter { it.path.tag?.let(TagName::key) == filter }

    companion object {
        const val NO_FILTER = "\u0000all"
    }
}

/** State of the one window: who is signed in, the notes of the phone's copy, the open note. All I/O off the main thread. */
class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val app = application as WolfMarkApp
    private val _state = MutableStateFlow(UiState(configured = app.auth.isConfigured))
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

    /** The window is in front again, or a delivery finished: the waiting shares are counted and the notes refreshed. */
    fun onForeground() {
        if (_state.value.screen != Screen.NOTES) return
        viewModelScope.launch { _state.update { it.copy(pendingShares = pendingCount()) } }
        refresh()
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
        val notes = withContext(Dispatchers.IO) { app.mirror.notes() }
        val tags = notes.groupBy { it.path.tag?.let(TagName::key) }
            .map { (_, list) -> TagCount(list.first().path.tag, list.size) }
            .sortedWith(compareByDescending<TagCount> { it.count }.thenBy { it.tag.orEmpty() })
        val pending = pendingCount()
        _state.update { s ->
            val filter = if (s.filter == UiState.NO_FILTER || tags.any { it.tag?.let(TagName::key) == s.filter }) s.filter else UiState.NO_FILTER
            s.copy(notes = notes, tags = tags, filter = filter, pendingShares = pending)
        }
    }

    fun signIn(activity: Activity) {
        _state.update { it.copy(error = null) }
        app.auth.signIn(activity) { error ->
            when {
                error == null -> {
                    DeliveryWorker.schedule(app) // shares made before signing in
                    start()
                }
                error.isNotEmpty() -> _state.update { it.copy(error = app.getString(R.string.sign_in_failed, error)) }
            }
        }
    }

    fun signOut() = viewModelScope.launch {
        syncJob?.cancel()
        withContext(Dispatchers.IO) { app.auth.signOut() }
        _state.update { UiState(screen = Screen.SIGN_IN, configured = app.auth.isConfigured, pendingShares = it.pendingShares) }
    }

    /** Brings the phone's copy up to date and looks for computers that receive shares. */
    fun refresh() {
        if (syncJob?.isActive == true) return
        syncJob = viewModelScope.launch {
            _state.update { it.copy(syncing = true, syncProgress = 0, error = null) }
            // The list fills while the notes arrive – the first run of a large notebook takes minutes.
            val filling = launch {
                while (true) {
                    kotlinx.coroutines.delay(2000)
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
            _state.update { it.copy(syncing = false, error = error?.let { e -> app.getString(R.string.sync_failed, e.message ?: e.javaClass.simpleName) }) }
        }
    }

    fun sendPending() {
        DeliveryWorker.schedule(app)
        viewModelScope.launch {
            kotlinx.coroutines.delay(3000)
            _state.update { it.copy(pendingShares = pendingCount()) }
        }
    }

    fun setFilter(filter: String?) = _state.update { it.copy(filter = filter) }

    fun open(note: MirrorNote) = viewModelScope.launch {
        _state.update { it.copy(open = OpenNote(note, null)) }
        val markdown = withContext(Dispatchers.IO) { app.mirror.read(note.path)?.let { prepare(note.path, NoteText.body(it)) } }
        _state.update { if (it.open?.note == note) it.copy(open = OpenNote(note, markdown ?: "")) else it }
    }

    fun close() = _state.update { it.copy(open = null) }

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
    fun chooseFolder() {
        _state.update { it.copy(screen = Screen.FOLDER, open = null) }
        browse(app.vault.substringBeforeLast('/', ""))
    }

    fun browse(path: String) = viewModelScope.launch {
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

    fun useFolder(path: String) = viewModelScope.launch {
        if (path.isEmpty()) return@launch
        syncJob?.cancel()
        app.vault = path
        app.devicesRefreshedAt = 0
        _state.update { it.copy(vault = app.vault, notes = emptyList(), tags = emptyList(), filter = UiState.NO_FILTER) }
        showNotes()
    }

    fun cancelFolder() {
        if (app.vaultConfirmed) _state.update { it.copy(screen = Screen.NOTES) }
    }

    private companion object {
        val IMAGE = Regex("""!\[([^\]]*)]\(([^)\s]+)[^)]*\)""")
    }
}
