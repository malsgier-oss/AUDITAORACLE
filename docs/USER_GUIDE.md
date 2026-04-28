# WorkAudit User Guide

## Quick Start Guide for End Users

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [User Interface Overview](#user-interface-overview)
3. [Importing Documents](#importing-documents)
4. [Processing Documents](#processing-documents)
5. [Searching Documents](#searching-documents)
6. [Assignments and Workflow](#assignments-and-workflow)
7. [Reports](#reports)
8. [Tips and Best Practices](#tips-and-best-practices)

---

## 1. Getting Started

### Launching WorkAudit

1. Double-click the **WorkAudit** icon on your Desktop
2. Enter your **username** and **password**
3. Click **Login**

### First-Time Login

- You will be required to change your password on first login
- Choose a strong password:
  - At least 8 characters
  - Include uppercase, lowercase, numbers, and symbols
  - Example: `Bank@2026Secure!`

### Forgot Password?

Contact your system administrator to reset your password.

---

## 2. User Interface Overview

### Main Navigation Menu (Left Side)

- **Dashboard** - Overview of your work and recent activity
- **Workspace** - Main document management area
- **Import** - Add new documents to the system
- **Processing** - Enhance and prepare documents
- **Search** - Find documents quickly
- **Archive** - View archived documents
- **Reports** - Generate compliance and audit reports
- **Settings** - Manage your preferences (Admin only)

### Top Bar

- **Username** - Your logged-in username
- **Branch** - Your assigned branch
- **Logout** - Sign out of the application

---

## 3. Importing Documents

### Method 1: Scan from Webcam

1. Click **Import** in the left menu
2. Select **Webcam** tab
3. Position document under camera
4. Click **Capture**
5. Review the captured image
6. Click **Save** to add to workspace

**Tips**:
- Ensure good lighting (no shadows)
- Align document straight
- Use a contrasting background
- Avoid glare from lights

### Method 2: Upload Files

1. Click **Import** in the left menu
2. Select **File Upload** tab
3. Click **Browse** or drag files into the window
4. Select PDF or image files (JPG, PNG, TIFF)
5. Click **Import**

**Supported Formats**:
- PDF documents
- JPEG/JPG images
- PNG images
- TIFF images

### Method 3: Folder Watch (Automatic)

If configured by your administrator, documents placed in a watched folder will be automatically imported.

1. Save files to the watched folder (ask admin for location)
2. Files will appear in your workspace within 1-2 minutes

---

## 4. Processing Documents

### Image Enhancement Tools

After importing, you can enhance document quality:

1. Navigate to **Processing** view
2. Select a document from the list
3. Use the available tools:

#### Crop Tool
- Remove unwanted borders or margins
- Click **Crop**, drag a selection box, click **Apply**

#### Rotate Tool
- Fix orientation
- Click **Rotate Left** or **Rotate Right**

#### Deskew Tool
- Straighten tilted documents
- Click **Auto Deskew** (automatic correction)

#### Brightness/Contrast
- Improve readability
- Adjust sliders for brightness and contrast
- Click **Apply**

#### Perspective Correction
- Fix documents photographed at an angle
- Click **Perspective Correction**
- Mark the four corners of the document
- Click **Apply**

### Entering Document Metadata

**IMPORTANT**: WorkAudit does NOT automatically extract data from documents. You MUST manually enter all information:

1. Select document in Workspace
2. Fill in the fields:
   - **Document Type**: Select from dropdown (e.g., Invoice, Receipt, Contract)
   - **Branch**: Select the relevant branch
   - **Date**: Enter document date (YYYY-MM-DD)
   - **Amount**: Enter monetary amount (if applicable)
   - **Account Number**: Enter account or reference number
   - **Description**: Brief description of the document
3. Click **Save**

---

## 5. Searching Documents

### Quick Search

1. Navigate to **Search** view
2. Enter keywords in the search box
3. Press **Enter** or click **Search**

### Advanced Search

Click **Advanced Filters** to search by:

- **Date Range**: Filter by document date or import date
- **Document Type**: Filter by specific types
- **Branch**: Filter by branch
- **Status**: Filter by workflow status (Pending, Reviewed, Approved, Issue)
- **Amount Range**: Find documents within a monetary range
- **Account Number**: Search by account

### Exporting Search Results

1. Perform a search
2. Click **Export Results**
3. Choose format:
   - **Excel** (.xlsx) - For data analysis
   - **PDF** - For sharing or printing
4. Select destination folder
5. Click **Save**

---

## 6. Assignments and Workflow

### Viewing Your Assignments

1. Navigate to **Dashboard**
2. View **My Assignments** section
3. Click on an assignment to open the document

### Assignment Statuses

- **Pending**: Not yet started
- **In Progress**: Currently being worked on
- **Completed**: Work finished
- **Cancelled**: Assignment was cancelled

### Completing an Assignment

1. Open the assigned document
2. Review the document thoroughly
3. Add notes if needed (right-click → Add Note)
4. Select action:
   - **Approve** - Document is correct and complete
   - **Request Changes** - Document needs corrections
   - **Reject** - Document has critical issues
5. Click **Submit**

### Adding Notes to Documents

1. Right-click on a document
2. Select **Add Note**
3. Choose note type:
   - **Observation**: General comment
   - **Question**: Requires clarification
   - **Issue**: Problem identified
4. Select severity (Info, Low, Medium, High, Critical)
5. Enter note text
6. Click **Save**

---

## 7. Reports

### Available Reports

Navigate to **Reports** view to generate:

- **Executive Summary**: High-level overview with charts
- **Audit Trail**: Complete activity log
- **Compliance Report**: Document status and retention
- **Chain of Custody**: Document lifecycle tracking
- **Performance Report**: Processing metrics
- **Custom Reports**: Build your own reports with specific fields and filters

### Generating a Report

1. Navigate to **Reports**
2. Select report type from dropdown
3. Set date range
4. Apply filters (optional):
   - Branch
   - Document type
   - Status
5. Click **Generate Report**
6. Choose format:
   - **PDF** - For viewing/sharing
   - **Excel** - For data analysis
7. Click **Save** and choose destination

### Custom Report Builder (New Feature)

Create customized reports with exactly the data you need:

#### Creating a Custom Report

1. Navigate to **Reports** > **Custom Report Builder**
2. Click **New Template**
3. Enter template name and description
4. **Select Fields**:
   - Drag fields from Available Fields panel to Selected Fields
   - Reorder fields by dragging
   - Toggle field visibility
5. **Add Filters** (optional):
   - Click **Add Filter**
   - Choose field, operator (equals, contains, etc.), and value
   - Combine multiple filters with AND/OR logic
6. **Add Sorting** (optional):
   - Select sort field and direction (ascending/descending)
7. **Preview** to see sample data
8. Click **Save Template**

#### Using Saved Templates

1. Navigate to **Reports** > **Custom Report Builder**
2. Click **Load Template**
3. Select a template from your list
4. Adjust date range if needed
5. Click **Generate Report**
6. Export to Excel or PDF

#### Sharing Templates

- **Private**: Only you can see and use the template
- **Shared**: All users in your branch can use the template
- To share: Check "Share with others" when saving template

#### Example Use Cases

- **Monthly Invoice Report**: Filter by "Invoice" document type, last 30 days, sorted by date
- **Draft Documents by Branch**: Filter by "Draft" status, group by branch
- **High-Value Documents**: Filter documents with amounts > $10,000
- **Compliance Review**: Documents pending review, overdue status

### Scheduled Reports

If configured by your administrator, reports will be automatically generated and emailed to specified recipients.

---

## 8. Tips and Best Practices

### Document Scanning Best Practices

✅ **DO:**
- Use consistent lighting
- Align documents straight
- Use high resolution (300 DPI minimum)
- Scan one document at a time
- Review image quality before saving

❌ **DON'T:**
- Scan with wrinkled or folded documents
- Use low lighting or flash
- Scan multiple documents together
- Skip image enhancement tools
- Forget to enter metadata

### Data Entry Best Practices

✅ **DO:**
- Double-check all entered data
- Use consistent date formats (YYYY-MM-DD)
- Include leading zeros in account numbers
- Be descriptive in document descriptions
- Add notes for unusual documents

❌ **DON'T:**
- Leave required fields empty
- Use abbreviations that others won't understand
- Enter data from memory (always verify from document)
- Skip assignment review
- Ignore document quality issues

### Performance Tips

- **Close unused documents** - Don't keep too many documents open
- **Use filters** - Don't load thousands of documents at once
- **Archive old documents** - Keep workspace clean
- **Logout when done** - Free up system resources

### Security Best Practices

- **Never share passwords** - Each user must have their own account
- **Lock your screen** - When leaving your desk (Win+L)
- **Logout daily** - Don't stay logged in overnight
- **Report suspicious activity** - Contact security immediately
- **Use strong passwords** - Follow password policy

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+I` | Import document |
| `Ctrl+S` | Save current document |
| `Ctrl+F` | Search/Find |
| `Ctrl+P` | Print current document |
| `Ctrl+Z` | Undo last edit |
| `Ctrl+R` | Refresh current view |
| `F5` | Refresh dashboard |
| `Esc` | Close current dialog |

---

## Troubleshooting for End Users

### I can't login

- Verify username and password (case-sensitive)
- Check Caps Lock is OFF
- Wait 1 minute if you had multiple failed attempts
- Contact administrator if locked out

### Document import is slow

- Check file size (large PDFs take longer)
- Ensure sufficient disk space
- Close other programs using camera (if scanning)
- Check network connection (if importing from network drive)

### I accidentally deleted a document

- **IMMEDIATELY** contact your administrator
- Do NOT continue working
- Administrator can restore from backup

### The application froze

1. Wait 30 seconds (may be processing)
2. If still frozen, click Ctrl+Alt+Del
3. Open Task Manager
4. End "WorkAudit" process
5. Restart application
6. Contact IT if happens frequently

---

## Glossary

| Term | Definition |
|------|------------|
| **Audit Trail** | Complete log of all actions in the system |
| **Assignment** | A document assigned to a user for review or action |
| **Branch** | Physical bank branch location |
| **Custodian** | Person responsible for document custody |
| **Document Type** | Category of document (Invoice, Receipt, etc.) |
| **Legal Hold** | Document protected from deletion for legal reasons |
| **Metadata** | Information about the document (date, amount, etc.) |
| **Retention** | How long a document must be kept |
| **Status** | Current state of document (Pending, Approved, etc.) |
| **Workflow** | Series of steps for document processing |

---

## Getting Help

### In-Application Help

- Hover over any field to see tooltips
- Click **?** icon for context-sensitive help
- Press **F1** for help documentation

### Contact Support

- **Internal IT Support**: it-support@yourbank.com
- **Phone**: +XXX-XXX-XXXX (24/7)

### Training

- **Initial Training**: 2-hour session for new users
- **Advanced Training**: Available on request
- **Quick Reference Cards**: Available in `docs/` folder

---

**Document Version**: 1.0  
**Last Updated**: 2026-02-22  
**For**: WorkAudit v1.0.0
