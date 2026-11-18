package io.github.vlko.wolfmark.core

import org.json.JSONObject
import kotlin.math.pow

/**
 * The colour of a tag, as on the desktop (`TagStore.GetColor`, `TagPalette`): the one chosen in
 * `.wolfmark/tags.json`, otherwise one of 14 picked by a stable hash of the tag – so the phone and every computer
 * agree without being told. Colours are `0xRRGGBB`. Checked against `testdata/tag-colors.json`.
 */
class TagColors(private val chosen: Map<String, Int> = emptyMap()) {

    /** [tag] null or empty = a note without a tag. */
    fun of(tag: String?): Int = if (tag.isNullOrEmpty()) UNTAGGED else chosen[TagName.key(tag)] ?: forTag(tag)

    companion object {
        val PALETTE = intArrayOf(
            0x1E88F5, 0xF7C62F, 0xF5922A, 0xF2564B, 0x5CCB86, 0x12A5B5, 0xA47BF2,
            0xF07AB8, 0x45BDF2, 0x9FD13F, 0x4C5FD7, 0xF57F72, 0xC08552, 0x6F8197,
        )
        const val CLAUDE_CODE = 0xD97757
        const val UNTAGGED = 0xC6C2B8
        const val DARK_INK = 0x1F1F1E
        const val WHITE = 0xFFFFFF

        fun forTag(tag: String?): Int {
            if (tag.isNullOrEmpty()) return UNTAGGED
            val key = TagName.key(tag)
            if (key == "claudecode") return CLAUDE_CODE
            return PALETTE[(stableHash(key) % PALETTE.size.toUInt()).toInt()]
        }

        /** FNV-1a over UTF-8. */
        fun stableHash(key: String): UInt {
            var hash = 2166136261u
            for (b in key.toByteArray(Charsets.UTF_8)) {
                hash = hash xor (b.toInt() and 0xff).toUInt()
                hash *= 16777619u
            }
            return hash
        }

        /** True when dark text reads better on this fill than white (WCAG relative luminance above 0.40). */
        fun prefersDarkForeground(rgb: Int): Boolean {
            fun channel(v: Int): Double {
                val c = v / 255.0
                return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
            }
            val luminance = 0.2126 * channel(rgb shr 16 and 0xff) + 0.7152 * channel(rgb shr 8 and 0xff) + 0.0722 * channel(rgb and 0xff)
            return luminance > 0.40
        }

        fun foregroundOn(rgb: Int): Int = if (prefersDarkForeground(rgb)) DARK_INK else WHITE

        /** [amount] of the way from [from] to [to], per channel, truncated (as the desktop's `Palette.Blend`). */
        fun blend(from: Int, to: Int, amount: Double): Int {
            fun channel(shift: Int): Int {
                val a = from shr shift and 0xff
                val b = to shr shift and 0xff
                return (a + (b - a) * amount).toInt() and 0xff
            }
            return (channel(16) shl 16) or (channel(8) shl 8) or channel(0)
        }

        fun parseHex(value: String?): Int? =
            value?.trim()?.removePrefix("#")?.takeIf { it.length == 6 }?.toIntOrNull(16)

        /** From the text of `.wolfmark/tags.json`; anything unreadable leaves the hashed colours. */
        fun parse(tagsJson: String?): TagColors {
            if (tagsJson.isNullOrBlank()) return TagColors()
            return try {
                val tags = JSONObject(tagsJson).optJSONArray("tags") ?: return TagColors()
                val chosen = HashMap<String, Int>()
                for (i in 0 until tags.length()) {
                    val t = tags.optJSONObject(i) ?: continue
                    val name = t.optString("name")
                    val color = parseHex(if (t.isNull("color")) null else t.optString("color"))
                    if (name.isNotEmpty() && color != null) chosen[TagName.key(name)] = color
                }
                TagColors(chosen)
            } catch (_: Exception) {
                TagColors()
            }
        }
    }
}
