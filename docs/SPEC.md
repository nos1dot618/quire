# Quire
## Product & Technical Specification

**Project name:** Quire  
**Category:** Native Windows document and scratchpad application  
**Primary platform:** Windows 10 and Windows 11  
**Language:** C#  
**UI framework:** Windows Presentation Foundation (WPF)  
**Runtime:** .NET 10 LTS  
**Document format:** `.quire`  
**Design philosophy:** Minimal, local-first, document-centric, and native-feeling

---

## 1. Overview

Quire is a lightweight Windows desktop application for collecting, organizing, editing, and exporting miscellaneous information in a single document.

It is designed for information that does not necessarily belong in a conventional word processor, code editor, or note-taking application: commit SHAs, branch names, pull request links, file paths, terminal output, code snippets, screenshots, images, GIFs, and short notes.

The application uses a notebook-inspired cell model. Users can create named cells, arrange them vertically, and place multiple kinds of content within each cell.

A cell is a logical container, not a single content type. For example, one cell might contain a heading, explanatory text, a screenshot, a link to a pull request, and a code snippet.

Quire should feel like a simple Windows text editor with a document occupying almost the entire window. It should not resemble a dashboard, a web application, or a complex productivity suite.

### Core principles

1. **Capture first:** Adding information should require minimal effort.
2. **Mixed content:** A single cell can contain any combination of supported content elements.
3. **Document-centric:** The document, rather than application chrome or navigation, is the primary interface.
4. **Native-feeling:** Follow the host operating system's conventions and appearance.
5. **Local-first:** Documents and attachments are stored locally by default.
6. **Portable:** A document and its attachments should be transferable as a single file.
7. **Editable and exportable:** Content should remain useful outside Quire.
8. **Simple architecture:** Avoid unnecessary databases, services, web runtimes, and infrastructure.

---

## 2. Target Use Cases

Quire should support everyday reference and scratchpad workflows, including:

- Collecting commit hashes, branch names, and repository paths while working on a pull request.
- Keeping pull request URLs, review comments, and test results together.
- Saving terminal output alongside screenshots of failures.
- Collecting images, GIFs, links, and notes during debugging.
- Recording temporary implementation plans and release checklists.
- Organizing technical research into named sections.
- Preparing notes that can later be exported to Markdown or PDF.

A typical document might contain a cell named `Pull request #519`, with a summary, a GitHub URL, a code excerpt, and a screenshot of the relevant test output.

Quire is not intended to replace a full IDE, source-control client, database, or general-purpose knowledge management platform.

---

## 3. User Interface and Experience

### 3.1 Overall window

The application should use a conventional desktop window with a standard menu bar and a document canvas.

The main window should show the document and nothing unnecessary.

There should be no permanently visible sidebar, dashboard, ribbon, floating toolbar, or card-based navigation system.

### 3.2 Menu bar

The initial menu structure should be:

- **File:** New, Open, Save, Save As, Recent Documents, Export, Exit.
- **Edit:** Undo, Redo, Cut, Copy, Paste, Select All, Find.
- **Insert:** Cell, Text, Markdown, Code, Image, Link, File, Divider.
- **Cell:** Rename, Duplicate, Move Up, Move Down, Convert, Delete.
- **View:** Theme, Zoom, Preview Mode, Full Screen.
- **Help:** Documentation, About Quire.

Context menus may expose less frequently used actions.

Keyboard shortcuts should be provided for common operations.

### 3.3 Visual design

Quire should have a restrained, rectangular visual style.

Requirements:

- Square corners on application-defined surfaces and cell boundaries.
- No gradients, decorative shadows, or floating cards.
- Minimal borders and subtle selection indicators.
- Compact typography and conventional spacing.
- No permanent cell action toolbar.
- No excessive icons or visual ornamentation.
- Light and dark appearance support.
- Keyboard navigation and accessibility support.

The document canvas should occupy nearly all available window space.

### 3.4 Windows 10 and Windows 11 adaptation

Quire should use WPF while respecting the appearance and capabilities of the operating system on which it runs.

The goal is for Quire to feel appropriate on both Windows 10 and Windows 11, rather than forcing one operating system's visual language onto the other.

Implementation guidelines:

- Use standard Windows window behavior and native file dialogs.
- Avoid unnecessary custom title bars and window chrome.
- Prefer system-aware colors and accessibility settings.
- Use an application theme that adapts to the detected OS version where appropriate.
- Keep custom document styling minimal and consistent.
- Apply OS-specific styling only where it provides a meaningful benefit.

**Important:** WPF does not automatically reproduce every native Windows 10 or Windows 11 control style. Its default controls and application-defined templates may look different from the system's own applications. Quire will need a deliberate theme strategy and testing on both operating systems. Native dialogs and standard window behavior should be retained wherever practical.

Windows 10 compatibility must be validated against the actual target OS builds and supported .NET runtime requirements.

---

## 4. Document and Cell Model

### 4.1 A document contains cells

A Quire document consists of an ordered collection of cells.

Each cell has:

- A stable unique identifier.
- A user-editable name.
- An ordered collection of content elements.
- Optional cell-level metadata.
- Optional presentation settings.

Cells should be independently reorderable, duplicable, and removable.

### 4.2 A cell can contain multiple elements

This is a fundamental requirement.

**A cell is a container for mixed content, not a synonym for a text block, image, or Markdown block.**

For example, a cell named `Test failure` could contain:

1. A Markdown heading.
2. An explanatory paragraph.
3. A screenshot.
4. A code block containing the failing command.
5. A link to the relevant issue.

Another cell might contain only a commit SHA, while a third could contain a GIF followed by explanatory text and a file path.

The user should be able to interleave content elements in any supported order.

A simplified conceptual structure:

    Document
    ├── Cell: Release notes
    │   ├── Markdown heading
    │   ├── Text paragraph
    │   └── Link
    ├── Cell: Test failure
    │   ├── Text paragraph
    │   ├── Image
    │   ├── Code block
    │   └── Link
    └── Cell: Useful references
        ├── File path
        ├── Commit SHA
        └── GIF

The user should be able to add, edit, delete, reorder, and convert individual elements without recreating the entire cell.

### 4.3 Cell names

Cell names are used to identify and organize information.

Examples:

- `PR #519`
- `Release branch`
- `Memory leak investigation`
- `Test logs`
- `Useful commands`
- `Screenshot — failure`

Names should be optional, editable, and searchable.

A cell name is metadata, not necessarily part of its exported content. Export behavior should allow the name to be included as a heading when appropriate.

### 4.4 Content element types

The initial model should support the following elements:

| Element | Purpose |
|---|---|
| Plain text | Arbitrary text, SHAs, identifiers, paths, and notes |
| Markdown | Structured text with headings, lists, links, and formatting |
| Code | Source code, commands, logs, and fixed-width text |
| Link | A URL with an optional display title |
| Image | A raster image with inline preview |
| GIF | An animated image with inline preview |
| File reference | A path to an external file |
| Attachment | A file copied into the document |
| Divider | A visual separator within a cell |

A link may be represented as a dedicated element or as a link within rich text, depending on the editing context.

### 4.5 Automatic content recognition

Quire should make pasting and dragging content effortless.

Examples:

- Pasting an image creates an image element.
- Dragging an image into a cell inserts an image element.
- Pasting a URL creates a link element when appropriate.
- Dropping a file inserts a file reference or imports an attachment according to the chosen behavior.
- Pasting multiple supported items preserves their order where possible.
- Pasting ordinary text inserts editable text.

Automatic recognition should not be overly aggressive.

A commit SHA or branch name should remain ordinary editable text unless the user explicitly chooses a specialized representation.

Existing cells should accept additional elements without forcing users to create separate cells for each content type.

### 4.6 Editing and presentation

Quire should support inline editing and rendered presentation.

Markdown elements should support source editing and rendered preview. Users should be able to switch between the two without losing their original Markdown.

Code elements should support monospaced text and basic syntax highlighting.

Images and GIFs should appear inline at an appropriate size while preserving their original content. Users should be able to open or copy them and, where appropriate, replace them.

Editing should preserve the order and identity of individual elements.

---

## 5. Document Storage

### 5.1 File format

Quire should use a portable document file with the `.quire` extension.

The proposed format is a ZIP archive containing a structured JSON manifest and binary assets.

Example:

    example.quire
    ├── document.json
    └── assets/
        ├── image-001.png
        ├── animation-001.gif
        └── attachment-001.txt

The manifest stores document metadata, cell ordering, cell names, element ordering, element types, text content, formatting information, and asset references.

Binary assets remain separate files inside the archive.

### 5.2 Why JSON and ZIP?

This approach offers several benefits:

- One file contains the complete document.
- Images and GIFs retain their original binary representation.
- Textual content remains inspectable and easy to migrate.
- The format can be versioned.
- The archive can be copied, backed up, and transferred.
- Exporters can work directly from the structured document model.

The internal format should not depend on WPF-specific serialized visual trees. Persist document meaning and content rather than UI controls.

### 5.3 Storage reliability

The application must protect users from accidental data loss.

Requirements:

- Autosave or recoverable draft state.
- Atomic replacement when saving documents.
- Recovery after interrupted writes where possible.
- Validation of document structure before loading.
- Clear errors for corrupted or unsupported documents.
- Versioned schemas and explicit migration logic.
- Safe handling of missing attachments.

Large binary assets should not be unnecessarily converted to Base64 or embedded as giant JSON strings.

### 5.4 External files versus embedded attachments

Quire should distinguish between a file reference and an embedded attachment.

A file reference points to an external path. It does not guarantee that the file will remain available after moving the document.

An embedded attachment is copied into the document archive and remains available when the document is transferred.

The UI should make this distinction understandable.

---

## 6. Export

Export should preserve the document's meaningful structure and content wherever the target format permits.

### 6.1 Markdown

Export cells in document order.

- Cell names can become headings.
- Markdown content should retain its formatting.
- Plain text should remain readable.
- Code should use fenced code blocks.
- Links should use Markdown link syntax.
- Images should be copied into an accompanying assets directory and referenced with relative paths.

The exporter should not claim that a single `.md` file contains binary images unless those images are separately embedded using a supported representation.

### 6.2 PDF

PDF export should create a printable document rather than capture screenshots of the application window.

Requirements:

- Selectable text.
- Embedded images where supported.
- Readable code blocks.
- Sensible page breaks.
- Appropriate margins and typography.
- Optional cell headings.
- Support for long documents and large images.

The PDF exporter should be independent of the UI layer.

### 6.3 HTML

HTML export should provide a readable web document.

It may use an accompanying asset directory or a self-contained representation where practical.

### 6.4 Plain text

Plain-text export should flatten supported content into a readable sequence.

Cell names may be included as section headings. Image and attachment content should receive appropriate textual placeholders where binary content cannot be represented.

### 6.5 Original document

The `.quire` format remains the canonical format because Markdown, HTML, PDF, and plain text cannot preserve every application-specific property.

---

## 7. Technology Stack

### 7.1 Core stack

- **Language:** C#
- **UI:** Windows Presentation Foundation (WPF)
- **Runtime:** .NET 10 LTS
- **Document model:** Plain C# classes and collections
- **Serialization:** System.Text.Json
- **Archive handling:** System.IO.Compression
- **Markdown parsing and rendering:** Markdig
- **PDF export:** A dedicated .NET PDF generation library
- **Build system:** SDK-style project files and the .NET CLI
- **Testing:** xUnit or NUnit

WPF is chosen because it is a mature, established desktop UI framework with extensive documentation and a long history of Windows desktop use.

This choice prioritizes stability, developer control, and a conventional desktop development model over adopting a newer UI framework solely for its default appearance.

### 7.2 Architectural principle

The document model and business logic should remain independent of WPF.

The UI should be responsible for editing, presenting, and manipulating the document. Storage and export should operate on the document model rather than inspecting live UI controls.

This separation makes it easier to test the application, change the UI later, and add new content element types.

### 7.3 Deployment

Quire should support a straightforward Windows installation and launch experience.

Deployment options should be evaluated during implementation:

- Framework-dependent deployment when the required .NET runtime is installed.
- Self-contained deployment when bundling the runtime is preferable.
- An installer for normal Windows installation and application registration.

The initial release should prioritize a reliable installer and predictable upgrades over advanced packaging or distribution infrastructure.

---

## 8. Suggested Project Structure

A small solution is sufficient.

    Quire.sln
    ├── src/
    │   └── Quire.App/
    │       ├── UI/
    │       ├── ViewModels/
    │       ├── Models/
    │       ├── Storage/
    │       ├── Editing/
    │       ├── Export/
    │       └── Themes/
    └── tests/
        └── Quire.Tests/

The initial implementation can use a single application project with well-separated folders.

A separate core library may be introduced when the document model and storage logic become substantial enough to justify it. Multiple projects and abstraction layers should not be created merely for architectural appearance.

### Main responsibilities

**Models**

Represent documents, cells, content elements, and attachments.

**UI and ViewModels**

Present the document, handle cell selection, and coordinate editing commands.

**Editing**

Handle insertion, deletion, reordering, clipboard operations, and undo/redo.

**Storage**

Serialize the document, manage assets, validate archives, and implement saving and loading.

**Export**

Convert the document model into Markdown, HTML, PDF, and plain text.

**Themes**

Manage application appearance, system settings, and any required Windows-version-specific styling.

---

## 9. Initial Release Scope

The MVP should establish the core editing and document model before introducing secondary features.

### Application foundation

- Main window and menu bar.
- Create, open, save, and save-as operations.
- Document and cell models.
- Add, rename, reorder, duplicate, and delete cells.
- Minimal rectangular document canvas.

### Mixed-content editing

- Multiple elements within each cell.
- Plain text and Markdown editing.
- Markdown preview.
- Code blocks.
- Image and GIF insertion.
- Clipboard paste and file drag-and-drop.
- Link and path handling.

### Persistence and reliability

- Versioned `.quire` format.
- JSON manifest and binary attachments.
- Autosave and crash recovery.
- Undo and redo.
- Document validation and migration.

### Export and usability

- Markdown export.
- PDF export.
- HTML and plain-text export.
- Search within documents.
- Keyboard shortcuts.
- Recent documents.
- Light and dark modes.
- Windows 10 and Windows 11 compatibility testing.

---

## 10. Out of Scope for the MVP

The initial release should not include:

- Cloud synchronization.
- User accounts or authentication.
- Collaboration or shared editing.
- A database server.
- Plugin systems.
- AI-powered organization.
- A browser-based interface.
- A scripting or code execution environment.
- Complex tagging, dashboards, or project management features.
- Automatic interpretation of arbitrary text into specialized objects.

These features should only be considered after the basic document workflow is reliable and useful.

---

## 11. Acceptance Criteria

The first stable release should meet the following requirements.

1. Users can create and save a document containing multiple named cells.
2. Each cell can contain multiple heterogeneous content elements.
3. Elements can be inserted, edited, removed, and reordered independently.
4. Pasting and dragging supported content into a cell behaves predictably.
5. Markdown can be edited as source and viewed as rendered content.
6. Images and GIFs remain available after saving, closing, and reopening a document.
7. A `.quire` document can be moved to another compatible installation without losing embedded assets.
8. Markdown and PDF exports produce readable documents.
9. The main window remains document-centric and free of permanent sidebars or unnecessary toolbars.
10. The interface respects the intended square-corner visual design.
11. Windows 10 and Windows 11 are tested separately, with the supported OS versions explicitly documented.
12. Interrupted saves and invalid documents do not silently destroy existing data.

---

## 12. Final Direction

Quire should be a small, polished, native-feeling Windows scratchpad organized around named cells and mixed-content documents.

Its defining feature is the ability to collect unrelated information together without sacrificing structure: text next to images, links beside code, and screenshots alongside notes.

C# and WPF provide a mature foundation for building this application without introducing a browser runtime or an unnecessarily complex architecture.

The initial implementation should concentrate on three things: a frictionless mixed-content editor, reliable portable document storage, and clean export to common formats.

Everything else should follow from those foundations.
