package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import io.github.vlko.wolfmark.Actions
import io.github.vlko.wolfmark.CalendarView
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.core.CalendarDay
import io.github.vlko.wolfmark.core.CalendarModel
import io.github.vlko.wolfmark.core.TagColors
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.Icons
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.RisoNumber
import io.github.vlko.wolfmark.ui.theme.Typo
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.time.format.TextStyle
import java.util.Locale

private fun monthName(month: YearMonth, locale: Locale): String =
    month.month.getDisplayName(TextStyle.FULL_STANDALONE, locale).uppercase(locale) + " " + month.year

/**
 * The day on the left – its number printed large, month and weekday beside it – and on the right what moves the
 * calendar: back, forward, today, week ⇄ month, and which week or month is shown.
 */
@Composable
fun CalendarHeader(state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val day = state.selectedDay
    val locale = currentLocale()
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom) {
        Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically) {
            RisoNumber(day.dayOfMonth.toString(), 54f)
            Column(Modifier.padding(start = 10.dp, top = 4.dp)) {
                Text(monthName(YearMonth.from(day), locale), style = Typo.lead(15f, p.inkSecondary, 0.9f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    day.dayOfWeek.getDisplayName(TextStyle.FULL_STANDALONE, locale).uppercase(locale),
                    Modifier.padding(top = 0.dp),
                    style = Typo.header(22f, p.ink),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Column(horizontalAlignment = Alignment.End) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconBtn(Icons.ChevronLeft, stringResource(R.string.previous), { a.step(false) }, tint = p.inkTertiary)
                IconBtn(Icons.ChevronRight, stringResource(R.string.next), { a.step(true) }, tint = p.inkTertiary)
                FlatBtn(stringResource(R.string.today), a::goToday, Modifier.padding(horizontal = 2.dp), ink = p.inkSecondary, size = 12.5f)
                if (state.view == CalendarView.WEEK) {
                    IconBtn(Icons.Calendar, stringResource(R.string.show_month), { a.setView(CalendarView.MONTH) }, size = 16.dp)
                } else {
                    IconBtn(Icons.Collapse, stringResource(R.string.show_week), { a.setView(CalendarView.WEEK) }, size = 16.dp)
                }
            }
            val title = if (state.view == CalendarView.WEEK) {
                stringResource(R.string.week_title, CalendarModel.isoWeek(state.week)).uppercase() + " · " + monthName(CalendarModel.monthOfWeek(state.week), locale)
            } else {
                monthName(state.month, locale)
            }
            Text(title, Modifier.padding(end = 6.dp, bottom = 3.dp), style = Typo.header(15f, p.ink, 0.6f), maxLines = 1)
        }
    }
}

/** The week between two brackets, as on the desktop: seven days coloured by what was written on them. */
@Composable
fun WeekBand(state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Bracket(left = true)
        Row(Modifier.weight(1f).padding(horizontal = 4.dp)) {
            for (day in state.calendar.week(state.week)) {
                DayCell(
                    day, state.colors,
                    today = day.date == state.today,
                    past = day.date < state.today,
                    selected = day.date == state.selectedDay,
                    dimmed = state.tagFilter != null && !day.hasTag(state.tagFilter),
                    band = true, height = 54.dp, numberSize = 24f,
                    onClick = { a.selectDay(day.date) },
                    modifier = Modifier.weight(1f),
                )
            }
        }
        Bracket(left = false)
    }
    // What the week was about.
    val topics = state.calendar.topics(state.week)
    Row(
        Modifier.fillMaxWidth().padding(top = 5.dp).height(18.dp).drawBehind {
            drawLine(p.accent, Offset(0f, size.height / 2), Offset(size.width, size.height / 2), 1.dp.toPx())
        },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(Modifier.horizontalScroll(rememberScrollState()).background(p.window), verticalAlignment = Alignment.CenterVertically) {
            for (topic in topics) {
                Box(Modifier.padding(start = 2.dp, end = 5.dp).size(8.dp).background(p.tag(state.colors.of(topic.tag))))
                Caps(topic.tag.ifEmpty { stringResource(R.string.no_tag) }, 12f, p.inkSecondary)
                Caps(topic.count.toString(), 12f, p.ink, Modifier.padding(start = 4.dp, end = 12.dp))
            }
        }
    }
}

@Composable
private fun Bracket(left: Boolean) {
    val ink = LocalPalette.current.ink
    Canvas(Modifier.width(8.dp).height(64.dp)) {
        val w = 3.dp.toPx()
        val x = if (left) w / 2 else size.width - w / 2
        val far = if (left) size.width else 0f
        drawLine(ink, Offset(x, 0f), Offset(x, size.height), w)
        drawLine(ink, Offset(x, w / 2), Offset(far, w / 2), w)
        drawLine(ink, Offset(x, size.height - w / 2), Offset(far, size.height - w / 2), w)
    }
}

/**
 * One day of the calendar (the desktop's DayCell): filled with its tags' colours – in slanted bands when there are
 * several, as wide as the tag has notes – hatched when it is past (week band only), framed in dashed yellow when
 * it is today, underlined when selected; dots for the number of notes, the open tasks in the corner.
 */
@Composable
fun DayCell(
    day: CalendarDay,
    colors: TagColors,
    today: Boolean,
    past: Boolean,
    selected: Boolean,
    dimmed: Boolean,
    band: Boolean,
    height: Dp,
    numberSize: Float,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val p = LocalPalette.current
    val measurer = rememberTextMeasurer()
    val tags = day.tags.take(4)
    val full = tags.map { colors.of(it.tag) }
    val fills = full.map { if (band) p.calendarTint(it) else it }
    val hatched = band && past
    val numberColor = when {
        tags.isEmpty() -> if (hatched) p.inkSecondary else p.inkTertiary
        hatched -> p.pastInk(full.first())
        else -> p.foregroundOn(fills.first())
    }
    val description = day.date.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.FULL)) + ", " + day.noteCount
    Canvas(
        modifier.height(height).clipToBounds().alpha(if (dimmed) 0.2f else 1f)
            .semantics { contentDescription = description }
            .clickable(role = Role.Button, onClick = onClick),
    ) {
        val w = size.width
        val h = size.height
        if (fills.isEmpty()) {
            drawRect(p.emptyCell)
        } else {
            drawRect(p.tag(fills.first()))
            val total = tags.sumOf { it.count }.toFloat()
            val slant = h * 0.45f
            var edge = 0f
            for (i in 1 until fills.size) {
                edge += tags[i - 1].count / total * w
                val band = Path().apply {
                    moveTo(edge + slant / 2, 0f)
                    lineTo(w, 0f)
                    lineTo(w, h)
                    lineTo(edge - slant / 2, h)
                    close()
                }
                drawPath(band, p.tag(fills[i]))
            }
        }
        if (hatched) hatch(p.pastHatch)

        val number = measurer.measure(day.date.dayOfMonth.toString(), Typo.number(numberSize, numberColor))
        drawText(number, topLeft = Offset((w - number.size.width) / 2, (h - number.size.height) / 2 - h * 0.06f))

        if (day.noteCount > 1) {
            val dot = 4.dp.toPx()
            val gap = 2.dp.toPx()
            val n = minOf(day.noteCount, 5)
            var x = (w - (n * dot + (n - 1) * gap)) / 2
            repeat(n) {
                drawRect(numberColor.copy(alpha = if (hatched) 1f else 0.8f), Offset(x, h - dot - 5.dp.toPx()), Size(dot, dot))
                x += dot + gap
            }
        }
        if (day.openTasks > 0 && fills.isNotEmpty()) {
            val ink = if (hatched) p.pastInk(full.last()) else p.foregroundOn(fills.last())
            val count = measurer.measure(day.openTasks.toString(), Typo.header(10f, ink, 0f))
            val box = 6.dp.toPx()
            val right = w - 4.dp.toPx()
            drawText(count, topLeft = Offset(right - count.size.width, 2.dp.toPx()))
            drawRect(ink, Offset(right - count.size.width - box - 2.dp.toPx(), 2.dp.toPx() + (count.size.height - box) / 2), Size(box, box), style = Stroke(1.dp.toPx()))
        }

        if (today) {
            val stroke = 3.dp.toPx()
            val inset = Offset(stroke / 2, stroke / 2)
            val frame = Size(w - stroke, h - stroke)
            drawRect(p.todayFrame, inset, frame, style = Stroke(stroke))
            drawRect(p.todayFrameDash, inset, frame, style = Stroke(stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(stroke * 2, stroke * 2))))
        } else if (selected) {
            val stroke = 3.dp.toPx()
            drawLine(p.ink, Offset(0f, h - stroke / 2), Offset(w, h - stroke / 2), stroke)
        }
    }
}

/** Thin slanted stripes over a past day – it reads as "done", the colours stay. */
private fun DrawScope.hatch(color: Color) {
    val step = 5.dp.toPx()
    clipRect {
        var x = -size.height
        while (x < size.width) {
            drawLine(color, Offset(x, size.height), Offset(x + size.height, 0f), 2.dp.toPx())
            x += step
        }
    }
}

/** The whole month: weekday bubbles, a numbered tag for every ISO week, the days in full colour. */
@Composable
fun MonthGrid(state: UiState, a: Actions, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    val measurer = rememberTextMeasurer()
    val gutter = 32.dp
    val locale = currentLocale()
    val thisMonth = YearMonth.from(state.today) == state.month
    Column(modifier.fillMaxWidth()) {
        Row(Modifier.padding(start = gutter, bottom = 5.dp)) {
            for (i in 0..6) {
                val weekday = java.time.DayOfWeek.of(i + 1)
                val current = thisMonth && state.today.dayOfWeek == weekday
                val label = weekday.getDisplayName(TextStyle.SHORT_STANDALONE, locale).trimEnd('.').uppercase(locale)
                val fill = if (current) p.todayFill else p.labelGrey
                val text = measurer.measure(label, Typo.caps(10.5f, if (current) p.todayInk else p.labelInk))
                Canvas(Modifier.weight(1f).padding(horizontal = 3.dp).height(18.dp)) {
                    val body = 14.dp.toPx()
                    drawRoundRect(fill, size = Size(size.width, body), cornerRadius = androidx.compose.ui.geometry.CornerRadius(2.dp.toPx()))
                    drawPath(
                        Path().apply {
                            moveTo(size.width / 2 - 4.dp.toPx(), body)
                            lineTo(size.width / 2 + 4.dp.toPx(), body)
                            lineTo(size.width / 2, size.height)
                            close()
                        },
                        fill,
                    )
                    drawText(text, topLeft = Offset((size.width - text.size.width) / 2, (body - text.size.height) / 2))
                }
            }
        }
        for (monday in CalendarModel.weeksOfMonth(state.month)) {
            val currentWeek = CalendarModel.monday(state.today) == monday
            Row(Modifier.padding(bottom = 3.dp), verticalAlignment = Alignment.CenterVertically) {
                val fill = if (currentWeek) p.todayFill else p.labelGrey
                val text = measurer.measure(CalendarModel.isoWeek(monday).toString(), Typo.caps(11f, if (currentWeek) p.todayInk else p.labelInk))
                Canvas(Modifier.width(gutter).height(18.dp).padding(end = 4.dp)) {
                    val body = size.width - 6.dp.toPx()
                    drawRect(fill, size = Size(body, size.height))
                    drawPath(
                        Path().apply {
                            moveTo(body, 0f)
                            lineTo(size.width, size.height / 2)
                            lineTo(body, size.height)
                            close()
                        },
                        fill,
                    )
                    drawText(text, topLeft = Offset((body - text.size.width) / 2, (size.height - text.size.height) / 2))
                }
                for (day in state.calendar.week(monday)) {
                    if (YearMonth.from(day.date) != state.month) {
                        Spacer(Modifier.weight(1f))
                    } else {
                        DayCell(
                            day, state.colors,
                            today = day.date == state.today,
                            past = day.date < state.today,
                            selected = day.date == state.selectedDay,
                            dimmed = state.tagFilter != null && !day.hasTag(state.tagFilter),
                            band = false, height = 38.dp, numberSize = 16f,
                            onClick = { a.selectDay(day.date) },
                            modifier = Modifier.weight(1f),
                        )
                    }
                }
            }
        }
    }
}

/** For the history's chip and headers: "17. 11." / "11/17", with the year when it is not this one. */
fun shortDate(day: LocalDate, today: LocalDate, locale: Locale): String {
    val pattern = if (locale.language == "sk") "d. M." else "M/d"
    val text = day.format(DateTimeFormatter.ofPattern(pattern))
    return if (day.year == today.year) text else "$text ${day.year}"
}
