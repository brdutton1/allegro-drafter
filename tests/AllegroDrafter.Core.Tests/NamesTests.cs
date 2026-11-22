namespace AllegroDrafter.Core.Tests;

public class NamesTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("same", "same", 0)]
    public void Levenshtein_matches_known_distances(string a, string b, int expected) =>
        Assert.Equal(expected, Names.Levenshtein(a, b));

    [Fact]
    public void ClosestMatch_finds_near_typo_case_insensitive()
    {
        var options = new[] { "Allegro 24x36: Standard", "Allegro 11x17: Half Size" };
        Assert.Equal("Allegro 24x36: Standard", Names.ClosestMatch("allegro 24x36 standard", options));
    }

    [Fact]
    public void ClosestMatch_returns_null_when_nothing_is_close()
    {
        Assert.Null(Names.ClosestMatch("E1 Titleblock Landscape Huge", new[] { "A" }));
        Assert.Null(Names.ClosestMatch("anything", Array.Empty<string>()));
    }

    [Fact]
    public void UnknownNameMessage_suggests_and_lists_options()
    {
        var msg = Names.UnknownNameMessage("Title block", "Allegro 24x36:Standard", new[] { "Allegro 24x36: Standard" });
        Assert.Contains("Did you mean 'Allegro 24x36: Standard'?", msg);
        Assert.Contains("Valid options:", msg);
    }

    [Fact]
    public void UnknownNameMessage_truncates_long_lists()
    {
        var options = Enumerable.Range(1, 30).Select(i => $"Type {i}").ToList();
        var msg = Names.UnknownNameMessage("View template", "zzzzzzzzzzzzzzzzzz", options, listLimit: 5);
        Assert.Contains("(+25 more, see get_standards)", msg);
    }

    [Fact]
    public void UnknownNameMessage_handles_empty_profile_section()
    {
        var msg = Names.UnknownNameMessage("Title block", "X", Array.Empty<string>());
        Assert.Contains("re-export standards", msg);
    }

    [Fact]
    public void SafeFileName_strips_path_characters()
    {
        Assert.Equal("Smith_ House_ v2", Names.SafeFileName("Smith/ House: v2"));
        Assert.Equal("untitled", Names.SafeFileName("   "));
    }
}
