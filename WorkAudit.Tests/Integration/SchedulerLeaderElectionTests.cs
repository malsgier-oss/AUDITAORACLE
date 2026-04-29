using FluentAssertions;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Integration;

public sealed class SchedulerLeaderElectionTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;

    public SchedulerLeaderElectionTests(OracleTestFixture fixture) => _fx = fixture;

    [SkippableFact]
    public void TryAcquireOrRenew_SecondHolderFailsUntilLeaseExpires()
    {
        Skip.IfNot(_fx.IsAvailable);
        new MigrationService(_fx.ConnectionString!).Migrate();

        var locks = new SchedulerLockStore(_fx.ConnectionString!);
        const string name = "test_lock_" + nameof(TryAcquireOrRenew_SecondHolderFailsUntilLeaseExpires);
        var holderA = "holder-a";
        var holderB = "holder-b";

        locks.TryAcquireOrRenew(name, holderA, TimeSpan.FromMinutes(5)).Should().BeTrue();
        locks.TryAcquireOrRenew(name, holderB, TimeSpan.FromMinutes(5)).Should().BeFalse();

        locks.ReleaseIfHolder(name, holderA);
        locks.TryAcquireOrRenew(name, holderB, TimeSpan.FromMinutes(5)).Should().BeTrue();
        locks.ReleaseIfHolder(name, holderB);
    }

    [SkippableFact]
    public void TryAcquireOrRenew_SameHolderCanRenew()
    {
        Skip.IfNot(_fx.IsAvailable);
        new MigrationService(_fx.ConnectionString!).Migrate();

        var locks = new SchedulerLockStore(_fx.ConnectionString!);
        const string name = "test_lock_renew";
        var holder = "holder-renew";

        locks.TryAcquireOrRenew(name, holder, TimeSpan.FromMinutes(5)).Should().BeTrue();
        locks.TryAcquireOrRenew(name, holder, TimeSpan.FromMinutes(5)).Should().BeTrue();
        locks.ReleaseIfHolder(name, holder);
    }
}
