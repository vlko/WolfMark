package io.github.vlko.wolfmark.core

import java.text.Normalizer

/** Tag rules of the desktop (`WolfMark.Core.Tags.TagName`): letters, digits and '-', NFC, at most 64 characters. */
object TagName {
    const val MAX_LENGTH = 64

    /** The tag of notes shared from the phone. */
    const val SHARE = "Share"

    fun isValid(tag: String): Boolean = tag.isNotEmpty() && tag.length <= MAX_LENGTH && tag.all(::isAllowedChar)

    private fun isAllowedChar(c: Char): Boolean =
        c == '-' || c.isLetterOrDigit() ||
            c.category == CharCategory.NON_SPACING_MARK || c.category == CharCategory.COMBINING_SPACING_MARK

    fun normalize(tag: String): String = Normalizer.normalize(tag, Normalizer.Form.NFC)

    /** Case-insensitive key: "Share" and "share" are one tag. */
    fun key(tag: String): String = normalize(tag).lowercase()
}
