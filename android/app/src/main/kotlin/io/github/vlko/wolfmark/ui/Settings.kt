package io.github.vlko.wolfmark.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import io.github.vlko.wolfmark.Actions
import io.github.vlko.wolfmark.MainViewModel
import io.github.vlko.wolfmark.R
import io.github.vlko.wolfmark.UiState
import io.github.vlko.wolfmark.ui.theme.Caps
import io.github.vlko.wolfmark.ui.theme.Icons
import io.github.vlko.wolfmark.ui.theme.LocalPalette
import io.github.vlko.wolfmark.ui.theme.Typo
import kotlin.math.roundToInt

/** Settings in the desktop's manner: caps section headings, plain fields, flat buttons – on one scrolling page. */
@Composable
fun SettingsScreen(state: UiState, a: Actions) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().height(48.dp).padding(start = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            IconBtn(Icons.ChevronLeft, stringResource(R.string.back), a::closeSettings, size = 22.dp)
            Text(stringResource(R.string.settings).uppercase(), Modifier.padding(start = 4.dp), style = Typo.header(19f, p.inkSecondary))
        }
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(start = 18.dp, end = 18.dp, bottom = 24.dp)) {
            Section(stringResource(R.string.settings_account))
            Text(state.account ?: "–", style = Typo.body(15f, p.ink))
            Spacer(Modifier.height(8.dp))
            FlatBtn(stringResource(R.string.sign_out), a::signOut)

            Section(stringResource(R.string.choose_folder))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Folder, null, Modifier.size(15.dp), tint = p.inkSecondary)
                Text("OneDrive / " + state.vault.replace("/", " / "), Modifier.padding(start = 8.dp), style = Typo.body(15f, p.ink))
            }
            Spacer(Modifier.height(8.dp))
            FlatBtn(stringResource(R.string.folder_change), a::chooseFolder)

            Section(stringResource(R.string.settings_appearance))
            Text(stringResource(R.string.settings_theme), style = Typo.body(13.5f, p.inkSecondary))
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                for ((value, label) in listOf("system" to R.string.theme_system, "light" to R.string.theme_light, "dark" to R.string.theme_dark)) {
                    val active = state.theme == value
                    FlatBtn(stringResource(label), { a.setTheme(value) }, fill = if (active) p.subtleFillHover else p.subtleFill, ink = if (active) p.ink else p.inkTertiary)
                }
            }
            Text(stringResource(R.string.settings_text_size), Modifier.padding(top = 14.dp), style = Typo.body(13.5f, p.inkSecondary))
            Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FlatBtn("A−", { a.setTextScale(state.textScale - 0.1f, true) }, enabled = state.textScale > MainViewModel.MIN_TEXT_SCALE + 0.01f)
                Caps("${(state.textScale * 100).roundToInt()} %", 14f, p.ink)
                FlatBtn("A+", { a.setTextScale(state.textScale + 0.1f, true) }, enabled = state.textScale < MainViewModel.MAX_TEXT_SCALE - 0.01f)
                FlatBtn(stringResource(R.string.reset), { a.setTextScale(1f, true) }, ink = p.inkSecondary)
            }
            Hint(stringResource(R.string.settings_text_size_hint))

            Section(stringResource(R.string.settings_sharing))
            Text(
                if (state.pendingShares == 0) stringResource(R.string.pending_none) else pluralStringResource(R.plurals.pending_shares, state.pendingShares, state.pendingShares),
                style = Typo.body(15f, p.ink),
            )
            if (state.pendingShares > 0) {
                Spacer(Modifier.height(8.dp))
                FlatBtn(stringResource(R.string.send_now), a::sendPending)
            }
            Text(stringResource(R.string.settings_computers), Modifier.padding(top = 14.dp), style = Typo.body(13.5f, p.inkSecondary))
            if (state.devices.isEmpty()) {
                Hint(stringResource(R.string.settings_no_computers))
            } else {
                for (device in state.devices) {
                    Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                        Caps(device.name, 14f, p.ink)
                        Text(device.address, Modifier.padding(start = 10.dp), style = Typo.body(12.5f, p.inkTertiary))
                    }
                }
                Hint(stringResource(R.string.settings_computers_hint))
            }

            Section(stringResource(R.string.settings_about))
            Text("WolfMark ${state.version}", style = Typo.body(15f, p.ink))
            Hint(stringResource(R.string.settings_about_text))
        }
    }
}

@Composable
private fun Section(title: String) {
    Caps(title, 14f, LocalPalette.current.ink, Modifier.padding(top = 22.dp, bottom = 8.dp))
}

@Composable
private fun Hint(text: String) {
    Text(text, Modifier.padding(top = 6.dp), style = Typo.body(12.5f, LocalPalette.current.inkTertiary))
}
