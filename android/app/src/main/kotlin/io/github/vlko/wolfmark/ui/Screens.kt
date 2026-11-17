package io.github.vlko.wolfmark.ui

import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.mikepenz.markdown.coil3.Coil3ImageTransformerImpl
import com.mikepenz.markdown.m3.Markdown
import io.github.vlko.wolfmark.FolderPicker
import io.github.vlko.wolfmark.MainViewModel
import io.github.vlko.wolfmark.OpenNote
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.Screen
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.TagName
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

@Composable
fun WolfMarkTheme(content: @Composable () -> Unit) {
    val dark = isSystemInDarkTheme()
    val context = LocalContext.current
    val colors = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> if (dark) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        dark -> darkColorScheme()
        else -> lightColorScheme()
    }
    MaterialTheme(colorScheme = colors, content = content)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WolfMarkScreen(model: MainViewModel, onSignIn: () -> Unit) {
    val state by model.state.collectAsStateWithLifecycle()
    WolfMarkTheme {
        val open = state.open
        BackHandler(enabled = open != null || (state.screen == Screen.FOLDER && state.notes.isNotEmpty())) {
            if (open != null) model.close() else model.cancelFolder()
        }
        Scaffold(
            topBar = {
                TopAppBar(
                    title = {
                        Text(
                            open?.note?.title?.ifEmpty { null } ?: stringResource(R.string.app_name),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    },
                    navigationIcon = {
                        if (open != null) TextButton(onClick = model::close) { Text(stringResource(R.string.back)) }
                    },
                    actions = {
                        if (state.screen == Screen.NOTES && open == null) {
                            TextButton(onClick = model::refresh, enabled = !state.syncing) { Text(stringResource(R.string.refresh)) }
                            var menu by remember { mutableStateOf(false) }
                            TextButton(onClick = { menu = true }) { Text("⋮", style = MaterialTheme.typography.titleLarge) }
                            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                                DropdownMenuItem(text = { Text(stringResource(R.string.choose_folder) + ": " + state.vault) }, onClick = { menu = false; model.chooseFolder() })
                                DropdownMenuItem(text = { Text(stringResource(R.string.sign_out) + (state.account?.let { " ($it)" } ?: "")) }, onClick = { menu = false; model.signOut() })
                            }
                        }
                    },
                )
            },
        ) { padding ->
            Box(Modifier.padding(padding).fillMaxSize()) {
                when {
                    open != null -> NoteView(open)
                    state.screen == Screen.LOADING -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
                    state.screen == Screen.SIGN_IN -> SignIn(state, onSignIn)
                    state.screen == Screen.FOLDER -> Folders(state.picker, state.error, model::browse, model::useFolder)
                    else -> Notes(state, model)
                }
            }
        }
    }
}

@Composable
private fun SignIn(state: UiState, onSignIn: () -> Unit) {
    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center) {
        Text(stringResource(R.string.sign_in_title), style = MaterialTheme.typography.headlineSmall)
        Spacer(Modifier.height(12.dp))
        Text(stringResource(R.string.sign_in_text), style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(24.dp))
        if (state.configured) {
            Button(onClick = onSignIn) { Text(stringResource(R.string.sign_in)) }
        } else {
            Text(stringResource(R.string.not_configured), color = MaterialTheme.colorScheme.error)
        }
        state.error?.let {
            Spacer(Modifier.height(16.dp))
            Text(it, color = MaterialTheme.colorScheme.error)
        }
        if (state.pendingShares > 0) {
            Spacer(Modifier.height(16.dp))
            Text(pluralStringResource(R.plurals.pending_shares, state.pendingShares, state.pendingShares))
        }
    }
}

@Composable
private fun Folders(picker: FolderPicker, error: String?, browse: (String) -> Unit, use: (String) -> Unit) {
    Column(Modifier.fillMaxSize()) {
        Column(Modifier.padding(16.dp)) {
            Text(stringResource(R.string.folder_title), style = MaterialTheme.typography.titleLarge)
            Text(stringResource(R.string.folder_text), style = MaterialTheme.typography.bodyMedium)
            Spacer(Modifier.height(12.dp))
            Text(stringResource(R.string.folder_root) + if (picker.path.isEmpty()) "" else " / " + picker.path.replace("/", " / "), fontWeight = FontWeight.SemiBold)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { browse(picker.path.substringBeforeLast('/', "")) }, enabled = picker.path.isNotEmpty()) { Text(stringResource(R.string.folder_up)) }
                Button(onClick = { use(picker.path) }, enabled = picker.path.isNotEmpty()) { Text(stringResource(R.string.folder_use)) }
            }
            error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        }
        HorizontalDivider()
        when {
            picker.loading -> LinearProgressIndicator(Modifier.fillMaxWidth())
            picker.folders.isNullOrEmpty() -> Text(stringResource(R.string.folder_empty), Modifier.padding(16.dp))
            else -> LazyColumn {
                items(picker.folders) { name ->
                    Text(
                        name,
                        Modifier.fillMaxWidth().clickable { browse(if (picker.path.isEmpty()) name else picker.path + "/" + name) }.padding(16.dp),
                        style = MaterialTheme.typography.bodyLarge,
                    )
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun Notes(state: UiState, model: MainViewModel) {
    Column(Modifier.fillMaxSize()) {
        if (state.syncing) {
            LinearProgressIndicator(Modifier.fillMaxWidth())
            if (state.syncProgress > 0) Text(stringResource(R.string.syncing, state.syncProgress), Modifier.padding(horizontal = 16.dp, vertical = 4.dp), style = MaterialTheme.typography.bodySmall)
        }
        state.error?.let { Text(it, Modifier.padding(16.dp), color = MaterialTheme.colorScheme.error) }
        if (state.pendingShares > 0) {
            Surface(color = MaterialTheme.colorScheme.secondaryContainer) {
                Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(pluralStringResource(R.plurals.pending_shares, state.pendingShares, state.pendingShares), Modifier.weight(1f))
                    TextButton(onClick = model::sendPending) { Text(stringResource(R.string.send_now)) }
                }
            }
        }
        if (state.tags.size > 1) {
            Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(selected = state.filter == UiState.NO_FILTER, onClick = { model.setFilter(UiState.NO_FILTER) }, label = { Text(stringResource(R.string.all_tags)) })
                state.tags.forEach { t ->
                    val key = t.tag?.let(TagName::key)
                    FilterChip(
                        selected = state.filter == key,
                        onClick = { model.setFilter(if (state.filter == key) UiState.NO_FILTER else key) },
                        label = { Text((t.tag?.let { "#$it" } ?: stringResource(R.string.no_tag)) + " " + t.count) },
                    )
                }
            }
        }

        val notes = state.visibleNotes
        if (notes.isEmpty() && !state.syncing) {
            Text(stringResource(R.string.no_notes), Modifier.padding(16.dp))
            return
        }
        val days = remember(notes) { notes.groupBy { it.day() }.toList() }
        LazyColumn(Modifier.fillMaxSize()) {
            days.forEach { (day, list) ->
                stickyHeader(key = day.toString()) {
                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, modifier = Modifier.fillMaxWidth()) {
                        Text(dayLabel(day), Modifier.padding(horizontal = 16.dp, vertical = 6.dp), style = MaterialTheme.typography.labelLarge)
                    }
                }
                items(list, key = { it.path.relativePath }) { note -> NoteRow(note) { model.open(note) } }
            }
        }
    }
}

@Composable
private fun dayLabel(day: LocalDate): String {
    val today = LocalDate.now()
    val date = day.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.FULL))
    return when (day) {
        today -> stringResource(R.string.today) + " · " + date
        today.minusDays(1) -> stringResource(R.string.yesterday) + " · " + date
        else -> date
    }
}

@Composable
private fun NoteRow(note: MirrorNote, onClick: () -> Unit) {
    Column(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 10.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(note.time().format(DateTimeFormatter.ofPattern("HH:mm")), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            note.path.tag?.let {
                Spacer(Modifier.width(8.dp))
                Text("#$it", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
            }
            if (note.path.name?.isConflictCopy == true) {
                Spacer(Modifier.width(8.dp))
                Text(stringResource(R.string.conflict_copy), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.error)
            }
        }
        Text(note.title, style = MaterialTheme.typography.titleMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
        if (note.preview.isNotEmpty()) {
            Text(note.preview, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun NoteView(open: OpenNote) {
    when (val markdown = open.markdown) {
        null -> LinearProgressIndicator(Modifier.fillMaxWidth())
        "" -> Text(stringResource(R.string.note_missing), Modifier.padding(16.dp))
        else -> Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
            Markdown(content = markdown, imageTransformer = Coil3ImageTransformerImpl)
        }
    }
}
