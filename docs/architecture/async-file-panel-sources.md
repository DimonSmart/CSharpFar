# Asynchronous file panel sources

## Design

The application schedules navigation and refresh through `PanelLoadingCoordinator`.
The coordinator owns one slot per logical `FilePanelState`, not per screen side.
Directory enumeration and view preparation (filtering, mount and volume metadata,
sorting and summaries) run outside the UI thread. A request holds a snapshot of its
destination, sort options and selection. Its completion is posted to the existing
UI action queue. Only the current request, with an unchanged panel revision and the
same registered source instance, can commit a view.

During navigation the previously committed path, items and provider identity remain
consistent. Cancellation invalidates the request without replacing that view.
The title displays a delayed `Loading` indicator (120 ms) for slow requests.
Escape cancels the active panel's request independently of the other panel.
There are at most two running requests and one latest pending request per logical
panel; a refresh during refresh requests at most one follow-up. Navigation supersedes
refresh. The second active slot permits newer navigation to finish before an
uncooperative old request returns; this is not unbounded speculative execution.

## Provider contracts and compatibility

Implement the optional `IAsyncFilePanelSource` extension for new providers.
It adds `EnumerateDirectoryAsync` and `GetItemAsync`; the existing stream and
mutation methods remain part of `IFilePanelSource`. All potentially blocking
operations must return promptly to their caller. Use genuinely asynchronous library
operations where available, or bounded background execution for synchronous-only APIs.
Path manipulation and capability queries are synchronous and must not perform I/O.

Existing SDK binaries retain the original `IFilePanelSource` interface.
`FilePanelSourceAsync.AsAsync()` returns the original async source or a cached
legacy adapter. The adapter limits concurrent fallback operations to two per source,
allows cancellation before work begins, and does not falsely abandon an operation
once it starts. Optional interfaces, identity and lifetime leases must be queried
on the original source, not on the adapter.

## Consumers and lifetime

Navigation, refresh and source opening use the shared loader. File-operation
planning and traversal, metadata and stream opening use the async contract.
The Viewer loads remote seekable streams asynchronously; non-seekable streams
are spooled before viewing. Editor open and save use background I/O while the
document and UI state remain owned by the UI. Quick View prepares a bounded
preview outside rendering, retains only the latest requested result, and never
reads the source during drawing. Directory scans use the async provider contract.

Acquire a source usage lease before asynchronous work; release it only when
the actual operation and resource cleanup finish. An obsolete request can be
ignored immediately by the UI while its worker continues to own the lease.
An owned source scheduled for retirement remains registered while leases exist.
Cancellation is cooperative: it cannot forcibly interrupt an arbitrary in-process
third-party library. A successful publication must not be reported as a failed
or cancelled write merely because a cancellation arrived after the commit point.
The existing safe publication, conflict and batch protocols still apply.

## Verification

Run the repository's existing cross-platform CI (Windows, Linux, macOS ARM and
Intel), including the independent SDK binary checks and real FTP/SFTP publication
checks. Controlled providers use task completion gates to verify out-of-order
completion, cancellation, refresh coalescing, source retirement and request limits.
See `PanelLoadingCoordinatorTests`, `FilePanelSourceAsyncTests`, the Viewer async
I/O tests and the Editor regressions. The bounds above are concurrency limits,
not provider throughput or latency guarantees.
