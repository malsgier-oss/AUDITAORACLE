# Load Testing Framework

This directory contains load tests for the WorkAudit application. These tests verify that the application can handle bank production workloads.

## Test Scenarios

### 1. Document Import Load Test
**Purpose**: Verify the application can import 500+ documents per day without performance degradation.

**Target Metrics**:
- Import throughput: <5 seconds per document
- Memory usage: <1GB during bulk import
- Success rate: >99%

**Manual Execution**:
```bash
# Run for 30 minutes (simulates partial workday)
dotnet test --filter "DocumentImportLoadTest"

# For full 8-hour test, modify timeout in test
```

### 2. Concurrent User Test
**Purpose**: Verify the application handles 10+ concurrent users without database lock issues.

**Target Metrics**:
- Query response time: <1 second (average)
- Database lock errors: 0
- Concurrent operations: 1000+ per minute

**Manual Execution**:
```bash
dotnet test --filter "ConcurrentUserTest"
```

### 3. Memory Leak Test
**Purpose**: Verify no memory leaks during extended operation.

**Target Metrics**:
- Memory growth: <300MB over 8 hours
- Peak memory: <1.5GB
- No OutOfMemory errors

**Manual Execution**:
```bash
# 30-minute version
dotnet test --filter "MemoryLeakTest"

# For 8-hour test, set duration to TimeSpan.FromHours(8)
```

### 4. Large File Test
**Purpose**: Verify the application can process large PDF files (50-200MB) without crashing.

**Target Metrics**:
- Processing time: <2 minutes for 100MB PDF
- Peak memory: <2GB
- No crashes or OutOfMemory errors

**Manual Execution**:
```bash
dotnet test --filter "LargeFileTest"
```

## Running All Load Tests

```bash
dotnet test --filter "FullyQualifiedName~Load" --logger "console;verbosity=detailed"
```

## Performance Baseline

Expected performance on modern hardware (8+ GB RAM, SSD):
- Document import: 2-4 seconds per document
- Concurrent queries: 100-200ms average response time
- Memory usage: 500-800MB during normal operation
- Large file processing: 30-90 seconds for 100MB PDF

## Notes for Production Deployment

1. **Disk Space**: Ensure at least 10GB free space per 1000 documents
2. **Memory**: Minimum 8GB RAM recommended, 16GB for heavy workloads
3. **CPU**: Multi-core processor recommended for parallel import
4. **Database**: SQLite performs well up to 100K documents. For larger datasets, consider PostgreSQL migration.

## TODO: Implement Full Load Tests

The load test implementations have been created as a framework. To complete:

1. Verify API compatibility with current codebase
2. Add actual PdfPig integration for PDF generation
3. Configure CI/CD to run subset of load tests
4. Create performance monitoring dashboard

For now, manual performance testing is recommended before production deployment.
