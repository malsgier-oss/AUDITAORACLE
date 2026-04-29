using Xunit;

namespace WorkAudit.Tests.Backup;

/// <summary>Marker fixture for backup test collection.</summary>
public sealed class BackupTestsFixture;

/// <summary>Serializes backup-related tests to avoid timestamp collisions on default backup paths and shared cleanup.</summary>
[CollectionDefinition("BackupTests", DisableParallelization = true)]
public sealed class BackupTestsCollection : ICollectionFixture<BackupTestsFixture>;
