package io.github.vlko.wolfmark.ui

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.view.WindowCompat
import io.github.vlko.wolfmark.Actions
import io.github.vlko.wolfmark.CalendarView
import io.github.vlko.wolfmark.FolderPicker
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.Screen
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.Icons
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.Palette
import io.github.vlko.wolfmark.ui.theme.Typo

/** The palette of the desktop, light or dark as chosen in Settings (or as the system says). */
@Composable
fun WolfMarkTheme(theme: String, content: @Composable () -> Unit) {
    val dark = when (theme) {
        "light" -> false
        "dark" -> true
        else -> isSystemInDarkTheme()
    }
    val palette = if (dark) Palette.Dark else Palette.Light
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            // Dark icons in the system bars over the light window, light ones over the dark.
            (view.context as? Activity)?.window?.let { window ->
                WindowCompat.getInsetsController(window, view).apply {
                    isAppearanceLightStatusBars = !dark
                    isAppearanceLightNavigationBars = !dark
                }
            }
        }
    }
    val scheme = if (dark) {
        darkColorScheme(primary = palette.accent, background = palette.window, surface = palette.window, onBackground = palette.ink, onSurface = palette.ink)
    } else {
        lightColorScheme(primary = palette.accent, background = palette.window, surface = palette.window, onBackground = palette.ink, onSurface = palette.ink)
    }
    CompositionLocalProvider(LocalPalette provides palette) {
        MaterialTheme(colorScheme = scheme, content = content)
    }
}

@Composable
fun WolfMarkScreen(state: UiState, a: Actions) {
    WolfMarkTheme(state.theme) {
        val p = LocalPalette.current
        BackHandler(enabled = state.screen == Screen.SETTINGS || state.fullNote || state.view == CalendarView.MONTH || (state.screen == Screen.FOLDER && state.notes.isNotEmpty())) {
            when {
                state.screen == Screen.SETTINGS -> a.closeSettings()
                state.screen == Screen.FOLDER -> a.cancelFolder()
                state.fullNote -> a.setFullNote(false)
                else -> a.setView(CalendarView.WEEK)
            }
        }
        // One flat surface, as on the desktop: no cards, no shadows.
        Box(Modifier.fillMaxSize().background(p.window).windowInsetsPadding(WindowInsets.safeDrawing)) {
            when (state.screen) {
                Screen.LOADING -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = p.accent) }
                Screen.SIGN_IN -> SignIn(state, a)
                Screen.FOLDER -> Folders(state.picker, state.error, a)
                Screen.SETTINGS -> SettingsScreen(state, a)
                Screen.NOTES -> Notes(state, a)
            }
        }
    }
}

@Composable
private fun TopBar(state: UiState, a: Actions) {
    val p = LocalPalette.current
    Row(Modifier.fillMaxWidth().height(48.dp).padding(start = 16.dp, end = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Pencil, null, Modifier.size(16.dp), tint = p.accent)
        Text(stringResource(R.string.app_name).uppercase(), Modifier.padding(start = 8.dp), style = Typo.header(19f, p.inkSecondary))
        Spacer(Modifier.weight(1f))
        IconBtn(Icons.Sync, stringResource(R.string.refresh), a::refresh, enabled = !state.syncing)
        IconBtn(Icons.Settings, stringResource(R.string.settings), a::openSettings, size = 20.dp)
    }
}

@Composable
private fun Notes(state: UiState, a: Actions) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxSize()) {
        TopBar(state, a)
        if (state.syncing) {
            LinearProgressIndicator(Modifier.fillMaxWidth().height(2.dp), color = p.accent, trackColor = p.window)
        } else {
            Spacer(Modifier.height(2.dp))
        }
        Row(Modifier.weight(1f)) {
            Column(Modifier.weight(1f).padding(start = 16.dp, end = if (state.fullNote) 12.dp else 4.dp)) {
                val open = state.open
                when {
                    state.fullNote && open != null -> {
                        NoteBar(open, state, a)
                        NoteView(open, state.textScale, a::setTextScale, Modifier.weight(1f))
                    }
                    state.view == CalendarView.MONTH -> {
                        CalendarHeader(state, a, Modifier.padding(bottom = 8.dp))
                        MonthGrid(state, a, Modifier.padding(bottom = 4.dp))
                        History(state, a, Modifier.weight(1f))
                    }
                    else -> {
                        CalendarHeader(state, a, Modifier.padding(bottom = 6.dp))
                        WeekBand(state, a)
                        NoteAndHistory(state, a, Modifier.weight(1f))
                    }
                }
            }
            if (!state.fullNote) TagRail(state.tags, state.colors, state.tagFilter, a::selectTag)
        }
        StatusBar(state, a)
    }
}

/** The note on top, the history under it, and between them a handle that moves the line (as the desktop's splitter). */
@Composable
private fun NoteAndHistory(state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val open = state.open
    if (open == null) {
        History(state, a, modifier.padding(top = 6.dp))
        return
    }
    BoxWithConstraints(modifier) {
        val height = maxHeight
        val total = with(LocalDensity.current) { height.toPx() }
        val split = rememberUpdatedState(state.split)
        Column(Modifier.fillMaxSize()) {
            Column(Modifier.height(height * state.split)) {
                NoteBar(open, state, a, Modifier.padding(top = 2.dp))
                NoteView(open, state.textScale, a::setTextScale, Modifier.weight(1f))
            }
            Box(
                Modifier.fillMaxWidth().height(16.dp).draggable(
                    rememberDraggableState { delta -> if (total > 0) a.setSplit(split.value + delta / total, false) },
                    Orientation.Vertical,
                    onDragStopped = { a.setSplit(split.value, true) },
                ),
                contentAlignment = Alignment.Center,
            ) {
                Box(Modifier.fillMaxWidth().height(1.dp).background(p.divider))
                Box(Modifier.width(40.dp).height(4.dp).clip(RoundedCornerShape(2.dp)).background(p.subtleFillHover))
            }
            History(state, a, Modifier.weight(1f))
        }
    }
}

/** The line at the bottom: how many notes, when they were last brought up to date, what is waiting, what went wrong. */
@Composable
private fun StatusBar(state: UiState, a: Actions) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxWidth()) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(p.divider))
        Row(Modifier.fillMaxWidth().height(26.dp).padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Sync, null, Modifier.size(11.dp), tint = p.inkTertiary)
            val notes = pluralStringResource(R.plurals.note_count, state.notes.size, state.notes.size)
            val sync = when {
                state.syncing && state.syncProgress > 0 -> " · " + stringResource(R.string.syncing, state.syncProgress)
                state.syncing -> " · " + stringResource(R.string.syncing_short)
                state.lastSync != null -> " · %02d:%02d".format(state.lastSync.hour, state.lastSync.minute)
                else -> ""
            }
            Caps(notes + sync, 11.5f, p.inkTertiary, Modifier.padding(start = 6.dp))
            if (state.pendingShares > 0) {
                Caps(
                    " · " + pluralStringResource(R.plurals.pending_shares, state.pendingShares, state.pendingShares),
                    11.5f, p.accent, Modifier.clickable(onClick = a::sendPending),
                )
            }
            Spacer(Modifier.weight(1f))
            state.error?.let { Caps(it, 11.5f, p.error, Modifier.weight(3f, fill = false).padding(start = 8.dp)) }
        }
    }
}

@Composable
private fun SignIn(state: UiState, a: Actions) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Pencil, null, Modifier.size(18.dp), tint = p.accent)
            Text(stringResource(R.string.app_name).uppercase(), Modifier.padding(start = 8.dp), style = Typo.header(22f, p.inkSecondary))
        }
        Spacer(Modifier.height(24.dp))
        Text(stringResource(R.string.sign_in_title).uppercase(), style = Typo.header(26f, p.ink))
        Spacer(Modifier.height(10.dp))
        Text(stringResource(R.string.sign_in_text), style = Typo.body(15f, p.inkSecondary))
        Spacer(Modifier.height(24.dp))
        if (state.configured) {
            FlatBtn(stringResource(R.string.sign_in), a::signIn, fill = p.accent, ink = androidx.compose.ui.graphics.Color.White)
        } else {
            Text(stringResource(R.string.not_configured), style = Typo.body(14f, p.error))
        }
        state.error?.let {
            Spacer(Modifier.height(16.dp))
            Text(it, style = Typo.body(14f, p.error))
        }
        if (state.pendingShares > 0) {
            Spacer(Modifier.height(16.dp))
            Caps(pluralStringResource(R.plurals.pending_shares, state.pendingShares, state.pendingShares), 12.5f, p.inkTertiary)
        }
    }
}

@Composable
private fun Folders(picker: FolderPicker, error: String?, a: Actions) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Spacer(Modifier.height(16.dp))
        SectionHeader(stringResource(R.string.folder_title), size = 19f)
        Text(stringResource(R.string.folder_text), Modifier.padding(top = 6.dp, bottom = 12.dp), style = Typo.body(14f, p.inkSecondary))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Folder, null, Modifier.size(14.dp), tint = p.inkTertiary)
            Caps(stringResource(R.string.folder_root) + if (picker.path.isEmpty()) "" else " / " + picker.path.replace("/", " / "), 13f, p.ink, Modifier.padding(start = 6.dp))
        }
        Row(Modifier.padding(vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FlatBtn(stringResource(R.string.folder_up), { a.browse(picker.path.substringBeforeLast('/', "")) }, enabled = picker.path.isNotEmpty())
            FlatBtn(stringResource(R.string.folder_use), { a.useFolder(picker.path) }, fill = p.todayFill, ink = p.todayInk, enabled = picker.path.isNotEmpty())
        }
        error?.let { Text(it, style = Typo.body(13f, p.error)) }
        Box(Modifier.fillMaxWidth().height(1.dp).background(p.divider))
        when {
            picker.loading -> LinearProgressIndicator(Modifier.fillMaxWidth().height(2.dp), color = p.accent, trackColor = p.window)
            picker.folders.isNullOrEmpty() -> Text(stringResource(R.string.folder_empty), Modifier.padding(vertical = 14.dp), style = Typo.body(14f, p.inkTertiary))
            else -> LazyColumn {
                items(picker.folders) { name ->
                    Row(
                        Modifier.fillMaxWidth().clickable { a.browse(if (picker.path.isEmpty()) name else picker.path + "/" + name) }.padding(vertical = 13.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Icon(Icons.Folder, null, Modifier.size(16.dp), tint = p.inkSecondary)
                        Text(name, Modifier.padding(start = 10.dp), style = Typo.body(15f, p.ink))
                    }
                }
            }
        }
    }
}
