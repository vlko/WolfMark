package io.github.vlko.wolfmark.ui.theme

import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import io.github.vlko.wolfmark.core.TagColors

/**
 * The desktop's palette (`Theming/Palette` in both apps, ADR 0004): one flat surface, ink in three strengths, one
 * accent. The same values, so the phone and the computer look like one app.
 */
@Immutable
data class Palette(
    val dark: Boolean,
    val window: Color,
    val ink: Color,
    val inkSecondary: Color,
    val inkTertiary: Color,
    val subtleFill: Color,
    val subtleFillHover: Color,
    val accent: Color,
    val todayFill: Color,
    val todayInk: Color,
    val divider: Color,
    val emptyCell: Color,
    val labelGrey: Color,
    val labelInk: Color,
    val risoPink: Color,
    val risoYellow: Color,
    val todayFrame: Color,
    val todayFrameDash: Color,
    val pastHatch: Color,
    // The editor's colours (`EditorTheme`).
    val text: Color,
    val muted: Color,
    val heading: Color,
    val link: Color,
    val code: Color,
    val codeBackground: Color,
    val quote: Color,
    val error: Color,
) {
    private val emptyRgb: Int get() = rgbOf(emptyCell)

    fun tag(rgb: Int): Color = Color(0xFF000000 or rgb.toLong())

    /** A tag colour as the week band shows it: softened towards the empty cell. */
    fun calendarTint(rgb: Int): Int = TagColors.blend(rgb, emptyRgb, if (dark) 0.25 else 0.4)

    /** The number of a hatched past day: the tag's colour, darkened (lightened in the dark theme). */
    fun pastInk(rgb: Int): Color = tag(if (dark) TagColors.blend(rgb, 0xFFFFFF, 0.8) else TagColors.blend(rgb, 0x000000, 0.6))

    fun foregroundOn(rgb: Int): Color = tag(TagColors.foregroundOn(rgb))

    companion object {
        private fun rgbOf(c: Color): Int =
            ((c.red * 255f + 0.5f).toInt() shl 16) or ((c.green * 255f + 0.5f).toInt() shl 8) or (c.blue * 255f + 0.5f).toInt()

        val Light = Palette(
            dark = false,
            window = Color(0xFFFAF9F4), ink = Color(0xFF1F1F1E), inkSecondary = Color(0xFF6B6862), inkTertiary = Color(0xFFA8A49B),
            subtleFill = Color(0xFFEFEDE6), subtleFillHover = Color(0xFFE5E2D9), accent = Color(0xFF1E88F5),
            todayFill = Color(0xFF1F1F1E), todayInk = Color(0xFFFFFFFF), divider = Color(0xFFE4E0D6), emptyCell = Color(0xFFEEECE5),
            labelGrey = Color(0xFFC6C9CE), labelInk = Color(0xFFFFFFFF), risoPink = Color(0xFFF49CBB), risoYellow = Color(0xFFF6D35B),
            todayFrame = Color(0xFFF6C62F), todayFrameDash = Color(0xFF1F1F1E), pastHatch = Color(0x6EFFFFFF),
            text = Color(0xFF232220), muted = Color(0xFFA9A399), heading = Color(0xFF161513), link = Color(0xFF2F6FDF),
            code = Color(0xFF8C4F3D), codeBackground = Color(0xFFEBE5DC), quote = Color(0xFF6B665E), error = Color(0xFFCD5C5C),
        )

        val Dark = Palette(
            dark = true,
            window = Color(0xFF1A1B1D), ink = Color(0xFFEEECE7), inkSecondary = Color(0xFFAAA7A0), inkTertiary = Color(0xFF706D68),
            subtleFill = Color(0xFF2A2C30), subtleFillHover = Color(0xFF35373C), accent = Color(0xFF4FA3FF),
            todayFill = Color(0xFFEEECE7), todayInk = Color(0xFF1A1B1D), divider = Color(0xFF34363A), emptyCell = Color(0xFF25272A),
            labelGrey = Color(0xFF46494E), labelInk = Color(0xFFE6E6E6), risoPink = Color(0xFFC24F7D), risoYellow = Color(0xFFB8942A),
            todayFrame = Color(0xFFF6C62F), todayFrameDash = Color(0xFF0E0F10), pastHatch = Color(0x5C1A1B1D),
            text = Color(0xFFECE8E1), muted = Color(0xFF7C776F), heading = Color(0xFFFAF7F2), link = Color(0xFF7FAEFF),
            code = Color(0xFFE3B4A0), codeBackground = Color(0xFF33312F), quote = Color(0xFFB5AFA5), error = Color(0xFFE07A7A),
        )
    }
}

val LocalPalette = staticCompositionLocalOf { Palette.Light }
