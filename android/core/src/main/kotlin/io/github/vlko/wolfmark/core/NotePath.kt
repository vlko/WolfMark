package io.github.vlko.wolfmark.core

import java.time.DateTimeException
import java.time.LocalDate

/**
 * A note's path relative to the notes folder, as the desktop's `NotePath`: `YYYY/MM/DD/<name>.md`. A Markdown file
 * elsewhere or with another name is still a note, only without a day and a tag of its own ([isStandard] is false).
 */
data class NotePath(val relativePath: String, val date: LocalDate?, val name: NoteName?) {
    val isStandard: Boolean get() = date != null && name != null
    val tag: String? get() = name?.tag
    val fileName: String get() = relativePath.substringAfterLast('/')

    companion object {
        const val ASSETS = "assets"

        /** Null for what is not a note at all: another extension, hidden and temporary files, `.wolfmark`, `assets`. */
        fun parse(relativePath: String): NotePath? {
            val rel = relativePath.replace('\\', '/').trim('/')
            if (rel.isBlank()) return null
            val segments = rel.split('/')
            val file = segments.last()
            if (file.length <= NoteName.EXTENSION.length || file[0] == '.' || file[0] == '~' ||
                !file.endsWith(NoteName.EXTENSION, ignoreCase = true)
            ) return null
            if (segments.dropLast(1).any { it.isEmpty() || it[0] == '.' || it.equals(ASSETS, ignoreCase = true) }) return null

            val date = if (segments.size == 4) parseDate(segments[0], segments[1], segments[2]) else null
            return NotePath(rel, date, if (date != null) NoteName.parse(file) else null)
        }

        fun directory(date: LocalDate): String =
            "%04d/%02d/%02d".format(date.year, date.monthValue, date.dayOfMonth)

        private fun parseDate(year: String, month: String, day: String): LocalDate? {
            if (year.length != 4 || month.length != 2 || day.length != 2) return null
            if (!(year + month + day).all { it in '0'..'9' }) return null
            val y = year.toInt()
            if (y < 1900) return null
            return try {
                LocalDate.of(y, month.toInt(), day.toInt())
            } catch (_: DateTimeException) {
                null
            }
        }
    }
}
