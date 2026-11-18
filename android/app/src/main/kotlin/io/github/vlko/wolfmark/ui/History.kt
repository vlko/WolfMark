package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import io.github.vlko.wolfmark.Actions
import io.github.vlko.wolfmark.MainViewModel.Companion.modifiedDay
import io.github.vlko.wolfmark.MainViewModel.Companion.modifiedTime
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.TagName
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.Icons
import io.github.vlko.wolfmark.ui.theme.LeadTime
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.Typo
import java.time.LocalDate
import java.time.format.DateTimeFormatter

/**
 * The history under the note (the desktop's HistoryView): notes by the day they were last changed, newest first,
 * narrowed by the tag of the rail and by the day picked in the calendar.
 */
@Composable
fun History(state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val notes = state.history
    val locale = currentLocale()
    Column(modifier) {
        SectionHeader(stringResource(R.string.history), Modifier.padding(top = 2.dp, bottom = 2.dp)) {
            state.tagFilter?.let { key ->
                val name = state.tags.firstOrNull { TagName.key(it) == key } ?: key
                Chip("#$name") { a.selectTag(name) }
            }
            if (state.dayFilter) Chip(shortDate(state.selectedDay, state.today, locale), a::clearDayFilter)
        }
        if (notes.isEmpty()) {
            Text(
                stringResource(if (state.notes.isEmpty() && !state.syncing) R.string.no_notes else R.string.no_notes_here),
                Modifier.padding(vertical = 12.dp),
                style = Typo.body(13.5f, p.inkTertiary),
            )
            return
        }
        val groups = remember(notes) { notes.groupBy { it.modifiedDay() }.toList() }
        // A new note arrives on top; a list that was at the top goes back there, otherwise it would sit just above the edge.
        val list = rememberLazyListState()
        LaunchedEffect(notes.first().path.relativePath, state.tagFilter, state.dayFilter, state.selectedDay) {
            if (list.firstVisibleItemIndex <= 3) list.scrollToItem(0)
        }
        LazyColumn(Modifier.fillMaxSize(), state = list) {
            groups.forEach { (day, rows) ->
                item(key = "day-$day") { Caps(dayHeader(day, state.today), 14f, p.inkTertiary, Modifier.padding(top = 8.dp, bottom = 3.dp)) }
                items(rows, key = { it.path.relativePath }) { note ->
                    HistoryRow(note, state, selected = state.open?.note?.path == note.path) { a.open(note) }
                }
            }
        }
    }
}

@Composable
private fun Chip(text: String, onClear: () -> Unit) {
    val p = LocalPalette.current
    Row(
        Modifier.padding(start = 8.dp).height(24.dp).clip(RoundedCornerShape(3.dp)).background(p.subtleFill).clickable(onClick = onClear).padding(start = 9.dp, end = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Caps(text, 12f, p.ink)
        Icon(Icons.Close, stringResource(R.string.clear_filter), Modifier.padding(start = 5.dp).size(10.dp), tint = p.inkSecondary)
    }
}

@Composable
private fun dayHeader(day: LocalDate, today: LocalDate): String = when (day) {
    today -> stringResource(R.string.today)
    today.minusDays(1) -> stringResource(R.string.yesterday)
    else -> {
        val locale = currentLocale()
        val pattern = (if (locale.language == "sk") "EEE d. MMMM" else "EEE, MMMM d") + if (day.year == today.year) "" else " yyyy"
        day.format(DateTimeFormatter.ofPattern(pattern, locale))
    }
}

@Composable
private fun HistoryRow(note: MirrorNote, state: UiState, selected: Boolean, onClick: () -> Unit) {
    val p = LocalPalette.current
    val tag = note.path.tag
    val edited = note.modifiedTime()
    val created = note.time()
    val locale = currentLocale()
    Row(
        Modifier.fillMaxWidth().height(IntrinsicSize.Min)
            .background(if (selected) p.subtleFill else p.window)
            .clickable(onClick = onClick)
            .padding(top = 4.dp, bottom = 4.dp, end = 6.dp),
    ) {
        Box(Modifier.padding(vertical = 3.dp).width(3.dp).fillMaxHeight().background(if (tag == null) p.window.copy(alpha = 0f) else p.tag(state.colors.of(tag))))
        Column(Modifier.width(70.dp), horizontalAlignment = Alignment.End) {
            LeadTime(edited.hour, edited.minute, 22f)
            val prefix = if (note.day() == note.modifiedDay()) "" else shortDate(note.day(), state.today, locale) + " "
            Caps(stringResource(R.string.created, prefix + "%02d:%02d".format(created.hour, created.minute)), 9.5f, p.inkTertiary)
        }
        Spacer(Modifier.width(7.dp))
        DottedLine()
        Column(Modifier.weight(1f).padding(start = 9.dp)) {
            Text(note.title, style = Typo.body(14f, p.ink), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(note.preview, style = Typo.body(12.5f, p.inkSecondary), maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (note.path.name?.isConflictCopy == true) {
            Icon(Icons.Warning, stringResource(R.string.conflict_copy), Modifier.padding(horizontal = 6.dp).size(12.dp).align(Alignment.CenterVertically), tint = p.inkTertiary)
        }
        Column(Modifier.padding(start = 6.dp), horizontalAlignment = Alignment.End) {
            if (tag != null) Caps(tag, 10.5f, p.inkTertiary)
            val tasks = note.openTasks + note.doneTasks
            if (tasks > 0) Text("${note.doneTasks}/$tasks", style = Typo.body(10.5f, p.inkTertiary))
        }
    }
}
