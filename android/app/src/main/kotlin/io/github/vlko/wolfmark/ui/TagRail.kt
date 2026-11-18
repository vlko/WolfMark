package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.core.TagColors
import io.github.vlko.wolfmark.core.TagName
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.Typo

/**
 * The tags as vertical tabs at the right edge (the desktop's TagRail): "ALL" first, then every tag that has notes
 * in its colour, most used first. The active tab stands out towards the content; tapping it again clears the filter.
 */
@Composable
fun TagRail(tags: List<String>, colors: TagColors, active: String?, onSelect: (String?) -> Unit, modifier: Modifier = Modifier) {
    val p = LocalPalette.current
    Column(
        modifier.width(40.dp).fillMaxHeight().verticalScroll(rememberScrollState()).padding(top = 4.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
        horizontalAlignment = Alignment.End,
    ) {
        Tab(stringResource(R.string.all_tags), p.inkTertiary, Color.White, active == null) { onSelect(null) }
        for (tag in tags) {
            val rgb = colors.of(tag)
            Tab(tag, p.tag(rgb), p.foregroundOn(rgb), TagName.key(tag) == active) { onSelect(tag) }
        }
    }
}

@Composable
private fun Tab(label: String, fill: Color, ink: Color, active: Boolean, onClick: () -> Unit) {
    Box(
        Modifier
            .padding(end = if (active) 8.dp else 2.dp)
            .width(30.dp)
            .defaultMinSize(minHeight = 64.dp)
            .clip(RoundedCornerShape(4.dp))
            .background(fill)
            .semantics { selected = active }
            .clickable(role = Role.Tab, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            label.uppercase(),
            Modifier.vertical().rotate(-90f).padding(horizontal = 12.dp),
            style = Typo.caps(14f, ink).copy(letterSpacing = androidx.compose.ui.unit.TextUnit(0.4f, androidx.compose.ui.unit.TextUnitType.Sp)),
            maxLines = 1,
        )
    }
}
