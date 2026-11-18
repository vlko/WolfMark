package io.github.vlko.wolfmark

import com.github.takahirom.roborazzi.captureRoboImage
import io.github.vlko.wolfmark.core.CalendarModel
import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.NotePath
import io.github.vlko.wolfmark.core.TagColors
import io.github.vlko.wolfmark.ui.WolfMarkScreen
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.time.LocalDate
import java.time.YearMonth
import java.time.ZoneId

/**
 * Draws the screens on the JVM (Robolectric + Roborazzi), with the app's own fonts, colours and strings, on notes
 * like the desktop's demo. `./gradlew :app:testDebugUnitTest -Pscreenshots` writes them to
 * `build/outputs/roborazzi` – to look at, and to compare with the desktop's screenshots in `docs/screenshots`.
 * Without `-Pscreenshots` the test only checks that every screen can be drawn.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [36], qualifiers = "w360dp-h780dp-xxhdpi")
class ScreenshotTest {
    private val today = LocalDate.of(2026, 11, 5)

    private fun note(day: Int, name: String, title: String, preview: String, editedHour: Int, open: Int = 0, done: Int = 0): MirrorNote {
        val path = NotePath.parse("2026/11/%02d/%s.md".format(day, name))!!
        val edited = LocalDate.of(2026, 11, day).atTime(editedHour, 28).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
        return MirrorNote(path, title, preview, edited, open, done)
    }

    private val notes = listOf(
        note(5, "work_2208", "Calendar polish before 1.1", "Goal for this week: the calendar should explain the week at a glance.", 22, open = 3, done = 3),
        note(5, "meeting_2018", "Stand-up Thu 5 Nov", "Follow up on the OneDrive conflict report · Measure start-up time", 20, open = 2, done = 1),
        note(5, "ClaudeCode_1631", "infra-terraform", "Plan the state migration for the staging account", 16),
        note(5, "Share_1203", "example.com", "https://example.com/an-article-worth-reading", 12),
        note(4, "ClaudeCode_0900", "billing-api", "Invoice endpoint returns 500 for empty carts", 9),
        note(4, "1100", "Loose thought", "Without a tag", 11),
        note(4, "meeting_1400", "Planning", "Scope of 1.1", 14),
        note(4, "work_1700", "Review notes", "PR 214, PR 220", 17),
        note(3, "0800", "Morning pages", "", 8),
        note(3, "meeting_1000", "1:1", "Career, on-call", 10),
        note(3, "meeting_1500", "Retro", "What went well", 15),
        note(2, "ClaudeCode_1000", "wolfmark", "History pane paging", 10),
        note(2, "1200", "Groceries", "", 12),
        note(2, "meeting_1300", "Design sync", "", 13),
        note(2, "reading_2100", "Designing Data-Intensive Applications", "Chapter 7 – transactions", 21),
        note(1, "family_1000", "Weekend plan", "", 10),
        note(12, "release_0900", "v1.1 checklist", "", 9),
    )

    private val colors = TagColors.parse(
        """{"version":1,"tags":[{"name":"work","color":"#2F6FEB"},{"name":"meeting","color":"#F2B705"},{"name":"ideas","color":"#8E44AD"},
           {"name":"reading","color":"#16A085"},{"name":"family","color":"#E84A5F"},{"name":"release","color":"#00A884"}]}""",
    )

    private val markdown = """
        # Calendar polish before 1.1

        Goal for this week: the calendar should explain the week *at a glance*.

        ## Done
        - [x] Week strip shows counts per tag
        - [x] Hatched past days, dashed frame for today
        - [ ] Labels between weeks in the month view

        > A calendar is a picture of what you did.

        See [the plan](https://example.com) and `CalendarPanel.cs`.

        | Area | Status |
        |---|---|
        | Week strip | done |
        | Month labels | review |
    """.trimIndent()

    private val state = UiState(
        screen = Screen.NOTES,
        account = "someone@outlook.com",
        notes = notes,
        calendar = CalendarModel(notes),
        colors = colors,
        tags = listOf("meeting", "ClaudeCode", "work", "reading", "family", "release", "Share"),
        today = today,
        selectedDay = today,
        week = CalendarModel.monday(today),
        month = YearMonth.from(today),
        open = OpenNote(notes.first(), markdown),
        lastSync = java.time.LocalTime.of(22, 48),
        pendingShares = 1,
        devices = listOf(DeviceLine("DESKTOP-VLKO", "192.168.1.20:47821")),
        version = "0.1.0",
    )

    private val nothing = object : Actions {}

    private fun shot(name: String, state: UiState) =
        captureRoboImage("build/outputs/roborazzi/$name.png") { WolfMarkScreen(state, nothing) }

    @Test
    fun weekLight() = shot("week-light", state.copy(theme = "light"))

    @Test
    fun weekDark() = shot("week-dark", state.copy(theme = "dark"))

    @Test
    fun weekFilteredByTagAndDay() = shot("week-filtered", state.copy(theme = "light", tagFilter = "meeting", dayFilter = true, selectedDay = today.minusDays(1)))

    @Test
    fun month() = shot("month", state.copy(theme = "light", view = CalendarView.MONTH))

    @Test
    fun noteOnly() = shot("note-only", state.copy(theme = "light", fullNote = true, textScale = 1.3f))

    @Test
    fun settings() = shot("settings", state.copy(theme = "light", screen = Screen.SETTINGS))

    @Test
    @Config(qualifiers = "sk-w360dp-h780dp-xxhdpi")
    fun weekInSlovak() = shot("week-sk", state.copy(theme = "light"))

    @Test
    fun signIn() = shot("sign-in", UiState(screen = Screen.SIGN_IN, theme = "light"))

    @Test
    fun emptyNotebook() = shot("empty", UiState(screen = Screen.NOTES, theme = "light", today = today, selectedDay = today, week = CalendarModel.monday(today)))
}
