using System.Collections.Concurrent;
using FluentAssertions;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Integration;

/// <summary>
/// Documents multi-PC / shared-Oracle behavior: last-write-wins updates and optional optimistic concurrency on <see cref="DocumentStore.UpdateResult"/>.
/// </summary>
public sealed class SharedOracleConcurrencyTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;

    public SharedOracleConcurrencyTests(OracleTestFixture fixture) => _fx = fixture;

    [SkippableFact]
    public void Migrate_ShouldIncludeSchedulerLeaderElection()
    {
        Skip.IfNot(_fx.IsAvailable);
        var migrationService = new MigrationService(_fx.ConnectionString!);
        migrationService.Migrate();
        migrationService.GetCurrentVersion().Should().BeGreaterThanOrEqualTo(53);
    }

    [SkippableFact]
    public void ParallelInserts_AllSucceedWithUniqueIds()
    {
        Skip.IfNot(_fx.IsAvailable);
        var store = _fx.DocumentStore;
        const int n = 8;
        var ids = new ConcurrentBag<long>();
        Parallel.For(0, n, _ =>
        {
            var id = store.Insert(new Document
            {
                FilePath = $"concurrent-{Guid.NewGuid():N}.pdf",
                Branch = Branches.Default,
                Section = "ConcurrencySmoke"
            });
            ids.Add(id);
        });

        ids.Should().HaveCount(n);
        ids.Distinct().Should().HaveCount(n);
    }

    [SkippableFact]
    public void UpdateResult_WithStaleExpectedUpdatedAt_ReturnsConcurrencyFailure()
    {
        Skip.IfNot(_fx.IsAvailable);
        var store = _fx.DocumentStore;
        var id = store.Insert(new Document
        {
            FilePath = "cc-stale.pdf",
            Branch = Branches.Default,
            Section = "ConcurrencySmoke",
            Status = Enums.Status.Draft
        });
        id.Should().BeGreaterThan(0);

        var loaded = store.GetById((int)id);
        loaded.Should().NotBeNull();
        store.UpdateStatus((int)id, Enums.Status.ReadyForAudit);

        var staleUtc = DateTime.Parse(loaded!.UpdatedAt!, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
        loaded.Status = Enums.Status.Cleared;
        var result = store.UpdateResult(loaded, staleUtc);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Concurrency");
    }

    [SkippableFact]
    public void UpdateResult_WithFreshExpectedUpdatedAt_Succeeds()
    {
        Skip.IfNot(_fx.IsAvailable);
        var store = _fx.DocumentStore;
        var id = store.Insert(new Document
        {
            FilePath = "cc-fresh.pdf",
            Branch = Branches.Default,
            Section = "ConcurrencySmoke",
            Status = Enums.Status.Draft
        });
        var loaded = store.GetById((int)id)!;
        var expectedUtc = DateTime.Parse(loaded.UpdatedAt!, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
        loaded.Explanation = "multi-pc test";
        var result = store.UpdateResult(loaded, expectedUtc);
        result.IsSuccess.Should().BeTrue();
    }
}
