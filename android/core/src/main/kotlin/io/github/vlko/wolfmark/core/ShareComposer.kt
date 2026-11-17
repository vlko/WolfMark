package io.github.vlko.wolfmark.core

import java.net.URI

/**
 * Turns what another app shared into the note's Markdown – the same text whichever way it then travels.
 *
 *  - a link alone: a heading (the shared subject, else the site) and the link;
 *  - a link with text (a selection shared from the browser): heading, link, the text as a quote;
 *  - text: stored whole, as it came; its first line is the title (a subject that is not already there goes on top);
 *  - images: after the text, as `wolfmark-attachment:n` until they have a place in `assets`.
 */
object ShareComposer {
    private val url = Regex("""https?://\S+""", RegexOption.IGNORE_CASE)

    fun compose(subject: String?, text: String?, imageCount: Int): String {
        val title = subject?.trim()?.lineSequence()?.firstOrNull()?.trim().orEmpty()
        val body = text?.replace("\r\n", "\n")?.trim().orEmpty()
        val out = StringBuilder()

        val lines = body.lines()
        val linkLine = lines.indexOfLast { url.matches(it.trim()) }
        val onlyLink = lines.size == 1 && linkLine == 0
        // A link on its own first or last line with something else around it: "selected text" + URL.
        val linkWithText = !onlyLink && linkLine >= 0 && (linkLine == lines.lastIndex || linkLine == 0) &&
            lines.count { url.matches(it.trim()) } == 1 && body.length <= 4000

        when {
            body.isEmpty() -> if (title.isNotEmpty()) out.append("# ").append(title).append('\n')
            onlyLink -> {
                out.append("# ").append(title.ifEmpty { siteName(body) }).append('\n').append(body).append('\n')
            }
            linkWithText -> {
                val link = lines[linkLine].trim()
                val rest = lines.filterIndexed { i, _ -> i != linkLine }.joinToString("\n").trim().trim('"', '“', '”')
                out.append("# ").append(title.ifEmpty { siteName(link) }).append('\n').append(link).append("\n\n")
                rest.lines().forEach { out.append("> ").append(it).append('\n') }
            }
            else -> {
                if (title.isNotEmpty() && !body.contains(title)) out.append("# ").append(title).append("\n\n")
                out.append(body).append('\n')
            }
        }

        if (imageCount > 0) {
            if (out.isNotEmpty()) out.append('\n')
            repeat(imageCount) { out.append("![](").append(ShareMessage.ATTACHMENT_PREFIX).append(it).append(")\n") }
        }
        return out.toString()
    }

    /** Puts the real links where the placeholders are – highest number first, so 1 does not eat the start of 10. */
    fun withLinks(markdown: String, links: List<String>): String =
        links.indices.reversed().fold(markdown) { text, i -> text.replace(ShareMessage.ATTACHMENT_PREFIX + i, links[i]) }

    private fun siteName(link: String): String =
        runCatching { URI(link).host }.getOrNull()?.removePrefix("www.")?.takeIf { it.isNotEmpty() } ?: link
}
