# Archive Provider v1 (read-only)

Enter on a supported archive opens it as a temporary, nested file-panel source.
Use Enter or Backspace to navigate, F3 to view a file and F5 to copy entries
to the other writable panel. Copying preserves subdirectories and empty
directories. Existing copy conflicts, progress and cancellation remain in
the ordinary FileOperationService. No edits, deletes, creates, renames or moves
from an archive are supported.

Supported extensions: ZIP (.zip), RAR/RAR5 (.rar), 7z (.7z), TAR (.tar),
GZip-compressed TAR (.tar.gz, .tgz). Content is inspected on open; merely
renaming a non-archive does not make it openable. Encrypted/password-protected
and multi-volume archives are not supported.

The source indexes metadata and synthesizes absent parent directories.
Actual contents are decompressed to a bounded stream when viewed or copied,
not stored in the index. ZIP and ordinary RAR use archive entry access;
solid RAR and solid 7z use sequential extraction; TAR and TAR.GZ use
sequential readers. Later entries in a solid or streaming archive may take
longer to reach. Opening a compressed TAR scans it once for its index.

Security limits:
- At most 100,000 archive entries (and at most 200,000 total indexed nodes);
- Up to 8 GiB decompressed per file and 32 GiB total declared data;
- 1 GiB temporary storage per opened non-local or nested archive;
- At most three archive levels;
- Entries over 64 MiB with a declared decompression ratio over 10,000:1
  are rejected;
- Actually decompressed bytes are counted against the per-entry limit and
  checked against the declared size.

Non-local archive streams, including non-seekable sources, are copied to a
temporary disk file with a size limit. Local archives are opened in place;
their length and modification timestamp are checked before subsequent reads
and directory enumeration. Remote external changes after spooling are not
tracked. Temporary files are released with the source; active operation
leases prevent premature retirement.

For safety the v1 provider rejects unsafe traversal/absolute names,
ambiguous duplicates, link entries, and encrypted or split entries. On some
Windows-specific filenames it may reject an archive that can be read safely
on Unix. It never interprets archive names as filesystem output paths itself;
destination path resolution remains with FileOperationService.

Library: SharpCompress 0.50.7, MIT. The project intentionally keeps
archive-specific readers separate from the panel source to allow a future
writable ZIP implementation via IFileOperationBatchProvider.

Known validation gap: automated compatibility tests for RAR/RAR5 and
solid RAR/7z require redistributable binary fixtures. Do not consider these
combinations proven without those fixtures and successful platform CI runs.

RAR/RAR5, solid RAR and solid/non-solid 7z integration fixtures are downloaded from
SharpCompress's pinned test suite during GitHub Actions builds, and verified against
Git object hashes. To run these integration tests locally, run
`pwsh ./eng/testing/download-archive-fixtures.ps1` and set
`CSHARPFAR_ARCHIVE_FIXTURES` to the absolute `tests/archive-fixtures` path before
`dotnet test`. Without this variable, the external fixture cases do not execute.
