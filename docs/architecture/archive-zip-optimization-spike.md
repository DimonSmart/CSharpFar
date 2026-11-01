# Archive Provider v2: ZIP update / append optimization assessment

Status: **no optimization adopted**. The transactional full-rebuild writer is
the production path for ZIP; this report records implementation decisions and
a reproducible benchmark proposal. It does not claim that a 5–10 GiB
performance benchmark has already run.

## Options considered

| Technique | Memory and I/O risk | Failure/compatibility considerations | Decision |
| --- | --- | --- | --- |
| Full safe rebuild (baseline) | Sequential source read and adjacent destination write; small copying buffers, disk space near archive size | Original is unchanged until verified atomic replace. Re-compresses unchanged entries and can alter compressed bytes; conservative metadata guard | Implemented |
| `System.IO.Compression.ZipArchiveMode.Update` on original | Update mode can retain entry data and allocate memory based on entries; not guaranteed constant RAM | This is an in-place update, not a transaction; exceptions or interruption can corrupt central directory | Rejected on original |
| `ZipArchiveMode.Update` on a complete temporary copy | Initial copy cost near archive size plus update cost; potentially high RAM | Retains source until replace, but does not establish reliable memory bounds or preserve all unusual metadata | Not adopted |
| Append-only local headers and new central directory | Less old-data I/O for small changes, but stale entries accumulate, storage grows and ZIP64 rules are complex | A power failure between headers and new central directory can leave inaccessible new entries; recovery must identify a valid previous directory, and independent compatibility tests are required | Research only |
| Raw compressed-entry copy | Could reduce CPU and decompression during a rebuild | SharpCompress 0.50.7 public writer does not offer a documented stable raw entry-copy contract with preservation of central-directory metadata; custom ZIP header manipulation introduces unacceptable maintenance risk | Not implemented |

The baseline has **O(1) bounded application buffers** relative to file
size during streaming, plus finite metadata proportional to entry count and
temporary files proportional to newly added data. It performs O(total
archive contents) I/O; cancellation cannot avoid that cost. Full rebuild
preserves logical data and only the metadata declared safe by the
format-specific eligibility validator.

## Proposed reproducible measurement (opt-in)

1. Generate a ZIP with 5 GiB and 10 GiB of compressed/realistic data using
   separately controlled entropy profiles, entry sizes, Unicode names,
   and ZIP64 cases.
2. Add and later replace a 1 MiB entry via the current batch/rebuild API,
   and compare a disposable copy updated with ZipArchiveMode.Update.
3. Record wall time (prepare/write/verify/publish), process peak RSS,
   temporary disk high-water mark, bytes read/written, and resulting ZIP
   size. Repeat each scenario at least three times on each filesystem.
4. Reopen independently with 7z and unzip; verify hashes of all entries,
   metadata and UTF-8 filenames. Kill the process at defined pre-publish
   points and inspect source byte-for-byte. Repeat update and append
   operations to measure accumulation and fragmentation.
5. Accept an optimization only after equivalent crash safety, metadata
   preservation and measurable improvement across relevant workloads.

**Measurements:** Not available for 5–10 GiB in this change. It would be
misleading to invent memory, latency or I/O figures. The implementation
therefore retains full safe rebuild rather than introducing an untested
append/update path.

Relevant references:
- [ZipArchiveMode.Update](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchivemode)
- [SharpCompress 0.50.7](https://www.nuget.org/packages/SharpCompress/0.50.7)
- [Archive Provider v2 ADR](archive-provider-v2.md)
