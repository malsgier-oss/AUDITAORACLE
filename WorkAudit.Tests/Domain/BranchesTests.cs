using FluentAssertions;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Domain;

public class BranchesTests
{
    [Theory]
    [InlineData("All Branches", true)]
    [InlineData("all branches", true)]
    [InlineData(" ALL BRANCHES ", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("Main Branch", false)]
    public void ScopesToAllBranches_MatchesSentinel(string? branch, bool expected)
    {
        Branches.ScopesToAllBranches(branch).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, Branches.MainBranch)]
    [InlineData("", Branches.MainBranch)]
    [InlineData("All Branches", Branches.MainBranch)]
    [InlineData("Main Branch", Branches.MainBranch)]
    [InlineData("main branch", Branches.MainBranch)]
    [InlineData("Misrata Branch", Branches.MisrataBranch)]
    [InlineData("Not A Real Branch", Branches.MainBranch)]
    public void ToConcreteBranchOrDefault_Normalizes(string? branch, string expected)
    {
        Branches.ToConcreteBranchOrDefault(branch).Should().Be(expected);
    }

    [Fact]
    public void UserMatchesAssigneeBranchFilter_NullFilter_MatchesAll()
    {
        Branches.UserMatchesAssigneeBranchFilter("Main Branch", null).Should().BeTrue();
        Branches.UserMatchesAssigneeBranchFilter(null, null).Should().BeTrue();
    }

    [Fact]
    public void UserMatchesAssigneeBranchFilter_AllBranchesUser_MatchesAnyConcreteBranch()
    {
        Branches.UserMatchesAssigneeBranchFilter(Branches.AllBranchesLabel, Branches.MisrataBranch).Should().BeTrue();
        Branches.UserMatchesAssigneeBranchFilter("all branches", Branches.MainBranch).Should().BeTrue();
    }

    [Fact]
    public void UserMatchesAssigneeBranchFilter_SingleBranch_RequiresMatch()
    {
        Branches.UserMatchesAssigneeBranchFilter(Branches.MainBranch, Branches.MainBranch).Should().BeTrue();
        Branches.UserMatchesAssigneeBranchFilter(Branches.MainBranch, Branches.MisrataBranch).Should().BeFalse();
    }
}
