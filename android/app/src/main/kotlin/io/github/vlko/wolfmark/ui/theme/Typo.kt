package io.github.vlko.wolfmark.ui.theme

import android.annotation.SuppressLint
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.vlko.wolfmark.R

/**
 * The desktop's typography (`Theming/Typo`): Barlow Condensed for everything that labels – light for the big
 * numbers, medium for caps, semi-bold for headers – Barlow ExtraBold for the calendar's day numbers, and the
 * system font for what is read.
 */
object Typo {
    val Condensed = FontFamily(
        Font(R.font.barlow_condensed_light, FontWeight.Light),
        Font(R.font.barlow_condensed_regular, FontWeight.Normal),
        Font(R.font.barlow_condensed_medium, FontWeight.Medium),
        Font(R.font.barlow_condensed_semibold, FontWeight.SemiBold),
    )
    val Numbers = FontFamily(Font(R.font.barlow_extrabold, FontWeight.ExtraBold))

    fun caps(size: Float, color: Color) = TextStyle(
        fontFamily = Condensed, fontWeight = FontWeight.Medium, fontSize = size.sp, letterSpacing = (size * 0.06f).sp, color = color,
    )

    fun header(size: Float, color: Color, spacing: Float = 0.5f) = TextStyle(
        fontFamily = Condensed, fontWeight = FontWeight.SemiBold, fontSize = size.sp, letterSpacing = spacing.sp, color = color,
    )

    fun lead(size: Float, color: Color, spacing: Float = 0f) = TextStyle(
        fontFamily = Condensed, fontWeight = FontWeight.Light, fontSize = size.sp, letterSpacing = spacing.sp, color = color,
    )

    fun body(size: Float, color: Color) = TextStyle(fontSize = size.sp, color = color)

    fun number(size: Float, color: Color) = TextStyle(fontFamily = Numbers, fontWeight = FontWeight.ExtraBold, fontSize = size.sp, color = color)
}

/** A label in capitals, as everywhere on the desktop. */
// The modifier comes last here: text, size and colour are what every call gives.
@SuppressLint("ModifierParameter")
@Composable
fun Caps(text: String, size: Float = 12.5f, color: Color = LocalPalette.current.inkSecondary, modifier: Modifier = Modifier) {
    Text(text.uppercase(), modifier, style = Typo.caps(size, color), maxLines = 1, overflow = TextOverflow.Ellipsis)
}

/** A time the desktop's way: the hour large and light, the minutes half the size beside it ("22⁰⁸"). */
@SuppressLint("ModifierParameter")
@Composable
fun LeadTime(hour: Int, minute: Int, size: Float, color: Color = LocalPalette.current.inkSecondary, modifier: Modifier = Modifier) {
    Row(modifier, verticalAlignment = Alignment.Bottom) {
        Text(hour.toString().padStart(2, '0'), style = Typo.lead(size, color))
        Text(
            minute.toString().padStart(2, '0'),
            Modifier.padding(start = 1.dp, bottom = (size * 0.12f).dp),
            style = TextStyle(fontFamily = Typo.Condensed, fontWeight = FontWeight.Normal, fontSize = (size * 0.5f).sp, color = color),
        )
    }
}

/** The big number of the day, printed like a risograph: the accent over a yellow and a pink copy slightly off. */
@Composable
fun RisoNumber(text: String, size: Float, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    Box(modifier) {
        Text(text, Modifier.offset((size * 0.035f).dp, (size * 0.03f).dp), style = Typo.lead(size, p.risoYellow))
        Text(text, Modifier.offset((-size * 0.04f).dp, (-size * 0.02f).dp), style = Typo.lead(size, p.risoPink))
        Text(text, style = Typo.lead(size, p.accent))
    }
}
