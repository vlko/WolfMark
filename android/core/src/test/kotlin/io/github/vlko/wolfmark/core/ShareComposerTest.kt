package io.github.vlko.wolfmark.core

import org.junit.Assert.assertEquals
import org.junit.Test

class ShareComposerTest {
    @Test
    fun aLinkAloneGetsTheSubjectOrTheSiteAsItsTitle() {
        assertEquals("# Example Domain\nhttps://example.com/x\n", ShareComposer.compose("Example Domain", "https://example.com/x", 0))
        assertEquals("# example.com\nhttps://www.example.com/a?b=c\n", ShareComposer.compose(null, "  https://www.example.com/a?b=c\n", 0))
    }

    @Test
    fun aSelectionSharedFromTheBrowserBecomesAQuoteUnderTheLink() {
        val shared = "\"Prvý riadok výberu.\nDruhý riadok.\"\n https://example.com/clanok"
        assertEquals(
            "# Článok\nhttps://example.com/clanok\n\n> Prvý riadok výberu.\n> Druhý riadok.\n",
            ShareComposer.compose("Článok", shared, 0),
        )
        assertEquals(
            "# example.com\nhttps://example.com/x\n\n> look at this\n",
            ShareComposer.compose("", "https://example.com/x\nlook at this", 0),
        )
    }

    @Test
    fun aWholeTextIsStoredAsItCame() {
        val article = "# Nadpis článku\n\n" + (1..300).joinToString("\n\n") { "Odsek $it s odkazom https://example.com/$it v texte." }
        assertEquals(article + "\n", ShareComposer.compose(null, article, 0))
        // Nothing is cut, however long.
        assertEquals(article.length + 1, ShareComposer.compose(null, article, 0).length)
        // Windows line ends from the other app do not survive as stray characters.
        assertEquals("one\ntwo\n", ShareComposer.compose(null, "one\r\ntwo", 0))
    }

    @Test
    fun aSubjectThatIsNotInTheTextGoesOnTop() {
        assertEquals("# Zápis z porady\n\nBod 1\nBod 2\n", ShareComposer.compose("Zápis z porady", "Bod 1\nBod 2", 0))
        assertEquals("Zápis z porady\nBod 1\n", ShareComposer.compose("Zápis z porady", "Zápis z porady\nBod 1", 0))
    }

    @Test
    fun imagesFollowTheTextAndGetTheirLinksLater() {
        assertEquals("![](wolfmark-attachment:0)\n", ShareComposer.compose(null, null, 1))
        assertEquals("# Dovolenka\n\n![](wolfmark-attachment:0)\n![](wolfmark-attachment:1)\n", ShareComposer.compose("Dovolenka", "", 2))
        val twelve = ShareComposer.compose(null, "foto", 12)
        val links = (0 until 12).map { "assets/1403-%02d.jpg".format(it + 1) }
        val done = ShareComposer.withLinks(twelve, links)
        assertEquals("foto\n\n" + links.joinToString("") { "![]($it)\n" }, done)
    }

    @Test
    fun nothingSharedIsAnEmptyNote() {
        assertEquals("", ShareComposer.compose(null, "  ", 0))
    }
}
