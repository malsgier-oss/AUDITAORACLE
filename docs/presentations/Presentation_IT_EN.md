# WorkAudit — IT Overview  
**Audience:** IT Department  
**Format:** Simple slide outline (copy into PowerPoint or similar)

---

## Slide 1 — Title

**WorkAudit — Technical Snapshot**  
*Desktop document platform · Oracle · Windows*

Presenter name · Date

---

## Slide 2 — Application type

| Item | Detail |
|------|--------|
| **Client** | WPF desktop (.NET 8) |
| **Database** | Oracle (19c target; ODP.NET) |
| **Files** | File system for attachments, images, PDFs |
| **Typical deploy** | Per workstation / branch; connection via configured Oracle (incl. TNS/wallet where used) |

---

## Slide 3 — What the system does (technical)

- **Document lifecycle:** Import (files, folder watch, webcam), processing (image/PDF), metadata, deduplication (hash).  
- **Workflow & security:** RBAC (Admin, Manager, Auditor, Viewer), assignments, approvals, audit trail.  
- **Reporting:** PDF/Excel/CSV, report history, templates, scheduled generation (e-mail where configured).  
- **Operations:** Structured logging, health/monitoring hooks, backup & recovery features (encrypted backups, verification).

---

## Slide 4 — Security stack (summary)

- Password hashing (BCrypt).  
- Encryption for sensitive config (e.g. SMTP, backups); DPAPI for machine-bound secrets where applicable.  
- Session management; comprehensive audit logging.  
- **Note:** Stakeholder security audit and DR drill are still recommended before full production (per internal readiness docs).

---

## Slide 5 — Environment & requirements

- **OS:** Windows 10 (1809+) / Windows 11.  
- **Runtime:** .NET 8 Desktop Runtime.  
- **RAM / disk:** Plan per admin runbook (e.g. 8 GB RAM, 50 GB+ disk for heavier branches).  
- **Oracle:** Valid connection string / wallet; firewall between app host and DB as per policy.

---

## Slide 6 — Deployment artifacts

- **MSI installer** for controlled rollout.  
- **Documentation:** Admin runbook, deployment notes, optional roadmap docs (e.g. HQ / sync — future IT decision, not required for single-branch pilot).  
- **Testing:** Unit, integration, and load tests in repository — supports CI confidence.

---

## Slide 7 — IT discussion topics

| Topic | Question for IT |
|------|------------------|
| **Topology** | Single local Oracle per branch vs. future HQ aggregation — which phase? |
| **Identity** | Directory integration vs. local users (current model is app-managed accounts unless extended). |
| **Backup** | Who runs DB backup vs. app-level backup; RPO/RTO targets. |
| **Monitoring** | Central log collection, alerts, and patching calendar for Oracle + Windows. |

---

## Slide 8 — One-line summary

**WorkAudit** is a **.NET 8 WPF client on Oracle** delivering **document workflows, RBAC, reporting, and auditability** for banking operations — **deployment and HQ sync patterns are IT-owned decisions** documented in-repo.

---

*End of outline*
