package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.calculateZoom
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.mikepenz.markdown.coil3.Coil3ImageTransformerImpl
import com.mikepenz.markdown.m3.Markdown
import com.mikepenz.markdown.m3.markdownColor
import com.mikepenz.markdown.m3.markdownTypography
import com.mikepenz.markdown.model.rememberMarkdownState
import io.github.vlko.wolfmark.Actions
import io.github.vlko.wolfmark.MainViewModel.Companion.modifiedTime
import io.github.vlko.wolfmark.OpenNote
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.Icons
import io.github.vlko.wolfmark.ui.theme.LeadTime
import io.github.vlko.wolfmark.ui.theme.LocalPalette

/**
 * The line above the note: when it was written, its tag, when it was last changed, and the switch to the note
 * alone. (On the desktop the times stand in a column beside the text; a phone has no room for that.)
 */
@Composable
fun NoteBar(open: OpenNote, state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val note = open.note
    val created = note.time()
    val edited = note.modifiedTime()
    Row(modifier.fillMaxWidth().height(36.dp), verticalAlignment = Alignment.CenterVertically) {
        LeadTime(created.hour, created.minute, 26f)
        Spacer(Modifier.width(10.dp))
        Row(
            Modifier.height(26.dp).clip(RoundedCornerShape(3.dp)).background(p.subtleFill).padding(horizontal = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.size(10.dp).background(p.tag(state.colors.of(note.path.tag))))
            Caps(note.path.tag ?: stringResource(R.string.no_tag), 13f, p.ink, Modifier.padding(start = 7.dp))
        }
        Spacer(Modifier.weight(1f))
        Caps(stringResource(R.string.edited, "%02d:%02d".format(edited.hour, edited.minute)), 10.5f, p.inkTertiary, Modifier.padding(end = 2.dp))
        if (state.fullNote) {
            IconBtn(Icons.Collapse, stringResource(R.string.exit_full_note), { a.setFullNote(false) }, size = 16.dp)
        } else {
            IconBtn(Icons.Expand, stringResource(R.string.full_note), { a.setFullNote(true) }, size = 16.dp)
        }
    }
}

/**
 * The note, rendered in the colours and proportions of the desktop's editor (`EditorTheme`, `MarkdownColorizer`).
 * Two fingers change the text size; one scrolls.
 */
@Composable
fun NoteView(open: OpenNote, scale: Float, onScale: (Float, Boolean) -> Unit, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val current = rememberUpdatedState(scale)
    val change = rememberUpdatedState(onScale)
    Box(
        modifier.pointerInput(Unit) {
            awaitEachGesture {
                var zoomed = false
                do {
                    // Before the scroll sees it: with two fingers down the gesture is a zoom, not a scroll.
                    val event = awaitPointerEvent(PointerEventPass.Initial)
                    if (event.changes.count { it.pressed } >= 2) {
                        val zoom = event.calculateZoom()
                        if (zoom != 1f) {
                            zoomed = true
                            change.value(current.value * zoom, false)
                        }
                        event.changes.forEach { it.consume() }
                    }
                } while (event.changes.any { it.pressed })
                if (zoomed) change.value(current.value, true)
            }
        },
    ) {
        when (val markdown = open.markdown) {
            null -> LinearProgressIndicator(Modifier.fillMaxWidth().height(2.dp), color = p.accent, trackColor = p.window)
            "" -> Text(stringResource(R.string.note_missing), Modifier.padding(top = 12.dp), color = p.inkTertiary, fontSize = 14.sp)
            else -> key(open.note.path.relativePath) {
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(top = 6.dp, bottom = 24.dp, end = 8.dp)) {
                    val base = 15f * scale
                    val text = TextStyle(fontSize = base.sp, lineHeight = (base * 1.45f).sp, color = p.text)
                    fun heading(times: Float) = TextStyle(fontSize = (base * times).sp, lineHeight = (base * times * 1.25f).sp, fontWeight = FontWeight.Bold, color = p.heading)
                    val code = TextStyle(fontFamily = FontFamily.Monospace, fontSize = (base * 0.92f).sp, lineHeight = (base * 1.4f).sp, color = p.code)
                    // A note of ordinary size is laid out at once (no empty flash when switching notes); a very
                    // long one is parsed in the background.
                    Markdown(
                        markdownState = rememberMarkdownState(markdown, immediate = markdown.length < 40_000),
                        colors = markdownColor(
                            text = p.text,
                            codeBackground = p.codeBackground,
                            inlineCodeBackground = p.codeBackground,
                            dividerColor = p.muted,
                            tableBackground = p.window,
                        ),
                        typography = markdownTypography(
                            h1 = heading(1.6f), h2 = heading(1.35f), h3 = heading(1.18f),
                            h4 = heading(1.05f), h5 = heading(1.05f), h6 = heading(1.05f),
                            text = text, paragraph = text, ordered = text, bullet = text, list = text, table = text,
                            code = code, inlineCode = code,
                            quote = text.copy(color = p.quote, fontStyle = FontStyle.Italic),
                            textLink = TextLinkStyles(style = SpanStyle(color = p.link, textDecoration = TextDecoration.Underline)),
                        ),
                        imageTransformer = Coil3ImageTransformerImpl,
                    )
                }
            }
        }
    }
}
