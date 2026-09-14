# Bulk Rename Utility (BRU)

## Capabilities, Features, Methods, and Practical Usage Guide

**Document purpose:** This guide explains what Bulk Rename Utility is, what it can do, how it is used, and why it is useful. It is intended for people who need to rename, reorganise, standardise, or clean up large collections of files and folders in Windows.

**Official website:** <https://www.bulkrenameutility.co.uk/>

---

## 1. What is Bulk Rename Utility?

Bulk Rename Utility, commonly abbreviated to **BRU**, is a Windows application for renaming multiple files and folders in a single operation.

Instead of manually renaming files one at a time, BRU lets the user define one or more renaming rules. It then displays a preview showing what every selected file or folder will be called before the changes are applied.

BRU can perform simple jobs, such as adding a prefix to a group of filenames, as well as advanced transformations using metadata, regular expressions, JavaScript, imported filename lists, numbering rules, dates, and file properties.

A simple description is:

> **Bulk Rename Utility is a high-speed file and folder renaming tool that applies consistent naming rules to many items at once, with a preview before the changes are committed.**

---

## 2. What is BRU used for?

BRU is useful whenever filenames or folder names need to be cleaned up, standardised, reordered, classified, or made easier to search.

Common uses include:

- Renaming hundreds or thousands of files at once.
- Applying a consistent naming convention.
- Adding project names, dates, locations, asset numbers, or document types.
- Removing unwanted camera, scanner, export, or system-generated text.
- Replacing spaces, underscores, hyphens, or other characters.
- Adding sequential numbers.
- Correcting filename capitalisation.
- Reordering parts of filenames.
- Renaming photos using EXIF metadata such as the date taken.
- Renaming music using MP3 ID3 information.
- Renaming video and media files using Windows file properties.
- Changing or removing file extensions.
- Processing files contained in subfolders.
- Renaming files according to a prepared CSV or text list.
- Preparing files for document management, archiving, migration, upload, or automation.

---

## 3. Why is it useful?

### Speed

A naming change that would take hours to perform manually can often be completed in seconds.

### Consistency

All selected items are processed using the same rules, reducing spelling errors and inconsistent formatting.

### Detailed preview

BRU displays the proposed result before renaming. Users can compare the existing name and the new name and identify unexpected changes before committing them.

### Multiple rules in one operation

A user can remove unwanted text, change case, insert a date, and add numbering as part of the same rename operation.

### Handles large collections

The developer states that BRU can handle folders or disks containing more than 100,000 entries and can rename thousands of files in seconds.

### Repeatable configurations

Frequently used renaming criteria can be saved as favourites, making standard naming processes easier to repeat.

### Advanced flexibility

Regular expressions and JavaScript allow more complicated filename logic than a basic find-and-replace tool.

### Reduced manual risk

Previewing new names, logging rename activity, and creating undo information can make bulk changes more controlled and auditable than manual renaming.

---

## 4. The standard BRU workflow

Although the interface contains many controls, the normal workflow is straightforward.

1. **Open Bulk Rename Utility.**
2. **Browse to the required folder** using the folder tree or path field.
3. **Select the files or folders** to rename.
4. **Enter the required criteria** in one or more renaming sections.
5. **Review the New Name preview** for every selected item.
6. **Check for duplicates, blank names, incorrect extensions, or unintended matches.**
7. **Click Rename** only after the preview is correct.
8. **Review the completion result or log**, if logging is enabled.

> **Important:** Only selected files and folders are processed. Entering renaming criteria does not rename anything by itself.

---

## 5. Core renaming methods

## 5.1 Add a prefix

A prefix is inserted at the beginning of the existing filename.

**Before:**

```text
Report.pdf
Photo001.jpg
```

**Prefix:**

```text
Project-A_
```

**After:**

```text
Project-A_Report.pdf
Project-A_Photo001.jpg
```

Useful for adding project names, office codes, years, categories, or status labels.

---

## 5.2 Add a suffix

A suffix is inserted at the end of the filename, normally before the extension.

**Before:**

```text
AssetRegister.xlsx
```

**Suffix:**

```text
_Approved
```

**After:**

```text
AssetRegister_Approved.xlsx
```

Useful for document status, revision, location, owner, or archive labels.

---

## 5.3 Find and replace text

BRU can search for specific text and replace it with different text.

**Before:**

```text
QLD Office Report.docx
```

**Find:**

```text
Office
```

**Replace with:**

```text
Regional
```

**After:**

```text
QLD Regional Report.docx
```

Leaving the replacement value blank removes the matching text.

---

## 5.4 Remove characters, digits, symbols, or words

Characters can be removed according to their position or type. Examples include:

- Removing the first four characters.
- Removing the final three characters.
- Removing text between two positions.
- Removing words.
- Removing digits.
- Removing symbols.
- Removing repeated spaces.

**Before:**

```text
IMG_2026_0001.jpg
```

**Remove the first four characters:**

```text
2026_0001.jpg
```

---

## 5.5 Insert text at a specific position

Text can be inserted at a defined character position in each filename.

**Before:**

```text
ProjectReport.pdf
```

**Insert a hyphen after character 7:**

```text
Project-Report.pdf
```

This is useful when existing filenames have predictable structures.

---

## 5.6 Change filename case

BRU can standardise capitalisation, including conversions such as:

- UPPER CASE
- lower case
- Title Case
- Sentence case

**Before:**

```text
brisbane OFFICE asset REGISTER.xlsx
```

**After using title case:**

```text
Brisbane Office Asset Register.xlsx
```

---

## 5.7 Add sequential numbering

BRU can add automatic numbers with configurable starting numbers, increments, padding, separators, and positions.

**Before:**

```text
Photo.jpg
Photo.jpg
Photo.jpg
```

**After:**

```text
Photo_001.jpg
Photo_002.jpg
Photo_003.jpg
```

Numbering is useful for ordered photos, scanned pages, drawing sets, exports, archive sets, and media collections.

> Sort the files into the intended order before applying sequential numbering.

---

## 5.8 Add dates and times

Dates can be added in multiple formats using available file timestamps or supported metadata.

**Before:**

```text
MeetingNotes.docx
```

**After:**

```text
2026-09-14_MeetingNotes.docx
```

Possible date sources can include:

- Creation date.
- Modified date.
- Accessed date.
- Photo date taken from EXIF metadata.
- Current date, depending on the chosen method.

---

## 5.9 Rename using the parent folder name

The containing folder's name can be incorporated into each filename.

**Folder:**

```text
Brisbane
```

**Before:**

```text
AssetList.xlsx
```

**After:**

```text
Brisbane_AssetList.xlsx
```

This is useful when combining files from separate office, client, project, year, or category folders.

---

## 5.10 Change or remove file extensions

BRU can modify file extensions in bulk.

**Example:**

```text
FILE01.JPEG
```

can become:

```text
FILE01.jpg
```

> **Warning:** Renaming an extension does not convert the file's underlying format. Changing `document.txt` to `document.pdf` does not turn the text file into a PDF.

---

## 5.11 Rename photos using EXIF metadata

BRU can use metadata embedded in supported image files, including information such as:

- Date picture taken.
- Image resolution.
- Other available EXIF fields.

**Before:**

```text
DSC1790.jpg
```

**Possible result:**

```text
2026-09-14_Brisbane_001.jpg
```

EXIF extraction may need to be enabled in the application. It can be disabled by default because reading metadata from many photographs may slow folder loading.

---

## 5.12 Rename MP3 files using ID3 tags

BRU can use supported MP3 ID3 information such as:

- Artist.
- Album.
- Title.

**Before:**

```text
Track01.mp3
```

**Possible result:**

```text
Artist - Song Title.mp3
```

The official FAQ specifically identifies support for ID3 V1 Artist, Album, and Title tags.

---

## 5.13 Rename using Windows file properties

BRU can use Windows file properties associated with different file types. Examples include:

- Video length.
- Frame width and height.
- Bitrate.
- Publisher.
- Title.
- Media dimensions.

The official site states that more than one hundred attributes are available across different file types.

---

## 5.14 Rename from a text or CSV list

BRU can import old and new filename mappings from a prepared text file.

Example using a pipe separator:

```text
OldName01.pdf|NewName01.pdf
OldName02.pdf|NewName02.pdf
OldName03.pdf|NewName03.pdf
```

A comma may also be supported as the separator:

```text
OldName01.pdf,NewName01.pdf
```

This method is particularly useful when the required names come from:

- Excel.
- SharePoint.
- ServiceNow.
- An asset register.
- A document register.
- A database export.
- A manually reviewed mapping list.

---

## 5.15 Use regular expressions

Regular expressions, usually called **regex**, are patterns used to identify and rearrange structured text.

They are useful when filenames follow a pattern but contain different values.

**Before:**

```text
Invoice 2026 - Client Name.pdf
```

**After:**

```text
Client Name - Invoice 2026.pdf
```

Regex is suitable for:

- Swapping filename sections.
- Capturing variable text.
- Finding dates or reference numbers.
- Removing repeated patterns.
- Reformatting structured identifiers.
- Applying conditional pattern-based changes.

BRU provides an online Regex Assistant that can generate expressions from plain-language descriptions. Generated patterns should still be tested carefully against sample files.

---

## 5.16 Use JavaScript rules

Advanced users can use JavaScript to create custom renaming logic that would be difficult to express through standard controls.

Possible uses include:

- Conditional renaming.
- Custom date handling.
- Complex text parsing.
- Applying different outcomes based on filename content.
- Calculating names from several pieces of information.

The BRU website provides an assistant that can generate JavaScript or regex from natural-language instructions. Always review the preview before applying generated logic.

---

## 5.17 Process folders and subfolders

Directory recursion lets BRU include files and folders below the selected location.

This can be useful for applying a standard naming rule across an entire folder hierarchy. It also increases the number of affected items, so filters and preview checks become especially important.

---

## 5.18 Filter what will be renamed

Filters help narrow down the items displayed or processed. Available filtering methods can include:

- Wildcards.
- Filename length.
- Path length.
- Regular expressions.
- JavaScript conditions.
- Folder depth or subfolder level.
- File or folder selection.

Filtering is useful when only certain file types, naming patterns, folders, or hierarchy levels should be changed.

---

## 5.19 Copy or move while applying naming rules

Recent BRU versions include copy or move location capabilities, including support for relative paths. This can support workflows that rename items while organising them into another folder or a relative subfolder.

Because copying, moving, overwriting, or reorganising files can have broader consequences than renaming alone, this should be tested on a small sample before use on production data.

---

## 5.20 Change timestamps and attributes

BRU can change file and folder timestamps, including:

- Created.
- Modified.
- Accessed.

It can also change attributes such as:

- Hidden.
- Read-only.
- Archive.

These functions affect metadata rather than only changing the visible filename.

---

## 6. Additional features

BRU includes or supports the following features:

- Rename files, folders, or both.
- Detailed new-name preview.
- Column-based sorting.
- Saved renaming favourites.
- Windows Explorer **Bulk Rename Here** integration.
- Directory recursion.
- Unicode filename support.
- UNC network paths.
- Portable, no-install edition.
- 32-bit and 64-bit editions.
- Windows on ARM compatibility.
- Dark Mode.
- Rename activity logging.
- Undo support or undo information.
- Multi-level undo in newer Version 4 releases.
- Optional command-line companion product called Bulk Rename Command.
- Natural-language assistance for generating regex or JavaScript rules.

---

## 7. Example workflows

## Example A: Standardise project documents

**Original files:**

```text
final report.docx
cost plan.xlsx
site photos.zip
```

**Required convention:**

```text
PROJECT123_Final_Report.docx
PROJECT123_Cost_Plan.xlsx
PROJECT123_Site_Photos.zip
```

**Method:**

1. Select the three files.
2. Apply Title Case.
3. Replace spaces with underscores.
4. Add the prefix `PROJECT123_`.
5. Review the preview.
6. Click Rename.

---

## Example B: Rename camera photographs chronologically

**Original files:**

```text
DSC0001.jpg
DSC0002.jpg
DSC0003.jpg
```

**Required convention:**

```text
2026-09-14_Site-Inspection_001.jpg
2026-09-14_Site-Inspection_002.jpg
2026-09-14_Site-Inspection_003.jpg
```

**Method:**

1. Enable EXIF extraction if the photo date is required.
2. Sort by date taken.
3. Add the EXIF date in `YYYY-MM-DD` format.
4. Add `_Site-Inspection_`.
5. Add sequential numbering starting at `001`.
6. Review the complete list before renaming.

---

## Example C: Remove export text from reports

**Original files:**

```text
EXPORT_20260914_Asset_Report_001.csv
EXPORT_20260914_Asset_Report_002.csv
```

**Required result:**

```text
Asset_Report_001.csv
Asset_Report_002.csv
```

**Method:**

1. Use Find and Replace.
2. Find `EXPORT_20260914_`.
3. Leave Replace blank.
4. Confirm the preview.
5. Rename the selected files.

---

## Example D: Rename from an asset register

A spreadsheet contains the approved names for a set of device photographs or reports.

**Mapping file:**

```text
IMG001.jpg|AU123456_HP-ZBook.jpg
IMG002.jpg|AU123457_HP-EliteBook.jpg
IMG003.jpg|AU123458_HP-ZBook.jpg
```

**Method:**

1. Export the mapping as a correctly formatted text or CSV file.
2. Ensure the old names exactly match the existing files.
3. Import the list into BRU.
4. Review unmatched records and duplicate proposed names.
5. Apply the rename operation.

---

## 8. Safe operating procedure

Bulk renaming is powerful, so a controlled process is recommended.

### Before renaming

1. Back up important data or confirm it can be restored.
2. Test the rule against duplicate or disposable sample files.
3. Select only the items that should be processed.
4. Confirm whether folders, files, or both are included.
5. Be careful when enabling subfolder recursion.
6. Confirm that file extensions will remain correct.
7. Sort the list correctly before adding sequence numbers.
8. Check whether any application is actively using the files.

### During preview

Check for:

- Duplicate new names.
- Empty or incomplete names.
- Unexpected character removal.
- Wrong numbering order.
- Modified extensions.
- Incorrect dates.
- Unintended folders or subfolders.
- Items that should have been excluded.

### After renaming

1. Review the completion message.
2. Check a representative sample of files.
3. Confirm that programs or links relying on the old paths still work.
4. Retain the log or undo information until the result has been verified.

> Renaming files changes their paths. Shortcuts, scripts, applications, databases, synchronisation tools, or document-management references that expect the old names may need to be updated.

---

## 9. Limitations and considerations

- BRU is primarily a Windows GUI application.
- Changing a file extension does not convert the underlying file format.
- Renaming can break shortcuts, links, scripts, playlists, application references, or database associations.
- Metadata availability depends on the file type and the metadata stored in that file.
- Complex regex or JavaScript rules can produce broad unintended changes if not tested.
- EXIF extraction can make browsing large photo collections slower.
- Files held open or protected by permissions may not be renameable.
- The GUI application is not intended as the command-line edition. Automated command-line workflows use the separate **Bulk Rename Command** product.
- Business and commercial use requires an appropriate commercial licence.

---

## 10. Licensing and installation

The official site states that BRU is free for personal, private use at home. Use within a business entity, company, or commercial context requires a commercial licence.

Installation and deployment options include:

- Standard Windows installer.
- 32-bit and 64-bit application versions.
- Portable or no-install package.
- Windows on ARM support.
- A separate Bulk Rename Command download for command-line use.

As of the official download page reviewed for this document, the listed release is **Version 4.1.0.1**, released **18 November 2025**. Always check the official website for the current version, licensing terms, and supported operating systems.

---

## 11. Who benefits from BRU?

BRU can be useful for:

- IT support and asset-management teams.
- Records and information-management teams.
- Photographers and media teams.
- Engineers and project teams.
- Document controllers.
- Researchers and archivists.
- Music and video collectors.
- Administrators handling exported reports.
- Anyone managing a large file collection.

---

## 12. Short description for colleagues

> Bulk Rename Utility is a Windows application used to rename large numbers of files and folders according to consistent rules. It can add or remove text, replace characters, change capitalisation, add dates and sequential numbers, use photo or media metadata, process subfolders, and apply advanced regex or JavaScript logic. Its detailed preview lets users check the proposed names before committing the changes, making it useful for document control, asset management, photography, archiving, data cleanup, and general file organisation.

---

## 13. Very short description

> **BRU is a Windows bulk-renaming tool that quickly applies consistent naming rules to many files and folders, with a preview before changes are made.**

---

## 14. Quick-reference checklist

```text
[ ] Browse to the correct folder
[ ] Select the correct files or folders
[ ] Apply the required renaming rules
[ ] Sort correctly before numbering
[ ] Confirm whether subfolders are included
[ ] Check the New Name preview
[ ] Look for duplicates and extension changes
[ ] Back up important files
[ ] Click Rename only when the preview is correct
[ ] Verify the results and retain the log/undo information
```

---

## 15. Official resources

- **BRU home and features:** <https://www.bulkrenameutility.co.uk/>
- **Download page:** <https://www.bulkrenameutility.co.uk/Download.php>
- **Frequently Asked Questions:** <https://www.bulkrenameutility.co.uk/Faq.php>
- **Support forum:** <https://www.bulkrenameutility.co.uk/forum/>
- **JavaScript and Regex Assistant:** <https://www.bulkrenameutility.co.uk/js/>

---

## Sources and accuracy note

This guide was prepared from the official Bulk Rename Utility website, feature information, FAQ, and download information accessed on **14 September 2026**. Product functionality, licensing, compatibility, and version information can change, so the official website should be treated as the definitive source.

### Primary sources

1. TGRMN Software, **Bulk Rename Utility homepage and feature list**: <https://www.bulkrenameutility.co.uk/>
2. TGRMN Software, **Bulk Rename Utility FAQ**: <https://www.bulkrenameutility.co.uk/Faq.php>
3. TGRMN Software, **Bulk Rename Utility download page**: <https://www.bulkrenameutility.co.uk/Download.php>
4. TGRMN Software, **Bulk Rename Utility release history**: <https://www.bulkrenameutility.co.uk/Downloads/BRUChangelog.pdf>

---

*Prepared as a general explanatory and operational reference. Test bulk changes on sample data before applying them to important or production files.*
