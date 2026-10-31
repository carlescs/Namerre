using Namerre.Domain.Renaming;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class CaseStylesTests
{
    [Theory]
    [InlineData("upper", "ÀNGEL ÑANDÚ ΚΌΣΜΟΣ 東京")]
    [InlineData("lower", "àngel ñandú κόσμοσ 東京")]
    [InlineData("title", "Àngel Ñandú Κόσμοσ 東京")]
    public void ExistingModesKeepTheirBehaviour(string mode, string expected)
        => Assert.Equal(expected, CaseStyles.Apply("àNGEL ñandú ΚΌΣΜΟΣ 東京", mode));

    [Theory]
    [InlineData("tHe QUICK fox", "The quick fox")]
    [InlineData("  ÉCOLE de MUSIQUE", "  École de musique")]
    [InlineData("01 - my FILE", "01 - My file")]
    [InlineData("καλημέρα ΚΌΣΜΕ", "Καλημέρα κόσμε")]
    [InlineData("東京 STORY", "東京 story")]
    [InlineData("---", "---")]
    public void SentenceCapitalizesTheFirstLetterOnly(string stem, string expected)
        => Assert.Equal(expected, CaseStyles.Apply(stem, "sentence"));

    [Theory]
    [InlineData("the lord of the rings", "en", "The Lord of the Rings")]
    [InlineData("THE LORD OF THE RINGS", "en", "The Lord of the Rings")]
    [InlineData("what dreams are made of", "en", "What Dreams Are Made Of")]
    [InlineData("star wars: a new hope", "en", "Star Wars: A New Hope")]
    [InlineData("alien - the director's cut", "en", "Alien - The Director's Cut")]
    [InlineData("spider-man and the x-men", "en", "Spider-Man and the X-Men")]
    [InlineData("don't look up", "en", "Don't Look Up")]
    [InlineData("el señor de los anillos", "es", "El Señor de los Anillos")]
    [InlineData("la casa de papel y el ÁRBOL", "es", "La Casa de Papel y el Árbol")]
    [InlineData("l'ombra del vent", "ca", "L'Ombra del Vent")]
    [InlineData("el mar i l'àvia", "ca", "El Mar i l'Àvia")]
    [InlineData("le seigneur des anneaux", "fr", "Le Seigneur des Anneaux")]
    [InlineData("le fabuleux destin d'amélie poulain", "fr", "Le Fabuleux Destin d'Amélie Poulain")]
    [InlineData("la vita è bella", "it", "La Vita È Bella")]
    [InlineData("il nome dell'anno", "it", "Il Nome dell'Anno")]
    [InlineData("o senhor dos anéis", "pt", "O Senhor dos Anéis")]
    [InlineData("ο άρχοντας των δαχτυλιδιών", "en", "Ο Άρχοντας Των Δαχτυλιδιών")]
    [InlineData("七人の侍 of the east", "en", "七人の侍 of the East")]
    public void SmartTitleKeepsShortWordsLowercase(string stem, string language, string expected)
        => Assert.Equal(expected, CaseStyles.Apply(stem, "smartTitle", language));

    [Fact]
    public void SmartTitleShortWordsDependOnTheLanguage()
    {
        Assert.Equal("Love in the Time", CaseStyles.Apply("love in the time", "smartTitle", "en"));
        Assert.Equal("Love In The Time", CaseStyles.Apply("love in the time", "smartTitle", "fr"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("de")]
    public void SmartTitleRejectsUnknownLanguages(string language)
        => Assert.Throws<ArgumentException>(() => CaseStyles.Apply("name", "smartTitle", language));

    [Theory]
    [InlineData("My File Name", "my_file_name", "my-file-name", "myFileName", "MyFileName")]
    [InlineData("  my   file--name.. ", "my_file_name", "my-file-name", "myFileName", "MyFileName")]
    [InlineData("myFileName", "my_file_name", "my-file-name", "myFileName", "MyFileName")]
    [InlineData("XMLHttpRequest", "xml_http_request", "xml-http-request", "xmlHttpRequest", "XmlHttpRequest")]
    [InlineData("season 1 episode 02", "season_1_episode_02", "season-1-episode-02", "season1Episode02", "Season1Episode02")]
    [InlineData("Àngel Ñandú", "àngel_ñandú", "àngel-ñandú", "àngelÑandú", "ÀngelÑandú")]
    [InlineData("Ελληνικό Όνομα", "ελληνικό_όνομα", "ελληνικό-όνομα", "ελληνικόΌνομα", "ΕλληνικόΌνομα")]
    [InlineData("東京 物語", "東京_物語", "東京-物語", "東京物語", "東京物語")]
    [InlineData("don't stop", "dont_stop", "dont-stop", "dontStop", "DontStop")]
    [InlineData("l'ombra", "l_ombra", "l-ombra", "lOmbra", "LOmbra")]
    [InlineData("𐐨𐐯 word", "𐐨𐐯_word", "𐐨𐐯-word", "𐐨𐐯Word", "𐐀𐐯Word")]
    public void IdentifierStylesSplitWords(string stem, string snake, string kebab, string camel, string pascal)
    {
        Assert.Equal(snake, CaseStyles.Apply(stem, "snake"));
        Assert.Equal(kebab, CaseStyles.Apply(stem, "kebab"));
        Assert.Equal(camel, CaseStyles.Apply(stem, "camel"));
        Assert.Equal(pascal, CaseStyles.Apply(stem, "pascal"));
    }

    [Fact]
    public void CombiningMarksStayWithTheirLetter()
        => Assert.Equal("café_olé", CaseStyles.Apply("Café Olé", "snake"));

    [Theory]
    [InlineData("snake")]
    [InlineData("kebab")]
    [InlineData("camel")]
    [InlineData("pascal")]
    public void IdentifierStylesOfPunctuationAreEmpty(string mode)
        => Assert.Equal("", CaseStyles.Apply("!!! ---", mode));

    [Fact]
    public void EveryModeIsAccepted()
    {
        foreach (var mode in CaseStyles.Modes) CaseStyles.Apply("name", mode);
    }
}
