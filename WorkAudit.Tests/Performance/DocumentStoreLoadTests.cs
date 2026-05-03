using FluentAssertions;
using System.Diagnostics;
using System.Threading.Tasks;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace WorkAudit.Tests.Performance;

/// <summary>
/// Load and performance tests for WorkAudit core operations.
/// These tests verify the system can handle expected production loads. Requires
/// <see cref="OracleTestConfig.EnvKey"/>.
/// </summary>
public class DocumentStoreLoadTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;
    private readonly ITestOutputHelper _output;
    private DocumentStore _store => _fx.DocumentStore;

    public DocumentStoreLoadTests(OracleTestFixture f, ITestOutputHelper output)
    {
        _fx = f;
        _output = output;
    }

    [SkippableFact]
    public void BulkInsert_500Documents_ShouldCompleteInReasonableTime()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        const int documentCount = 500;
        var documents = GenerateTestDocuments(documentCount);
        var sw = Stopwatch.StartNew();

        // Act
        foreach (var doc in documents)
        {
            _store.Insert(doc);
        }
        sw.Stop();

        // Assert
        _output.WriteLine($"Inserted {documentCount} documents in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {(double)sw.ElapsedMilliseconds / documentCount:F2}ms per document");
        
        sw.ElapsedMilliseconds.Should().BeLessThan(30000); // Should complete in < 30 seconds
        var avgPerDoc = (double)sw.ElapsedMilliseconds / documentCount;
        avgPerDoc.Should().BeLessThan(100); // < 100ms per document on average

        // Verify all documents were inserted
        var allDocs = _store.ListDocuments(limit: 1000);
        allDocs.Should().HaveCount(documentCount);
    }

    [SkippableFact]
    public void Query_10000Documents_ShouldReturnQuickly()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert 10,000 documents
        const int documentCount = 10000;
        _output.WriteLine($"Preparing {documentCount} test documents...");
        
        var insertSw = Stopwatch.StartNew();
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                DocumentType = i % 5 == 0 ? "Invoice" : i % 3 == 0 ? "Receipt" : "Contract",
                Status = i % 4 == 0 ? "Reviewed" : "Draft",
                Branch = i % 3 == 0 ? "Main" : i % 2 == 0 ? "East" : "West",
                Section = "Finance",
                Engagement = $"Engagement-{i % 10}"
            };
            _store.Insert(doc);
            
            if (i % 1000 == 0 && i > 0)
            {
                _output.WriteLine($"  ... inserted {i} documents");
            }
        }
        insertSw.Stop();
        _output.WriteLine($"Inserted {documentCount} documents in {insertSw.ElapsedMilliseconds}ms");

        // Act - Query with filters
        var querySw = Stopwatch.StartNew();
        var results = _store.ListDocuments(
            documentType: "Invoice",
            status: "Draft",
            limit: 1000
        );
        querySw.Stop();

        // Assert
        _output.WriteLine($"Queried {results.Count} results from {documentCount} documents in {querySw.ElapsedMilliseconds}ms");
        
        results.Should().NotBeEmpty();
        results.Should().OnlyContain(d => d.DocumentType == "Invoice");
        results.Should().OnlyContain(d => d.Status == "Draft");
        querySw.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete in < 5 seconds
    }

    [SkippableFact]
    public void GetDocumentById_FromLargeDataset_ShouldBeFast()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert 5,000 documents
        const int documentCount = 5000;
        var ids = new List<int>();
        
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                Branch = "Main"
            };
            ids.Add((int)_store.Insert(doc));
        }

        // Act - Get random documents by ID
        var randomIds = ids.OrderBy(_ => Guid.NewGuid()).Take(100).ToList();
        var sw = Stopwatch.StartNew();
        
        foreach (var id in randomIds)
        {
            var doc = _store.Get(id);
            doc.Should().NotBeNull();
        }
        
        sw.Stop();

        // Assert
        _output.WriteLine($"Retrieved 100 documents by ID from {documentCount} total in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {(double)sw.ElapsedMilliseconds / 100:F2}ms per lookup");
        
        sw.ElapsedMilliseconds.Should().BeLessThan(1000); // 100 lookups in < 1 second
        var avgPerLookup = (double)sw.ElapsedMilliseconds / 100;
        avgPerLookup.Should().BeLessThan(10); // < 10ms per lookup on average
    }

    [SkippableFact]
    public void Update_1000Documents_ShouldCompleteInReasonableTime()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert 1,000 documents
        const int documentCount = 1000;
        var documents = new List<Document>();
        
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                Status = "Draft",
                Branch = "Main"
            };
            doc.Id = (int)_store.Insert(doc);
            documents.Add(doc);
        }

        // Act - Update all documents
        var sw = Stopwatch.StartNew();
        
        foreach (var doc in documents)
        {
            doc.Status = "Reviewed";
            doc.ReviewedAt = DateTime.UtcNow.ToString("O");
            _store.Update(doc);
        }
        
        sw.Stop();

        // Assert
        _output.WriteLine($"Updated {documentCount} documents in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {(double)sw.ElapsedMilliseconds / documentCount:F2}ms per update");
        
        sw.ElapsedMilliseconds.Should().BeLessThan(15000); // < 15 seconds
        var avgPerUpdate = (double)sw.ElapsedMilliseconds / documentCount;
        avgPerUpdate.Should().BeLessThan(20); // < 20ms per update on average

        // Verify updates
        var updatedDoc = _store.Get(documents[0].Id);
        updatedDoc!.Status.Should().Be("Reviewed");
    }

    [SkippableFact]
    public async Task ConcurrentReads_MultipleThreads_ShouldHandleLoad()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert test documents
        const int documentCount = 1000;
        var ids = new List<int>();
        
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                Branch = "Main"
            };
            ids.Add((int)_store.Insert(doc));
        }

        // Act - Concurrent reads from multiple threads
        const int threadCount = 10;
        const int readsPerThread = 100;
        var tasks = new List<Task>();
        var sw = Stopwatch.StartNew();

        for (int t = 0; t < threadCount; t++)
        {
            var task = Task.Run(() =>
            {
                for (int i = 0; i < readsPerThread; i++)
                {
                    var randomId = ids[Random.Shared.Next(ids.Count)];
                    var doc = _store.Get(randomId);
                    doc.Should().NotBeNull();
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // Assert
        var totalReads = threadCount * readsPerThread;
        _output.WriteLine($"Completed {totalReads} concurrent reads from {threadCount} threads in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {(double)sw.ElapsedMilliseconds / totalReads:F2}ms per read");
        
        sw.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete in < 10 seconds
    }

    [SkippableFact]
    public void FullTextSearch_LargeDataset_ShouldPerformAdequately()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert documents with OCR text
        const int documentCount = 2000;
        var searchTerms = new[] { "invoice", "contract", "receipt", "payment", "agreement" };
        
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                Branch = "Main",
                OcrText = $"This is a test document containing {searchTerms[i % searchTerms.Length]} and other text content for document number {i}"
            };
            _store.Insert(doc);
        }

        // Act - Perform full-text search
        var sw = Stopwatch.StartNew();
        var results = _store.FullTextSearch("invoice", limit: 100);
        sw.Stop();

        // Assert
        _output.WriteLine($"Full-text search returned {results.Count} results from {documentCount} documents in {sw.ElapsedMilliseconds}ms");
        
        results.Should().NotBeEmpty();
        results.Should().OnlyContain(d => d.OcrText != null && d.OcrText.Contains("invoice", StringComparison.OrdinalIgnoreCase));
        sw.ElapsedMilliseconds.Should().BeLessThan(3000); // Should complete in < 3 seconds
    }

    [SkippableFact]
    public void GetDistinct_Branches_FromLargeDataset_ShouldBeFast()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange - Insert documents across multiple branches
        const int documentCount = 5000;
        var branches = new[] { "Main", "East", "West", "North", "South", "Central", "Regional" };
        
        for (int i = 0; i < documentCount; i++)
        {
            var doc = new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/doc{i}.pdf",
                Branch = branches[i % branches.Length]
            };
            _store.Insert(doc);
        }

        // Act - Get distinct branches
        var sw = Stopwatch.StartNew();
        var distinctBranches = _store.GetDistinctBranches();
        sw.Stop();

        // Assert
        _output.WriteLine($"Retrieved {distinctBranches.Count} distinct branches from {documentCount} documents in {sw.ElapsedMilliseconds}ms");
        
        distinctBranches.Should().HaveCount(branches.Length);
        distinctBranches.Should().Contain(branches);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete in < 1 second
    }

    private static List<Document> GenerateTestDocuments(int count)
    {
        var documents = new List<Document>();
        var documentTypes = new[] { "Invoice", "Receipt", "Contract", "Statement", "Report" };
        var statuses = new[] { "Draft", "Reviewed", "Approved", "Rejected" };
        var branches = new[] { "Main", "East", "West", "North", "South" };

        for (int i = 0; i < count; i++)
        {
            documents.Add(new Document
            {
                Uuid = Guid.NewGuid().ToString(),
                FilePath = $"/test/batch/doc{i}.pdf",
                DocumentType = documentTypes[i % documentTypes.Length],
                Status = statuses[i % statuses.Length],
                Branch = branches[i % branches.Length],
                Section = "Finance",
                Engagement = $"Project-{i / 100}",
                PageCount = Random.Shared.Next(1, 20),
                FileSize = Random.Shared.Next(100000, 5000000),
                CaptureTime = DateTime.UtcNow.AddDays(-i % 365).ToString("O"),
                Source = "LoadTest"
            });
        }

        return documents;
    }
}
