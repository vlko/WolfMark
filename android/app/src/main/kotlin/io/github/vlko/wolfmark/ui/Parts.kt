package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.layout
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.Typo

/** A square button with just an icon (the desktop's IconButton, a little larger for fingers). */
@Composable
fun IconBtn(icon: ImageVector, label: String, onClick: () -> Unit, modifier: Modifier = Modifier, size: Dp = 18.dp, tint: Color = LocalPalette.current.inkSecondary, enabled: Boolean = true) {
    Box(
        modifier.size(36.dp).clip(RoundedCornerShape(3.dp)).clickable(enabled = enabled, onClickLabel = label, role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, label, Modifier.size(size), tint = if (enabled) tint else LocalPalette.current.inkTertiary)
    }
}

/** A flat, filled button with a caps label (the desktop's FlatButton). */
@Composable
fun FlatBtn(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, fill: Color = LocalPalette.current.subtleFill, ink: Color = LocalPalette.current.ink, enabled: Boolean = true, size: Float = 13.5f) {
    Box(
        modifier.height(32.dp).clip(RoundedCornerShape(3.dp)).background(fill).clickable(enabled = enabled, role = Role.Button, onClick = onClick).padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Caps(text, size, if (enabled) ink else LocalPalette.current.inkTertiary)
    }
}

/** "HISTORY ─────────": a header and a hairline to the end of the row. */
@Composable
fun SectionHeader(title: String, modifier: Modifier = Modifier, size: Float = 17f, trailing: @Composable RowScope.() -> Unit = {}) {
    val p = LocalPalette.current
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Text(title.uppercase(), style = Typo.header(size, p.inkSecondary))
        Box(Modifier.padding(start = 10.dp, top = 2.dp).weight(1f).height(1.dp).background(p.divider))
        trailing()
    }
}

/** The desktop's dotted vertical rule between a time and what it belongs to. */
@Composable
fun DottedLine(modifier: Modifier = Modifier) {
    val color = LocalPalette.current.inkTertiary
    Canvas(modifier.width(1.dp).fillMaxHeight()) {
        drawLine(color, Offset(size.width / 2, 0f), Offset(size.width / 2, size.height), strokeWidth = 1.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(2.dp.toPx(), 3.dp.toPx())))
    }
}

/** The language the app is shown in – read so that a change of language redraws what depends on it. */
@Composable
fun currentLocale(): java.util.Locale = androidx.compose.ui.platform.LocalConfiguration.current.locales[0]

/** Lays the content out turned a quarter: its width becomes its height. Follow it with `rotate(-90f)`. */
fun Modifier.vertical(): Modifier = layout { measurable, constraints ->
    val placeable = measurable.measure(constraints.copy(minWidth = 0, maxWidth = androidx.compose.ui.unit.Constraints.Infinity, minHeight = 0))
    layout(placeable.height, placeable.width) {
        placeable.place(-(placeable.width / 2 - placeable.height / 2), -(placeable.height / 2 - placeable.width / 2))
    }
}
