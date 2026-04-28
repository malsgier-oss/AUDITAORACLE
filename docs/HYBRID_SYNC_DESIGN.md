# Hybrid Sync Architecture Design

## Version: 1.0 Draft
## Status: Design Phase
## Target: Multi-Branch Bank Deployment

> **Repository note:** An optional stub ASP.NET “central API” sample previously lived under `CentralApi/` and has been **removed** from this codebase. This document remains a **reference design** for possible HQ sync; any future implementation would be a separate initiative.

---

## Overview

The hybrid sync architecture enables multiple bank branches to operate independently with local Oracle databases while synchronizing to a centralized database for enterprise-wide reporting and compliance.

### Design Goals

1. **Branch Autonomy**: Each branch operates independently, even during network outages
2. **Data Consistency**: Eventual consistency with conflict resolution
3. **Performance**: Local operations remain fast (<1 second)
4. **Security**: Encrypted data in transit (TLS 1.3), authentication via API keys
5. **Scalability**: Support 4-10 branches initially, up to 50+ branches
6. **Audit Trail**: Complete sync history with conflict resolution logging

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     Central Server (HQ)                         │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │   ASP.NET Core API Server (Port 443 - HTTPS/TLS 1.3)    │  │
│  │   - Authentication (JWT tokens)                           │  │
│  │   - Sync endpoints (/api/sync/*)                         │  │
│  │   - Health monitoring                                     │  │
│  └──────────────────┬──────────────────────────────────────┘  │
│                     │                                          │
│  ┌──────────────────▼──────────────────────────────────────┐  │
│  │   Central Database (PostgreSQL or SQL Server)           │  │
│  │   - documents (all branches)                            │  │
│  │   - users (centralized auth)                            │  │
│  │   - sync_log (sync history)                             │  │
│  │   - branch_status (health monitoring)                   │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │   File Storage (Azure Blob / S3 / Network Share)        │  │
│  │   - Attachments, images, PDFs from all branches         │  │
│  └─────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                 ┌────────────┼────────────┐
                 │            │            │
        ┌────────▼──────┐ ┌──▼──────┐ ┌──▼──────────┐
        │  Branch 01    │ │Branch 02│ │  Branch N   │
        │               │ │         │ │             │
        │ ┌───────────┐ │ │         │ │             │
        │ │WorkAudit  │ │ │  ...    │ │   ...       │
        │ │Desktop App│ │ │         │ │             │
        │ │           │ │ │         │ │             │
        │ │┌─────────┐│ │ │         │ │             │
        │ ││Oracle DB││ │ │         │ │             │
        │ ││(Local)  ││ │ │         │ │             │
        │ │└─────────┘│ │ │         │ │             │
        │ │           │ │ │         │ │             │
        │ │ISyncService│ │ │         │ │             │
        │ │(Background)│ │ │         │ │             │
        │ └───────────┘ │ │         │ │             │
        └───────────────┘ └─────────┘ └─────────────┘
```

---

## Components

### 1. Branch Client (WorkAudit Desktop App)

**Technology**: WPF, .NET 8.0, Oracle

**Components**:
- `ISyncService` - Manages bidirectional sync
- `ISyncQueueStore` - Queues pending changes
- `ISyncConflictResolver` - Handles conflicts
- `ISyncStatusService` - Monitors sync health

**Responsibilities**:
- Normal document operations (local Oracle)
- Queue local changes for upload
- Poll central server for changes (every 5 minutes)
- Download and apply remote changes
- Resolve conflicts (local precedence or manual)
- Sync health monitoring and alerting

### 2. Central API Server

**Technology**: ASP.NET Core 8.0, RESTful API, Swagger

**Endpoints**:

```
POST   /api/auth/login                    - Authenticate branch client
GET    /api/sync/changes?since={timestamp} - Get changes since timestamp
POST   /api/sync/upload                    - Upload local changes
POST   /api/sync/attachments               - Upload file attachments
GET    /api/sync/status                    - Get sync status
POST   /api/sync/resolve-conflict          - Submit conflict resolution
```

**Responsibilities**:
- Authenticate branch clients (API key + JWT)
- Receive and merge changes from branches
- Serve changes to branches
- Conflict detection and resolution coordination
- File attachment synchronization
- Audit logging
- Health monitoring

### 3. Central Database

**Technology**: PostgreSQL 15+ or SQL Server 2019+

**Schema Additions** (beyond Oracle schema):

```sql
-- Sync metadata table
CREATE TABLE sync_metadata (
  id SERIAL PRIMARY KEY,
  entity_type VARCHAR(50) NOT NULL,      -- 'document', 'user', etc.
  entity_uuid UUID NOT NULL,
  branch_id VARCHAR(50) NOT NULL,
  operation VARCHAR(20) NOT NULL,        -- 'insert', 'update', 'delete'
  data JSONB NOT NULL,                   -- Full entity data
  version BIGINT NOT NULL,               -- Optimistic locking
  synced_at TIMESTAMP NOT NULL DEFAULT NOW(),
  created_at TIMESTAMP NOT NULL,
  UNIQUE(entity_uuid, version)
);

CREATE INDEX idx_sync_metadata_entity ON sync_metadata(entity_type, entity_uuid);
CREATE INDEX idx_sync_metadata_branch ON sync_metadata(branch_id);
CREATE INDEX idx_sync_metadata_synced_at ON sync_metadata(synced_at);

-- Branch status table
CREATE TABLE branch_status (
  branch_id VARCHAR(50) PRIMARY KEY,
  branch_name VARCHAR(200) NOT NULL,
  last_sync_at TIMESTAMP,
  document_count INTEGER DEFAULT 0,
  is_online BOOLEAN DEFAULT TRUE,
  last_health_check TIMESTAMP,
  version VARCHAR(50)
);

-- Sync conflicts table
CREATE TABLE sync_conflicts (
  id SERIAL PRIMARY KEY,
  entity_type VARCHAR(50) NOT NULL,
  entity_uuid UUID NOT NULL,
  branch_id VARCHAR(50) NOT NULL,
  local_version JSONB NOT NULL,
  remote_version JSONB NOT NULL,
  conflict_type VARCHAR(50) NOT NULL,    -- 'update_update', 'update_delete'
  detected_at TIMESTAMP DEFAULT NOW(),
  resolved_at TIMESTAMP,
  resolution VARCHAR(20),                -- 'local_wins', 'remote_wins', 'manual'
  resolved_by VARCHAR(200)
);
```

---

## Sync Protocol

### 1. Initial Sync (First Connection)

```
Branch                          Central Server
  |                                   |
  |----(1) POST /api/auth/login ----->|
  |<------ JWT token ------------------|
  |                                   |
  |----(2) GET /api/sync/changes ---->|
  |       since=1970-01-01            |
  |<------ Full data dump -------------|
  |                                   |
  |----(3) Apply remote changes ------|
  |       to local Oracle             |
  |                                   |
  |----(4) POST /api/sync/upload ---->|
  |       local documents             |
  |<------ 200 OK ---------------------|
```

### 2. Incremental Sync (Every 5 Minutes)

```
Branch                          Central Server
  |                                   |
  |----(1) GET /api/sync/changes ---->|
  |       since=last_sync_timestamp   |
  |<------ Changes since timestamp----|
  |                                   |
  |----(2) Detect conflicts ---------|
  |                                   |
  |----(3) Resolve conflicts ---------|
  |       (automatic or queue manual) |
  |                                   |
  |----(4) Apply remote changes ------|
  |                                   |
  |----(5) POST /api/sync/upload ---->|
  |       local changes               |
  |<------ 200 OK or Conflict ---------|
  |                                   |
  |----(6) Update last_sync_timestamp-|
```

### 3. Conflict Resolution

#### Conflict Types

1. **Update-Update**: Same entity modified in branch and central
2. **Update-Delete**: Entity updated locally but deleted remotely
3. **Delete-Update**: Entity deleted locally but updated remotely

#### Resolution Strategies

| Conflict Type | Default Strategy | Alternative |
|---------------|------------------|-------------|
| Update-Update | Branch wins (local precedence) | Manual review |
| Update-Delete | Keep update (restore entity) | Manual review |
| Delete-Update | Keep deletion | Manual review |

#### Automatic Resolution (Default)

- **Branch-first policy**: Local changes take precedence
- Conflicts auto-resolved and logged
- Notification to admin for critical conflicts

#### Manual Resolution

- Administrator reviews conflict in UI
- Side-by-side comparison shown
- Admin selects winning version
- Resolution logged to audit trail

---

## Data Sync Scope

### Entities to Sync

| Entity | Direction | Priority |
|--------|-----------|----------|
| Documents (metadata only) | Bidirectional | High |
| Users | Central → Branch (read-only) | Medium |
| Assignments | Bidirectional | High |
| Audit Log | Branch → Central (append-only) | Critical |
| Notes | Bidirectional | Medium |
| Configuration | Central → Branch (admin-initiated) | Low |

### Entities NOT Synced

- **File attachments**: Synced on-demand or via scheduled batch jobs (large files)
- **Sessions**: Local only (branch-specific)
- **Temporary data**: Processing queue, UI state

### File Attachment Sync Strategy

**Option A: On-Demand Sync** (Recommended for <10 branches)

- Attachments uploaded when document is created/modified
- Attachments downloaded when requested by user
- Cached locally after first download

**Option B: Scheduled Batch Sync** (Recommended for 10+ branches)

- Nightly batch job syncs all attachments
- Uses rsync protocol or Azure Blob Storage
- Bandwidth throttling (max 10 Mbps during business hours)

---

## Security

### Authentication

1. **Branch → Central**: API key + JWT tokens
   - API key per branch (stored encrypted in local config)
   - JWT token refreshed every 1 hour
   - TLS 1.3 mutual authentication

2. **User Authentication**: Centralized (optional) or local
   - If centralized: Branch queries central user DB on login
   - If local: User table synced from central (read-only)

### Data in Transit

- **Protocol**: HTTPS only (TLS 1.3)
- **Cipher Suites**: AES-256-GCM only
- **Certificate**: Bank's SSL certificate (not self-signed)
- **Pinning**: Optional certificate pinning for added security

### Data at Rest

- **Central DB**: Encrypted columns (PostgreSQL pgcrypto)
- **File Storage**: Server-side encryption (Azure SSE, AWS S3 SSE)
- **Branch DB**: Optional file-level encryption (DatabaseEncryptionService)

---

## Implementation Plan

### Phase 1: Central Server Infrastructure (Estimated: 4-6 weeks)

#### Week 1-2: Database Setup
- [ ] Install PostgreSQL 15+ or SQL Server 2019+
- [ ] Create central database schema
- [ ] Add sync metadata tables
- [ ] Configure replication (if HA required)
- [ ] Set up automated backups

#### Week 3-4: API Server Development
- [ ] Create ASP.NET Core 8.0 project
- [ ] Implement authentication (JWT + API keys)
- [ ] Implement sync endpoints
- [ ] Add conflict detection logic
- [ ] Create health monitoring endpoints
- [ ] Write API documentation (Swagger)

#### Week 5-6: Testing & Security
- [ ] Unit tests for sync logic
- [ ] Integration tests (branch → central)
- [ ] Load testing (10 branches, 1000 docs/day)
- [ ] Security testing (auth, injection, XSS)
- [ ] Penetration testing
- [ ] Deploy to staging environment

### Phase 2: Branch Client Sync (Estimated: 3-4 weeks)

#### Week 1: Sync Infrastructure
- [ ] Create `ISyncService` interface and implementation
- [ ] Create `ISyncQueueStore` (Oracle table for pending changes)
- [ ] Implement change tracking (triggers or entity modifications)
- [ ] Add sync configuration settings

#### Week 2: Sync Logic
- [ ] Implement upload logic (local → central)
- [ ] Implement download logic (central → local)
- [ ] Add conflict detection
- [ ] Implement automatic conflict resolution

#### Week 3: File Sync
- [ ] Implement attachment upload
- [ ] Implement attachment download
- [ ] Add file integrity verification (checksums)
- [ ] Implement retry logic with exponential backoff

#### Week 4: Testing
- [ ] Test offline operation (graceful degradation)
- [ ] Test sync after extended offline period
- [ ] Test conflict scenarios
- [ ] Test with 1000+ documents
- [ ] Performance testing

### Phase 3: Deployment (Estimated: 2-3 weeks)

#### Week 1: Staging
- [ ] Deploy central server to staging
- [ ] Deploy updated branch app to test branch
- [ ] Run end-to-end tests
- [ ] Performance verification
- [ ] Security audit

#### Week 2: Production Rollout
- [ ] Deploy central server to production
- [ ] Update branch 1 (pilot)
- [ ] Monitor for 3 days
- [ ] Roll out to remaining branches (1-2 per day)

#### Week 3: Stabilization
- [ ] Monitor sync health across all branches
- [ ] Optimize sync intervals based on usage
- [ ] Address any issues
- [ ] User training on conflict resolution

---

## API Specification

### Authentication

```http
POST /api/auth/login
Content-Type: application/json

{
  "branchId": "branch01",
  "apiKey": "enc:v1:..."
}

Response:
{
  "token": "eyJhbGc...",
  "expiresAt": "2026-02-22T12:00:00Z"
}
```

### Get Changes

```http
GET /api/sync/changes?since=2026-02-22T08:00:00Z&entityTypes=document,assignment
Authorization: Bearer eyJhbGc...

Response:
{
  "changes": [
    {
      "entityType": "document",
      "entityUuid": "123e4567-e89b-12d3-a456-426614174000",
      "operation": "update",
      "version": 5,
      "branchId": "branch02",
      "data": { "status": "approved", ... },
      "syncedAt": "2026-02-22T10:30:00Z"
    }
  ],
  "timestamp": "2026-02-22T11:00:00Z"
}
```

### Upload Changes

```http
POST /api/sync/upload
Authorization: Bearer eyJhbGc...
Content-Type: application/json

{
  "branchId": "branch01",
  "changes": [
    {
      "entityType": "document",
      "entityUuid": "123e4567-e89b-12d3-a456-426614174000",
      "operation": "insert",
      "version": 1,
      "data": { "title": "Invoice 2024-001", ... },
      "createdAt": "2026-02-22T09:00:00Z"
    }
  ]
}

Response:
{
  "accepted": 15,
  "conflicts": [
    {
      "entityUuid": "...",
      "conflictType": "update_update",
      "resolution": "local_wins"
    }
  ]
}
```

---

## Conflict Resolution Example

### Scenario: Document Updated in Two Branches Simultaneously

**Timeline**:
- T0: Document created in Branch 01, synced to central (version 1)
- T1: Document downloaded by Branch 02
- T2: Branch 01 updates status to "Approved" (version 2, not yet synced)
- T3: Branch 02 updates status to "Rejected" (version 2, not yet synced)
- T4: Branch 01 syncs first → Central accepts (version 2)
- T5: Branch 02 syncs → **CONFLICT DETECTED**

**Conflict Detection**:
- Central sees Branch 02 trying to update version 1 → 2
- But central is already at version 2 (from Branch 01)
- Data differs: "Approved" vs "Rejected"

**Resolution (Automatic - Branch-First Policy)**:
- Branch 02's change wins (newer timestamp)
- Central updates to version 3 with "Rejected" status
- Branch 01 receives update on next sync (downgrades to "Rejected")
- Conflict logged to audit trail with full history

**Resolution (Manual)**:
- Administrator receives notification
- Opens conflict resolution UI
- Reviews both versions side-by-side
- Selects winning version or merges manually
- Conflict resolution recorded in audit trail

---

## Sync Database Tables (Branch Oracle)

### sync_queue Table

```sql
CREATE TABLE sync_queue (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  entity_type TEXT NOT NULL,
  entity_uuid TEXT NOT NULL,
  operation TEXT NOT NULL,              -- 'insert', 'update', 'delete'
  entity_data TEXT NOT NULL,            -- JSON serialized entity
  version INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  synced_at TEXT,                       -- NULL if not yet synced
  retry_count INTEGER DEFAULT 0,
  last_error TEXT,
  UNIQUE(entity_uuid, version)
);

CREATE INDEX idx_sync_queue_pending ON sync_queue(entity_type, synced_at) WHERE synced_at IS NULL;
```

### sync_log Table

```sql
CREATE TABLE sync_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sync_started_at TEXT NOT NULL,
  sync_completed_at TEXT,
  direction TEXT NOT NULL,              -- 'upload', 'download'
  entities_processed INTEGER DEFAULT 0,
  conflicts_detected INTEGER DEFAULT 0,
  success INTEGER NOT NULL,             -- 1 = success, 0 = failure
  error_message TEXT,
  duration_ms INTEGER
);
```

### sync_conflicts Table (Branch)

```sql
CREATE TABLE sync_conflicts (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  entity_type TEXT NOT NULL,
  entity_uuid TEXT NOT NULL,
  local_version TEXT NOT NULL,          -- JSON
  remote_version TEXT NOT NULL,         -- JSON
  conflict_type TEXT NOT NULL,
  detected_at TEXT NOT NULL,
  resolved_at TEXT,
  resolution TEXT,                      -- 'local_wins', 'remote_wins', 'manual'
  resolved_by TEXT
);
```

---

## Performance Considerations

### Sync Intervals

- **Default**: Every 5 minutes
- **Low Priority Data**: Every 30 minutes (configuration, non-urgent)
- **High Priority**: Real-time (critical status changes)

### Bandwidth Optimization

- **Differential Sync**: Only changed data transmitted
- **Compression**: Gzip compression on all API payloads
- **Batching**: Group multiple changes into single request
- **Throttling**: Max 10 Mbps during business hours, unlimited off-hours

### Central Server Capacity

- **Branches**: 50 branches supported
- **Requests**: 1000 requests/minute
- **Storage**: 500 GB documents, 100 GB database
- **Concurrent Syncs**: 10 branches simultaneously

---

## Offline Operation

### Graceful Degradation

When network is unavailable:
1. Branch continues normal operations (local Oracle)
2. Changes queued in `sync_queue` table
3. Sync status indicator shows "Offline" (yellow)
4. Operations remain fast (<1 second)
5. No data loss

### Reconnection

When network restored:
1. Sync service detects connectivity
2. Resumes sync operations
3. Processes queued changes (oldest first)
4. Status indicator turns green
5. Admin notification: "Sync restored"

### Extended Offline Period (>1 Week)

- **Risk**: Large sync queue, potential conflicts
- **Mitigation**:
  1. Manual export/import of critical data
  2. Coordinate with other branches to avoid conflicts
  3. Gradual sync (throttled) to avoid overload

---

## Deployment Strategy

### Option 1: Phased Rollout (Recommended)

- **Week 1**: Deploy central server, pilot with Branch 01
- **Week 2**: Add Branch 02-03 (observe sync behavior)
- **Week 3-4**: Roll out to remaining branches (2 per day)

### Option 2: Big Bang

- Deploy central server
- Update all branches simultaneously
- High risk, not recommended for bank deployment

---

## Monitoring and Alerting

### Metrics to Monitor

- **Sync Latency**: Time between local change and central visibility (target: <5 minutes)
- **Conflict Rate**: Conflicts per 1000 documents (target: <1%)
- **Sync Success Rate**: Successful syncs (target: >99%)
- **Queue Depth**: Pending changes per branch (target: <100)
- **Central DB Size**: Growth rate (trigger cleanup if >100 GB)

### Alerts

- **Critical**: Sync failed for >1 hour
- **Warning**: Sync delayed >15 minutes
- **Warning**: Conflict rate >5%
- **Info**: Branch offline >30 minutes

---

## Cost Estimate

### Central Server Infrastructure

- **Cloud Hosting** (Azure/AWS):
  - App Server (4 vCPU, 16 GB RAM): ~$200/month
  - Database (PostgreSQL Managed): ~$150/month
  - Storage (500 GB): ~$50/month
  - Bandwidth (1 TB/month): ~$80/month
  - **Total**: ~$480/month

- **On-Premises**:
  - Server hardware: $5,000-10,000 (one-time)
  - SQL Server license: $15,000+ (one-time) or PostgreSQL (free)
  - Maintenance: $100-200/month

### Development Effort

- **Central Server**: 4-6 weeks (1 senior developer)
- **Branch Client**: 3-4 weeks (1 senior developer)
- **Testing**: 2-3 weeks (1 QA engineer)
- **Total**: 9-13 weeks, ~$50,000-80,000 (at $100-150/hour)

---

## Alternatives to Full Sync

### Option A: Report-Only Sync (Simplified)

- Branches operate independently (no real-time sync)
- Nightly batch job exports data to central
- Central database used for enterprise reporting only
- **Pros**: Simpler, lower cost, minimal conflicts
- **Cons**: Data latency (24 hours), no central user management

### Option B: Manual Export/Import

- Branches export weekly reports to Excel
- Central team manually consolidates
- **Pros**: Zero infrastructure cost, simple
- **Cons**: Manual effort, error-prone, poor scalability

### Option C: Scheduled Database Replication (Read-Only Central)

- Branch Oracle databases replicated to central PostgreSQL (nightly)
- No bidirectional sync, no conflict resolution
- Central database is read-only aggregation
- **Pros**: Simple, low conflict risk
- **Cons**: No central user management, no real-time data

---

## Recommendation

**Start with Option A (Report-Only Sync)** if:
- Budget is limited
- <10 branches
- Real-time central visibility not critical
- Simpler is better for initial deployment

**Proceed with Full Hybrid Sync** if:
- Centralized user management required
- Real-time reporting needed
- >10 branches planned
- Budget allows for $50K+ development + $500/month hosting

---

## Next Steps

1. **User Approval**: Review this design with stakeholders
2. **Budget Approval**: Allocate funds for development and hosting
3. **Technology Selection**: Decide PostgreSQL vs SQL Server
4. **Hosting**: Cloud (Azure/AWS) vs On-Premises
5. **Development**: Hire or assign development team
6. **Timeline**: Set target go-live date

---

**Prepared by**: System Architect  
**Review Date**: TBD  
**Status**: Draft for Discussion
