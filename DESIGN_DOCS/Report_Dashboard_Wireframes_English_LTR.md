# Report Dashboard Wireframes - English (LTR Layout)

## Overview
The Report Tab transformation into an Audit Manager's Intelligence Dashboard with comprehensive notes integration and professional export capabilities.

---

## 1. Dashboard Header Section
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 📊 Audit Intelligence Dashboard                    [Language: English ▼]   │
│ Audit Period: 2024-01-01 to 2024-03-31                                     │
│                                                                             │
│ [📅 Change Period]  [🔄 Refresh]  [⚙️ Settings]  [📤 Export Report ▼]     │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Features:**
- Left-aligned header with clear hierarchy
- Language switcher in top-right corner
- Period selector with quick presets (This Month, Quarter, YTD, Custom)
- Export dropdown: [PDF Report | Excel Data | Print Preview]

---

## 2. KPI Summary Cards (4-Column Grid)
```
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ 📄 FILES     │ │ ⚠️ CRITICAL   │ │ ✅ COMPLIANCE │ │ 📊 COVERAGE  │
│ SCANNED      │ │ ISSUES       │ │ RATE         │ │ RATE         │
│              │ │              │ │              │ │              │
│   1,247      │ │     23       │ │   94.2%      │ │   87.5%      │
│   +12% ↗     │ │   -5% ↘      │ │   +2.1% ↗    │ │   +4.3% ↗    │
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
```

**Card Details:**
- Icon + Label at top
- Large number (primary metric)
- Trend indicator with arrow and percentage
- Color coding: Green (good), Yellow (warning), Red (critical)
- Hover tooltip shows detailed breakdown

---

## 3. Findings Table with Notes Integration
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Audit Findings & Observations                            [🔍 Filter] [⚙️]  │
├─────────────────────────────────────────────────────────────────────────────┤
│ Severity │ File Name              │ Type      │ Branch    │ Notes │ Status  │
├──────────┼────────────────────────┼───────────┼───────────┼───────┼─────────┤
│ 🔴 HIGH  │ Loan_Agreement_042.pdf │ Contract  │ Main St.  │ 3📝   │ Open    │
│          │ ↳ Missing signature on page 3, compliance risk                   │
│ 🟡 MED   │ Tax_Return_2023.pdf    │ Tax Doc   │ Downtown  │ 1📝   │ Review  │
│ 🟢 LOW   │ Receipt_INV001.jpg     │ Receipt   │ Uptown    │ 0     │ Cleared │
│ 🔴 HIGH  │ Audit_Report_Q1.docx   │ Report    │ Main St.  │ 5📝   │ Open    │
│          │ ↳ Data inconsistencies found, requires CFO review               │
└─────────────────────────────────────────────────────────────────────────────┘
                                               Showing 4 of 87 findings [1 2 3...]
```

**Features:**
- Severity column with color-coded icons
- Note count indicator (📝 with number)
- Expandable inline preview of critical notes
- Click note count → Opens detailed notes panel
- Right-click context menu: [View Document | Add Note | Mark Resolved]
- Sortable columns
- Inline filtering by severity, status, branch

---

## 4. Notes Detail Panel (Slide-out from right)
```
                                    ┌────────────────────────────────────┐
                                    │ Notes: Loan_Agreement_042.pdf  [✕] │
                                    ├────────────────────────────────────┤
                                    │ [📝 Add New Note]                  │
                                    │                                    │
                                    │ ┌──────────────────────────────┐  │
                                    │ │ 🔴 ISSUE | John D. | 2h ago   │  │
                                    │ │ Missing signature on page 3.  │  │
                                    │ │ Compliance violation per      │  │
                                    │ │ Section 12.4                  │  │
                                    │ │ [📎 screenshot.png]           │  │
                                    │ │ Tags: #compliance #urgent     │  │
                                    │ └──────────────────────────────┘  │
                                    │                                    │
                                    │ ┌──────────────────────────────┐  │
                                    │ │ 📋 OBSERVATION | Sarah K.     │  │
                                    │ │ Loan amount exceeds branch    │  │
                                    │ │ approval limit ($500K). Needs │  │
                                    │ │ regional manager sign-off.    │  │
                                    │ └──────────────────────────────┘  │
                                    │                                    │
                                    │ ┌──────────────────────────────┐  │
                                    │ │ ✅ EVIDENCE | Mike T.         │  │
                                    │ │ Verified with customer via    │  │
                                    │ │ phone call. Signature pending │  │
                                    │ │ courier delivery.             │  │
                                    │ └──────────────────────────────┘  │
                                    └────────────────────────────────────┘
```

**Note Card Structure:**
- Type indicator icon (🔴 Issue, 📋 Observation, ✅ Evidence, 💡 Recommendation)
- Author name + timestamp
- Note content (rich text)
- Attachments section
- Tags for categorization
- Edit/Delete icons (for note creator)

---

## 5. Executive Summary Section (Auto-Generated)
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 📋 Executive Summary                                    [Regenerate] [Edit] │
├─────────────────────────────────────────────────────────────────────────────┤
│ Audit Scope & Period                                                        │
│ This audit covers 1,247 documents processed between January 1 and March 31, │
│ 2024, spanning 12 branches across 5 sections. The review focused on loan    │
│ agreements, tax documents, and compliance reports.                          │
│                                                                             │
│ Key Findings                                                                │
│ • 23 critical issues identified, primarily in loan documentation (78%)      │
│ • Compliance rate improved to 94.2%, up from 92.1% in previous quarter      │
│ • Main Street branch shows highest volume but also highest issue rate       │
│ • 87% of required documents successfully processed and reviewed             │
│                                                                             │
│ Risk Assessment                                                             │
│ Overall risk posture: MODERATE. While compliance improvements are noted,    │
│ signature verification gaps and documentation delays require attention.     │
│                                                                             │
│ [Click to expand full analysis...]                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Features:**
- AI-generated summary based on findings data
- Editable by authorized users
- Collapsible/expandable sections
- Language-specific generation (English or Arabic)

---

## 6. Smart Recommendations Engine
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 💡 Recommended Actions                                   [Dismiss All] [+]  │
├─────────────────────────────────────────────────────────────────────────────┤
│ 🔴 HIGH PRIORITY                                                            │
│ ├─ Address 12 missing signatures in Main Street branch loan docs           │
│ │  Impact: Regulatory non-compliance | Est. effort: 2-3 days               │
│ │  [Assign Task] [Create Report] [✓ Mark Acknowledged]                     │
│ │                                                                           │
│ 🟡 MEDIUM PRIORITY                                                          │
│ ├─ Review approval workflows for loans exceeding $500K                     │
│ │  Impact: Process efficiency | Est. effort: 1 day                         │
│ │  [Schedule Review] [✓ Mark Acknowledged]                                 │
│ │                                                                           │
│ 🟢 LOW PRIORITY                                                             │
│ └─ Update document type classification for 15 tax returns                  │
│    Impact: Data quality | Est. effort: 30 mins                             │
│    [Bulk Update] [✓ Mark Acknowledged]                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Recommendation Logic:**
- Pattern analysis: "12 loan docs missing signatures" → "Implement signature verification checklist"
- Severity distribution: "High issue concentration in Branch X" → "Conduct branch-specific training"
- Historical trends: "Repeat issues in tax docs" → "Revise tax document processing procedure"
- Regulatory requirements: "GDPR compliance gaps" → "Schedule compliance review"

---

## 7. Risk Assessment Matrix
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Risk Assessment Matrix                                                      │
│                                                                             │
│            Impact →                                                         │
│      │  Low    │ Medium  │  High   │ Critical │                            │
│ ─────┼─────────┼─────────┼─────────┼──────────┤                            │
│ HIGH │   2     │    5    │   12    │    4     │                            │
│ ─────┼─────────┼─────────┼─────────┼──────────┤                            │
│ MED  │   8     │   15    │    3    │    0     │                            │
│ ─────┼─────────┼─────────┼─────────┼──────────┤                            │
│ LOW  │  23     │    9    │    1    │    0     │                            │
│ ─────┴─────────┴─────────┴─────────┴──────────┘                            │
│        Likelihood →                                                         │
│                                                                             │
│ Click any cell to filter findings by risk category                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Interactive Features:**
- Click cell → Filter findings table to show only those items
- Color-coded heat map (red = high risk, yellow = medium, green = low)
- Drill-down to see specific findings in each category

---

## 8. Export Configuration Panel
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Export Professional Audit Report                                      [✕]  │
├─────────────────────────────────────────────────────────────────────────────┤
│ Report Template                                                             │
│ ◉ Executive Summary (comprehensive, all sections)                          │
│ ○ Findings Only (detailed findings table with notes)                       │
│ ○ Management Brief (KPIs + summary + recommendations)                      │
│ ○ Custom (select sections below)                                           │
│                                                                             │
│ ☑ Include Sections                                                         │
│   ☑ Executive Summary                                                      │
│   ☑ KPI Dashboard                                                          │
│   ☑ Detailed Findings Table                                                │
│   ☑ All Notes & Observations        ┌─────────────────────────────┐       │
│   ☑ Risk Assessment Matrix          │ Note Detail Level:          │       │
│   ☑ Recommendations                 │ ◉ Full (all note content)   │       │
│   ☑ Charts & Visualizations         │ ○ Summary (note count only) │       │
│   ☐ Raw Data Appendix               │ ○ None (exclude notes)      │       │
│                                      └─────────────────────────────┘       │
│ Format Options                                                              │
│ Format: [PDF ▼]  Language: [English ▼]  Page Size: [A4 ▼]                 │
│ ☑ Include company branding     ☑ Add page numbers                         │
│ ☑ Table of contents            ☑ Digital signature placeholder            │
│                                                                             │
│ [📥 Export to PDF]  [👁️ Preview]  [Cancel]                                │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Export Features:**
- Template presets for different audiences
- Granular section control
- Note inclusion options (critical for your requirement)
- Professional formatting options
- Preview before export

---

## 9. Section Toggle Controls
```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Dashboard Sections                                         [Customize View] │
├─────────────────────────────────────────────────────────────────────────────┤
│ ▼ KPI Summary (4 cards visible)                                            │
│ ▼ Findings Table (87 items, 3 filters active) ──────────────── [Expand ▼]  │
│ ▶ Executive Summary (collapsed)                                            │
│ ▼ Recommendations (3 high priority)                                        │
│ ▶ Risk Matrix (collapsed)                                                  │
│ ▼ Trend Analysis (6-month chart visible)                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Collapsible Panels:**
- Each major section can be collapsed to reduce clutter
- Section headers show summary stats even when collapsed
- User preferences saved per-user

---

## 10. Bilingual Number Formatting Example
```
English Layout:
┌──────────────┐
│ 📄 FILES     │
│ SCANNED      │
│              │
│   1,247      │  ← Western numerals, comma separator
│   +12.5% ↗   │  ← Decimal point
└──────────────┘

Arabic Layout (shown in next wireframe document):
┌──────────────┐
│   الملفات 📄  │  ← RTL icon placement
│  الممسوحة    │
│              │
│   ١٬٢٤٧     │  ← Arabic-Indic numerals (optional), RTL comma
│   ↗ ١٢٫٥٪+   │  ← RTL percentage, Arabic decimal separator
└──────────────┘
```

---

## Implementation Notes

### Column Ordering Logic
```csharp
// English (LTR): Severity → File → Type → Branch → Notes → Status
// Arabic (RTL):  Status → Notes → Branch → Type → File → Severity
// Logical order remains same, visual presentation mirrors
```

### Font Selection
- **English:** Segoe UI, Calibri, Arial (clear, professional)
- **Arabic:** Arabic Typesetting, Tahoma, Traditional Arabic (proper diacritics support)
- **Code:** Must support both font families and switch via resource dictionary

### Spacing & Layout
- **English:** Standard 8px/16px grid, left-to-right content flow
- **Arabic:** Same grid, right-to-left content flow, wider spacing for Arabic script (1.1x line height)

---

## Responsive Behavior

### Minimum Width: 1280px (optimal for dashboard)
- Below 1280px: Switch to vertical stack layout
- KPI cards: 4 columns → 2 columns → 1 column
- Findings table: Hide less critical columns, show on expand
- Charts: Maintain aspect ratio, reduce height on narrow screens

### Print Layout
- A4 portrait for reports
- A4 landscape for wide tables/charts
- Automatic page breaks at section boundaries
- Header/footer with page numbers and audit metadata

---

## Accessibility Considerations

- **Keyboard Navigation:** Tab through all interactive elements
- **Screen Readers:** ARIA labels on all icons, meaningful button text
- **High Contrast Mode:** Ensure severity colors work in Windows High Contrast
- **Font Scaling:** Support 100%-200% text zoom without layout breaks

---

## Performance Targets

- **Initial Load:** < 2 seconds for 1,000 findings
- **Filter/Sort:** < 200ms for 10,000 findings
- **Note Panel Open:** < 100ms to slide out and render notes
- **Export Generation:** < 5 seconds for 50-page PDF with charts

---

**Next:** See `Report_Dashboard_Wireframes_Arabic_RTL.md` for Arabic layout specifics
