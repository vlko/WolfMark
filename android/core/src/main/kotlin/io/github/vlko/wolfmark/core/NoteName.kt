package io.github.vlko.wolfmark.core

/**
 * A note's file name, as the desktop's `NoteName`: `<tag>_<HHmm>[ss][-n][-CONFLICT].md` or without the tag.
 * `-n` (2 and up) tells notes of the same moment apart; any other suffix marks a OneDrive conflict copy.
 * Checked against `testdata/note-names.json`, like the desktop.
 */
data class NoteName(
    val tag: String?,
    val hour: Int,
    val minute: Int,
    val second: Int = 0,
    val hasSeconds: Boolean = false,
    val suffix: Int = 0,
    val conflictMarker: String? = null,
) {
    val isConflictCopy: Boolean get() = conflictMarker != null

    val fileName: String
        get() = buildString {
            if (tag != null) append(tag).append('_')
            append(two(hour)).append(two(minute))
            if (hasSeconds) append(two(second))
            if (suffix >= 2) append('-').append(suffix)
            if (conflictMarker != null) append('-').append(conflictMarker)
            append(EXTENSION)
        }

    /** The next name to try when this one is taken: first with seconds, then -2, -3, … */
    fun nextCollisionCandidate(secondsIfMissing: Int): NoteName =
        if (!hasSeconds) copy(second = secondsIfMissing, hasSeconds = true, suffix = 0)
        else copy(suffix = if (suffix == 0) 2 else suffix + 1)

    companion object {
        const val EXTENSION = ".md"

        private fun two(n: Int) = n.toString().padStart(2, '0')

        fun create(tag: String?, hour: Int, minute: Int): NoteName {
            val normalized = tag?.let(TagName::normalize)
            require(normalized == null || TagName.isValid(normalized)) { "Invalid tag '$tag'." }
            return NoteName(normalized, hour, minute)
        }

        fun parse(fileName: String): NoteName? {
            if (!fileName.endsWith(EXTENSION, ignoreCase = true)) return null
            var stem = fileName.dropLast(EXTENSION.length)
            if (stem.isEmpty()) return null

            var tag: String? = null
            val separator = stem.indexOf('_')
            if (separator >= 0) {
                val candidate = TagName.normalize(stem.substring(0, separator))
                if (!TagName.isValid(candidate)) return null
                tag = candidate
                stem = stem.substring(separator + 1)
            }

            var digits = 0
            while (digits < stem.length && digits < 6 && stem[digits] in '0'..'9') digits++
            if (digits != 4 && digits != 6) return null
            val hour = stem.substring(0, 2).toInt()
            val minute = stem.substring(2, 4).toInt()
            val second = if (digits == 6) stem.substring(4, 6).toInt() else 0
            if (hour > 23 || minute > 59 || second > 59) return null

            var rest = stem.substring(digits)
            var suffix = 0
            var conflict: String? = null
            if (rest.isNotEmpty()) {
                if (rest[0] != '-' || rest.length == 1) return null
                rest = rest.substring(1)
                val dash = rest.indexOf('-')
                val first = if (dash >= 0) rest.substring(0, dash) else rest
                if (isCollisionNumber(first)) {
                    suffix = first.toInt()
                    rest = if (dash >= 0) rest.substring(dash + 1) else ""
                    if (dash >= 0 && rest.isEmpty()) return null
                }
                if (rest.isNotEmpty()) {
                    if (!isValidConflictMarker(rest)) return null
                    conflict = rest
                }
            }

            return NoteName(tag, hour, minute, second, digits == 6, suffix, conflict)
        }

        private fun isCollisionNumber(s: String): Boolean =
            s.isNotEmpty() && s.length <= 4 && s[0] != '0' && s.all { it in '0'..'9' } && s != "1"

        private fun isValidConflictMarker(s: String): Boolean =
            s.isNotEmpty() && s.length <= 64 && s.any { it !in '0'..'9' } &&
                s.none { it.isWhitespace() || it in "\\/:*?\"<>|." }
    }
}
