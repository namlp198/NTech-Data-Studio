using NTech.DataStudio.Application.Prepare;

namespace NTech.DataStudio.Application.Tests.Prepare;

public class NormalizeHelpTests
{
    [Fact]
    public void EveryOptionHasAnExplanationWithBenefitAndDrawback()
    {
        var all = Enum.GetValues<GeometryMethod>().Select(NormalizeHelp.Method)
            .Concat(new[] { 1024, 2048, 4096 }.Select(NormalizeHelp.MaxEdge))
            .Concat(Enum.GetValues<MasterFormat>().Select(NormalizeHelp.Format))
            .Concat(Enum.GetValues<MonoMode>().Select(NormalizeHelp.Mono))
            .ToList();

        Assert.NotEmpty(all);
        foreach (var help in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(help.What));
            Assert.False(string.IsNullOrWhiteSpace(help.Benefit));
            Assert.False(string.IsNullOrWhiteSpace(help.Drawback));
            Assert.Contains("Lợi ích:", help.Text);
            Assert.Contains("Bất lợi:", help.Text);
        }
    }

    [Fact]
    public void TheMaxEdgeTextNamesTheEdgeItIsAbout()
    {
        Assert.Contains("2048", NormalizeHelp.MaxEdge(2048).What);
        Assert.Contains("4096", NormalizeHelp.MaxEdge(4096).What);
    }
}
