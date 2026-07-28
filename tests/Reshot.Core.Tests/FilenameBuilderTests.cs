using Reshot.Core.Export;
using Xunit;

namespace Reshot.Core.Tests;

public class FilenameBuilderTests
{
    private static readonly DateTime Sample = new(2026, 7, 18, 14, 32, 1);

    [Fact]
    public void Builds_default_template()
    {
        var name = FilenameBuilder.Build("reshot_{date}_{time}", "png", Sample);
        Assert.Equal("reshot_2026-07-18_14-32-01.png", name);
    }

    [Fact]
    public void Normalizes_extension()
    {
        Assert.Equal("x.jpg", FilenameBuilder.Build("x", ".JPG", Sample));
        Assert.Equal("x.png", FilenameBuilder.Build("x", "PNG", Sample));
    }

    [Fact]
    public void Empty_template_falls_back_to_default()
    {
        var name = FilenameBuilder.Build("", "png", Sample);
        Assert.Equal("reshot_2026-07-18_14-32-01.png", name);
    }

    [Fact]
    public void Keeps_literal_text_around_placeholders()
    {
        var name = FilenameBuilder.Build("shot-{date}", "png", Sample);
        Assert.Equal("shot-2026-07-18.png", name);
    }
}
