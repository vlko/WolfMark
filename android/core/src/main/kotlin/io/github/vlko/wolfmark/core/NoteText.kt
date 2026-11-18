package io.github.vlko.wolfmark.core

/**
 * Title and preview of a note for the list: the first line with text after the front matter, then what follows.
 * A simplified cousin of the desktop's `NoteContentAnalyzer` (which runs the full Markdown lexer): the common
 * syntax is stripped, exotic lines may read slightly differently than on the desktop.
 */
object NoteText {
    const val TITLE_LENGTH = 120
    const val PREVIEW_LENGTH = 200

    data class Summary(val title: String, val preview: String, val openTasks: Int = 0, val doneTasks: Int = 0)

    /** The note's text without a leading YAML front matter block. */
    fun body(content: String): String {
        val lines = content.replace("\r\n", "\n").split('\n')
        if (lines.firstOrNull()?.trimEnd() != "---") return content
        val end = (1 until lines.size).firstOrNull { lines[it].trimEnd() == "---" || lines[it].trimEnd() == "..." }
            ?: return content
        return lines.drop(end + 1).joinToString("\n")
    }

    fun summarize(content: String): Summary {
        var title: String? = null
        val preview = StringBuilder()
        var inCode = false
        var open = 0
        var done = 0
        for (line in body(content).split('\n')) {
            val trimmed = line.trim()
            if (trimmed.startsWith("```") || trimmed.startsWith("~~~")) {
                inCode = !inCode
                continue
            }
            if (!inCode) {
                task.find(trimmed)?.let { if (it.groupValues[1] == " ") open++ else done++ }
            }
            val plain = if (inCode) trimmed else strip(trimmed)
            if (plain.isEmpty()) continue
            if (title == null) {
                title = plain.take(TITLE_LENGTH)
            } else if (preview.length < PREVIEW_LENGTH) {
                if (preview.isNotEmpty()) preview.append(' ')
                preview.append(plain)
            }
        }
        return Summary(title.orEmpty(), preview.toString().take(PREVIEW_LENGTH), open, done)
    }

    private val task = Regex("""^(?:>\s?)*[-*+]\s+\[([ xX])]\s""")
    private val image = Regex("""!\[([^\]]*)]\([^)]*\)""")
    private val link = Regex("""\[([^\]]*)]\([^)]*\)""")
    private val wiki = Regex("""\[\[([^\]|]*)(?:\|([^\]]*))?]]""")
    private val leading = Regex("""^(?:#{1,6}\s+|>\s?|[-*+]\s+\[[ xX]]\s+|[-*+]\s+|\d+[.)]\s+)+""")
    private val rule = Regex("""^(?:[-*_]\s*){3,}$""")
    private val tableRule = Regex("""^\|?[\s:|-]+\|?$""")

    private fun strip(line: String): String {
        if (rule.matches(line) || (line.contains('-') && line.contains('|') && tableRule.matches(line))) return ""
        var s = leading.replace(line, "")
        s = image.replace(s, "$1")
        s = link.replace(s, "$1")
        s = wiki.replace(s) { m -> m.groupValues[2].ifEmpty { m.groupValues[1] } }
        s = s.replace("**", "").replace("__", "").replace("~~", "").replace("`", "")
        s = s.replace(Regex("""(?<![\w*])\*(?=\S)|(?<=\S)\*(?![\w*])"""), "")
        if (s.startsWith("|") || s.endsWith("|")) {
            s = s.trim('|').split('|').map { it.trim() }.filter { it.isNotEmpty() }.joinToString(" · ")
        }
        return s.trim()
    }
}
