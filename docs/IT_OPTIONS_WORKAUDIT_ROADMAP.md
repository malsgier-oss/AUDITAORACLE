# WorkAudit — Options for IT Review

**Purpose:** Help IT and stakeholders choose priorities for architecture, delivery, and operations.  
**Context:** Desktop app (`WorkAudit`) with **local Oracle** per installation; optional **HQ Oracle** (or other shared database) and multi-branch patterns are described for planning in [`HYBRID_SYNC_DESIGN.md`](HYBRID_SYNC_DESIGN.md). This repository **does not** ship a separate ASP.NET “central API” service — HQ connectivity (direct SQL, VPN, exports, or a future middleware layer) is an **IT deployment choice**.

> **Pilot phase (single branch, real work):** **Hybrid HQ sync** is **not in scope** for the first pilot. Operations use **local Oracle only** on pilot PCs, with normal **backup/recovery** and **support** as in the admin runbook. The sections below on HQ Oracle and sync are for **after** pilot success, when IT selects a roadmap package. Do **not** require HQ infrastructure or sync for go-live at one branch.

**How to use this doc:** Pick one primary track (e.g. A + D), note dependencies, assign owners, and set a target quarter — **or** use only the pilot note above until multi-branch / HQ reporting is approved.

---

## Proposed approach: Oracle locally + HQ Oracle + sync *only* agreed data

This section states the **recommended default** for IT review. Alternatives (e.g. SQL Server on every PC) remain in section 2.

### Local database (each branch / workstation)

| Proposal | Detail |
|----------|--------|
| **Engine** | **Oracle** — central database service for branch and HQ environments. |
| **Role** | **System of record for day-to-day work** while offline or when HQ is unreachable; fast reads/writes. |
| **Why not replicate full SQL Server to every PC** | Lower operational cost, simpler deployment, fewer licenses and patches; matches the current WorkAudit architecture. |

### HQ database (one designated server)

| Proposal | Detail |
|----------|--------|
| **Engine** | **Microsoft SQL Server** on the HQ machine (or cluster) — **enterprise reporting, compliance, and cross-branch views**. |
| **Role** | **Aggregated / authoritative** data for HQ. Exposure of SQL Server to branch networks (direct client access vs. private link vs. batch export) is an **IT policy** decision; do not expose SQL Server directly to the public internet. |

### What gets synced (only in-scope data)

Sync is **not** a full copy of every Oracle byte to HQ. Only **agreed entity types** and **directions** are replicated (see [`HYBRID_SYNC_DESIGN.md` — Data Sync Scope](HYBRID_SYNC_DESIGN.md#data-sync-scope)).

| Entity / data | Direction (proposal) | Priority |
|---------------|----------------------|----------|
| **Documents (metadata)** | Bidirectional | High |
| **Users** | Central → Branch (read-only) | Medium |
| **Assignments** | Bidirectional | High |
| **Audit log** | Branch → Central (append-only) | Critical |
| **Notes** | Bidirectional | Medium |
| **Configuration** | Central → Branch (admin-initiated) | Low |

### What is *not* synced like normal rows (or not at all)

| Item | Proposal |
|------|----------|
| **File attachments** (binary files) | **Not** synced as part of every row sync by default — **on-demand** or **scheduled batch** (large bandwidth); metadata may still sync. |
| **Sessions** | **Local only** (branch/session-specific). |
| **Temporary / UI state** | **Not** synced (processing queues, UI-only state). |

IT should confirm **attachment strategy** (on-demand vs nightly batch) and **retention** on HQ storage.

### Summary line for stakeholders

**Branches keep Oracle; HQ holds Oracle; the API moves only the agreed sync scope** — not the entire database file and not necessarily all blobs in real time.

---

## 1. Central server & hybrid sync (HQ)

| ID | Option | Summary | Typical dependency |
|----|--------|---------|---------------------|
| **A1** | **SQL Server at HQ** | Designate one server/VM as the **single central database**; secured connection strings and access paths (Key Vault / secret store in production) per IT’s chosen topology. | Server sizing, backup, firewall |
| **A2** | **API authentication** | Protect `/api/sync/*` with **JWT** (or similar) issued after branch/API-key validation; no anonymous sync. | A1 or shared dev DB |
| **A3** | **Persist sync protocol** | Implement real **`POST /api/sync/upload`** and **`GET /api/sync/changes`** against HQ tables (e.g. sync metadata, branch status, conflicts per design). | A1, A2 |
| **A4** | **Desktop sync client** | Add **`ISyncService`** in the WPF app: local **queue**, periodic upload/download, safe behavior **offline**. | A3 stable contract |
| **A5** | **Attachment sync** | Sync files (PDFs, images) to HQ storage (file share / blob) after metadata sync is proven. | A4, storage choice |

**Decision points for IT**

- **HQ SQL Server** edition, HA (Always On?), and **backup/restore** SLA.  
- **Network:** VPN / private link; **do not** expose SQL Server directly to the public internet — use approved secure paths (private connectivity, app tier, or batch) per threat model.  
- **Per-branch identity:** branch ID + API key / certificate rotation policy.

---

## 2. Branch / desktop data store

| ID | Option | Summary | Notes |
|----|--------|---------|--------|
| **B1** | **Oracle per workstation (recommended)** | **Proposed default:** local **Oracle**; **only in-scope** data syncs to HQ via API (see *Proposed approach* above). | Lowest change; aligns with selective sync |
| **B2** | **SQL Server Express / LocalDB per PC** | Same SQL engine as HQ on every client; heavier **ops** (install, patches, backups). | Only if policy requires SQL everywhere |
| **B3** | **Defer client DB change** | Ship sync with **B1** first; revisit B2 only if required. | Recommended default |

---

## 3. Security & compliance

| ID | Option | Summary |
|----|--------|---------|
| **D1** | **Threat model** for HQ connectivity (authz, TLS where applicable, rate limits, audit logging). |
| **D2** | **Secrets management** (no connection strings in source control; use vault or env). |
| **D3** | **Penetration test** or internal security review before production sync. |

---

## 4. Quality & maintainability

| ID | Option | Summary |
|----|--------|---------|
| **Q1** | **Automated tests** for sync DTOs, API contracts, and critical `GetResult`/store paths. |
| **Q2** | **Staging environment** mirroring HQ (e.g. SQL Server) for UAT. |
| **Q3** | **Observability:** health checks, structured logs, alerts on sync failure / lag per branch. |

---

## 5. Product / UX (desktop)

| ID | Option | Summary |
|----|--------|---------|
| **C1** | **Auditor “create draft”** parity with manager Reports (if product wants it). |
| **C2** | **Localization** of new UI strings. |
| **C3** | **Accessibility** improvements (screen reader, keyboard). |
| **C4** | **Navigation cleanup** (avoid magic view indices; named navigation helpers). |

---

## 6. Documentation & operations

| ID | Option | Summary |
|----|--------|---------|
| **E1** | **Single architecture one-pager:** HQ Oracle + local Oracle + API + sync direction. |
| **E2** | **Consolidate** user/admin docs (`docs/` vs `Documentation/`); remove outdated features from guides. |
| **E3** | **Runbooks:** HQ SQL backup/restore, DR drill, branch reconnection after outage. |

---

## 7. Suggested packages (for planning)

**Package A — “Sync MVP” (minimum viable central reporting path)**  
A1 → A2 → A3 → A4 (add D1/D2 in parallel).

**Package B — “Safe production”**  
Package A + D3 + Q2 + Q3 + E3.

**Package C — “Desktop polish only”**  
C1–C4 + E2, **without** committing to HQ sync timeline.

---

## 8. Sign-off (template)

| Question | IT decision |
|----------|-------------|
| HQ database | ☐ SQL Server (version: ______) · ☐ Other: ______ |
| Branch local DB | ☐ **B1 Oracle (proposed)** · ☐ B2 Oracle XE |
| **Sync scope** | ☐ Accept **metadata + listed entities only** (see § Proposed approach) · ☐ Request changes: ________________ |
| **Attachments** | ☐ On-demand · ☐ Scheduled batch · ☐ Other: ______ |
| Sync timeline | ☐ Q___ FY___ |
| Network model | ☐ VPN · ☐ Private link · ☐ Other: ______ |
| Primary package | ☐ A · ☐ B · ☐ C · ☐ Custom: ______ |

**Approvers:** _________________________ **Date:** ___________

---

*Generated for WorkAudit roadmap discussion. Align technical details with [`HYBRID_SYNC_DESIGN.md`](HYBRID_SYNC_DESIGN.md).*
