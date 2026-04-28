# 🚀 Quick Start - Test Arabic Reports in 5 Minutes

## Step 1: Launch Application
```bash
# If running from Visual Studio, press F5
# OR if built, run:
.\bin\Debug\net8.0-windows10.0.19041.0\Audita.exe
```

## Step 2: Generate First English Report
1. Click **Reports** tab
2. Select **"Daily Summary"** from report type dropdown
3. Set date: **Yesterday** only
4. Scroll to **Advanced Options**
5. **Report Language:** Select **"English"**
6. Click **"Generate Report"** button
7. ✅ PDF should open - verify it looks professional

## Step 3: Generate First Arabic Report
1. Same settings as above
2. Change **Report Language:** to **"العربية (Arabic)"**
3. Click **"Generate Report"** button
4. ✅ PDF should open with:
   - Arabic headers (right-aligned)
   - Arabic text readable (not boxes)
   - Western numerals (0-9)
   - Professional blue/gray styling

## Step 4: Test Assignment Report (Critical - Just Fixed)
1. Select **"Assignment Summary"** from report type
2. Date range: **Last 30 days**
3. Test **English** first
4. Test **Arabic** next
5. ✅ Both should work WITHOUT "conflicting size constraints" error

## Step 5: Test Executive Report (Most Complex)
1. Select **"Executive Summary"** from report type
2. Date range: **Last 7 days**
3. Test **English** - should have:
   - Cover page
   - Table of contents
   - Multiple sections
   - KPI cards
   - Charts
4. Test **Arabic** - should have same features in Arabic

---

## ✅ Quick Pass/Fail Criteria

### PASS if:
- ✅ Reports generate without errors
- ✅ Arabic text displays (not boxes □□□)
- ✅ Text is right-aligned for Arabic
- ✅ Charts appear in both languages
- ✅ No layout overflow errors

### FAIL if:
- ❌ Error messages appear
- ❌ Arabic shows as boxes/question marks
- ❌ Text is left-aligned for Arabic
- ❌ Layout looks broken
- ❌ Application crashes

---

## 🐛 If You Hit Issues

**Issue:** Arabic text shows as boxes
→ **Fix:** Install Calibri font (usually pre-installed on Windows)

**Issue:** "Conflicting size constraints" error
→ **Fix:** This was in AssignmentSummaryReport, now fixed. If you see it elsewhere, let me know which report.

**Issue:** Numbers show as ٠١٢٣ instead of 0123
→ **Fix:** Should use Western numerals - if not, report which report type

**Issue:** Application won't launch
→ **Fix:** Check if another instance is running (look in Task Manager for "Audita.exe")

---

## 📊 Full Testing Guide

For comprehensive testing of all 11 reports, see:
**`ARABIC_REPORTS_TESTING_GUIDE.md`**

---

**Estimated Time:** 5-10 minutes for quick smoke test
**Estimated Time:** 1-2 hours for full testing of all 11 reports
