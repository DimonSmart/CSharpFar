# Archive Provider v2: safe mutation (ADR)

Status: Implemented baseline, with explicit format restrictions. SharpCompress: **0.50.7**.
Scope: existing local archive files. Not a general-purpose transactional filesystem.

## Design decision

Changes to an archive are expressed through the existing
`IFilePanelSource` / `IFileOperationBatchProvider` contracts and coordinated
by `FileOperationService`, rather than new format-specific F5/F6/F7/F8 commands.

A batch keeps a logical, copy-on-write entry tree. New/replaced file bodies
are spooled individually to temporary files. A single user operation
rewrites each changed destination archive at most once. Deletes/renames do
not touch the physical archive until commit.

Writers always build a complete temporary archive adjacent to the original.
They reopen it, validate entry names/types/sizes and verify hashes of newly
staged entry bodies. Source SHA-256 is checked before and after the rebuild.
A cooperative cross-process sidecar lock (FileShare.None) serializes
CSharpFar writers for a given canonical path. Publication uses
`File.Replace(temporary, original, backup)` without a delete/move gap.
No in-place update is performed.

`ArchiveFilePanelSource` updates its own index after publication and
invalidates other sessions of the same path using a process-local revision
counter. Stale sources refuse operations until reopened.

### Point of no return

Successful `File.Replace` is the commit point. Failure to remove a backup,
update a panel or perform post-commit housekeeping must not be interpreted
as rollback. Cancellation is honored before replacement; cancellation
after replacement cannot restore the old version.

### Guarantees and limitations

- Aborted and failed pre-publication batches leave the original archive
  unchanged. After a successful destination commit, a cross-provider move
  may still fail to remove its source; `PartiallyMoved` reports this.
- A move between different archives is **not distributed-atomic**.
  Successfully copied files are deleted individually; skipped files and
  non-empty parent directories remain.
- Cooperative lock files protect instances writing through this
  implementation, not arbitrary external writers. SHA-256 and file-stat
  checks reduce (but cannot eliminate) the TOCTOU window against a
  non-cooperating process.
- Direct `OpenWriteAsync` on the archive source is intentionally unsupported:
  callers must use an explicit mutation batch to express cancellation.
  F4 and ordinary F5 use batch writes.
- Concurrent open archive-entry streams may prevent `File.Replace`,
  especially on Windows. The operation fails without modifying the original.
- Symbolic links are excluded from writable mode. The path-based physical
  identity does not yet reconcile hardlinks, case-sensitive path aliases
  or all cross-platform filesystem file IDs.
- The source file itself is not opened writable before publication. Temporary
  files require room on the same filesystem; ENOSPC leaves the source intact.
  Recovery of orphaned `.csharpfar-*.tmp` and `.backup` requires manual
  inspection. Do **not** automatically delete an unverified last good copy.
- Atomic rename and flush durability depend on filesystem/OS. There is no
  guarantee against hardware corruption or an uncooperative outside writer.
- Existing v1 maximum counts, single-file and total-size checks continue
  to apply; they also apply to batch staging.

## Actual writable format policy

| Format | Writable variant | Read-only variants / limitations |
| --- | --- | --- |
| ZIP | Strict classic ZIP, stored or Deflate entries, no extras, comments, encryption or ZIP64, standard central directory | ZIP64, unusual methods, additional metadata, signatures, encryption, multi-disk |
| TAR | Regular files and directories, basic supported POSIX metadata | Links, devices, unknown PAX extended metadata, unusual entries |
| TAR.GZ / TGZ | Above TAR subset with standard GZip header | Custom GZip headers, additional streams, unsupported TAR metadata |
| 7z | Non-solid LZMA2 without rich file attributes, where validation succeeds | Solid archives, other codecs, encryption, extended attributes; failure to prove compatibility |
| RAR / RAR5 | Never writable | Entire format; extraction remains supported |
| Nested or remote archives | Never writable | Spool/read-only session from v1 |

This table is *not* a guarantee that all files with a matching extension can
be rewritten. The provider checks each archive before advertising write
capabilities. For compatibility, unknown critical metadata results in a
read-only session, not lossy conversion. ZIP writer re-compresses unchanged
entries and therefore does not preserve compressed byte identities, even
when logical contents and supported metadata remain the same.

## Operational workflow

- F5: one destination batch if available; copying a directory recursively
  does not commit each child.
- F6: one batch for in-container move; for distinct providers, copy and
  commit first, then delete only successfully copied source elements.
- F7: direct create wraps an explicit single-command batch.
- F8/Delete: selected items are grouped by provider, nested duplicate
  selections removed, then committed once.
- F4/new file: `EditorFileService.SaveAsync` stages the write and commits.
  The modal editor polls task completion on the surface wake callback,
  leaving the document dirty on an error. A stale archive revision rejects
  save rather than silently overwriting changes.
- Unsupported formats or metadata remain read-only; the user must export
  files for editing or choose an independently supported archive.

## Maintenance

Keep the write-eligibility checks conservative. Test every newly supported
writer option and significant metadata field with independent tools.
CI validates Windows, Ubuntu, macOS Apple Silicon and Intel. 7z correctness
requires the test fixture generated by SharpCompress and external 7z
verification where the tool is available.

See `archive-zip-optimization-spike.md` for optional optimizations considered.
