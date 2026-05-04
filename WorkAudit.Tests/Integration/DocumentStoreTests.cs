using System.Globalization;
using FluentAssertions;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Integration;

public class DocumentStoreTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;
    private DocumentStore _store => _fx.DocumentStore;

    public DocumentStoreTests(OracleTestFixture f) => _fx = f;

    [SkippableFact]
    public void Insert_ShouldCreateDocument()
    {
        Skip.IfNot(_fx.IsAvailable);
        var doc = new Document
        {
            FilePath = "test.pdf",
            DocumentType = "Check",
            Branch = Branches.Default,
            Section = "Sec"
        };

        var id = _store.Insert(doc);

        id.Should().BeGreaterThan(0);
        var retrieved = _store.GetById((int)id);
        retrieved.Should().NotBeNull();
        retrieved!.DocumentType.Should().Be("Check");
    }

    [SkippableFact]
    public void Update_ShouldModifyDocument()
    {
        Skip.IfNot(_fx.IsAvailable);
        var doc = new Document { FilePath = "test.pdf", Branch = Branches.Default, Section = "S" };
        var id = _store.Insert(doc);
        doc.Id = (int)id;
        doc.DocumentType = "Invoice";

        var success = _store.Update(doc);

        success.Should().BeTrue();
        var retrieved = _store.GetById((int)id);
        retrieved!.DocumentType.Should().Be("Invoice");
    }

    [SkippableFact]
    public void InsertAndUpdate_AccountFields_ShouldRoundTrip()
    {
        Skip.IfNot(_fx.IsAvailable);
        var doc = new Document
        {
            FilePath = "stmt.pdf",
            Branch = Branches.Default,
            Section = "S",
            AccountName = "Jane Auditor",
            AccountNumber = "00012345",
            TransactionReference = "WIRE-99-AB"
        };
        var id = _store.Insert(doc);
        doc.Id = (int)id;

        var loaded = _store.GetById(doc.Id);
        loaded!.AccountName.Should().Be("Jane Auditor");
        loaded.AccountNumber.Should().Be("00012345");
        loaded.TransactionReference.Should().Be("WIRE-99-AB");

        loaded.AccountName = "Updated Name";
        loaded.AccountNumber = null;
        loaded.TransactionReference = "CHK-1";
        _store.Update(loaded).Should().BeTrue();

        var after = _store.GetById(doc.Id);
        after!.AccountName.Should().Be("Updated Name");
        after.AccountNumber.Should().BeNull();
        after.TransactionReference.Should().Be("CHK-1");
    }

    [SkippableFact]
    public void Delete_ShouldRemoveDocument()
    {
        Skip.IfNot(_fx.IsAvailable);
        var doc = new Document { FilePath = "test.pdf", Branch = Branches.Default, Section = "S" };
        var id = _store.Insert(doc);

        var success = _store.Delete((int)id);

        success.Should().BeTrue();
        var retrieved = _store.GetById((int)id);
        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public void GetByFileHash_ExistingHash_ShouldReturnDocument()
    {
        Skip.IfNot(_fx.IsAvailable);
        var h = "abc123def456_" + Guid.NewGuid().ToString("N");
        var doc = new Document
        {
            FilePath = "test.pdf",
            FileHash = h,
            Branch = Branches.Default,
            Section = "S"
        };
        _store.Insert(doc);

        var retrieved = _store.GetByFileHash(h);

        retrieved.Should().NotBeNull();
        retrieved!.FileHash.Should().Be(h);
    }

    [SkippableFact]
    public void GetByFileHash_NonExistentHash_ShouldReturnNull()
    {
        Skip.IfNot(_fx.IsAvailable);
        var retrieved = _store.GetByFileHash("nonexistent________" + Guid.NewGuid().ToString("N"));

        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public void ListDocuments_WithPagination_ShouldReturnCorrectPage()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "P_" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < 25; i++)
        {
            _store.Insert(new Document
            {
                FilePath = $"doc_{i}.pdf",
                Branch = br,
                Section = "S"
            });
        }

        var page1 = _store.ListDocuments(branch: br, limit: 10, offset: 0);
        var page2 = _store.ListDocuments(branch: br, limit: 10, offset: 10);
        var page3 = _store.ListDocuments(branch: br, limit: 10, offset: 20);

        page1.Count.Should().Be(10);
        page2.Count.Should().Be(10);
        page3.Count.Should().Be(5);
    }

    [SkippableFact]
    public void ListDocuments_DefaultLimit_ShouldReturn500()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "L_" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < 600; i++)
        {
            _store.Insert(new Document
            {
                FilePath = $"doc_{i}.pdf",
                Branch = br,
                Section = "S"
            });
        }

        var results = _store.ListDocuments(branch: br, limit: 500);

        results.Count.Should().Be(500);
    }

    [SkippableFact]
    public void ListDocuments_WithBranchFilter_ShouldFilterCorrectly()
    {
        Skip.IfNot(_fx.IsAvailable);
        var b1 = "B1_" + Guid.NewGuid().ToString("N");
        var b2 = "B2_" + Guid.NewGuid().ToString("N");
        _store.Insert(new Document { FilePath = "doc1.pdf", Branch = b1, Section = "S" });
        _store.Insert(new Document { FilePath = "doc2.pdf", Branch = b2, Section = "S" });
        _store.Insert(new Document { FilePath = "doc3.pdf", Branch = b1, Section = "S" });

        var branch1Docs = _store.ListDocuments(branch: b1);

        branch1Docs.Count.Should().Be(2);
        branch1Docs.Should().AllSatisfy(d => d.Branch.Should().Be(b1));
    }

    [SkippableFact]
    public void ListDocuments_OrdersOlderDocumentsBeforeNewer_OnDefaultQuery()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "O_" + Guid.NewGuid().ToString("N");
        var id1 = _store.Insert(new Document { FilePath = "older.pdf", Branch = br, Section = "S" });
        var id2 = _store.Insert(new Document { FilePath = "newer.pdf", Branch = br, Section = "S" });

        var list = _store.ListDocuments(branch: br, limit: 100);
        var indexNewer = list.FindIndex(d => d.Id == (int)id2);
        var indexOlder = list.FindIndex(d => d.Id == (int)id1);

        indexNewer.Should().BeGreaterThanOrEqualTo(0);
        indexOlder.Should().BeGreaterThanOrEqualTo(0);
        indexOlder.Should().BeLessThan(indexNewer);
    }

    /// <summary>
    /// Phase 1b regression guard: when reports request <c>newestFirst</c>, the row cap must drop
    /// the OLDEST rows, not the newest. The pre-fix default ordering returned the oldest rows
    /// first, so once a deployment had more than 50k documents the most recent issues silently
    /// disappeared from every report.
    /// </summary>
    [SkippableFact]
    public void ListDocuments_NewestFirst_ReturnsRecentRowsWhenCappedByLimit()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "NF_" + Guid.NewGuid().ToString("N");
        var oldId = (int)_store.Insert(new Document { FilePath = "older.pdf", Branch = br, Section = "S" });
        var newId = (int)_store.Insert(new Document { FilePath = "newer.pdf", Branch = br, Section = "S" });

        // Cap to 1 row; with newestFirst we must keep the most recent insert, not the oldest.
        var top = _store.ListDocuments(branch: br, limit: 1, newestFirst: true);

        top.Should().HaveCount(1);
        top[0].Id.Should().Be(newId, "newestFirst must drop the OLDER rows when the limit is hit");
        top[0].Id.Should().NotBe(oldId);
    }

    [SkippableFact]
    public void ListDocuments_TextSearch_ShouldMatchByDocumentId()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "Srch_" + Guid.NewGuid().ToString("N");
        var idA = (int)_store.Insert(new Document
        {
            FilePath = "first.pdf",
            Branch = br,
            Section = "S",
            OcrText = "no digits in body"
        });
        var idB = (int)_store.Insert(new Document
        {
            FilePath = "second.pdf",
            Branch = br,
            Section = "S",
            OcrText = "other content"
        });

        var byFullId = _store.ListDocuments(branch: br, textSearch: idA.ToString(CultureInfo.InvariantCulture));
        byFullId.Should().ContainSingle(d => d.Id == idA);
        byFullId.Should().NotContain(d => d.Id == idB);
    }

    [SkippableFact]
    public void ListDocuments_CreatedBy_ShouldRestrictRows()
    {
        Skip.IfNot(_fx.IsAvailable);
        var br = "Cb_" + Guid.NewGuid().ToString("N");
        var idA = (int)_store.Insert(new Document { FilePath = "a.pdf", Branch = br, Section = "S", CreatedBy = "user_a" });
        var idB = (int)_store.Insert(new Document { FilePath = "b.pdf", Branch = br, Section = "S", CreatedBy = "user_b" });

        var forA = _store.ListDocuments(branch: br, createdBy: "user_a");
        forA.Should().Contain(d => d.Id == idA);
        forA.Should().NotContain(d => d.Id == idB);
    }
}

