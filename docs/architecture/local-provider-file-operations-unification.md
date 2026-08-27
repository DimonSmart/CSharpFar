# Local Copy / Provider Copy unification research

Baseline: `master` at `340d9fafafac1841cbfed5f2a9328d68c6ff0ae4` (`Fix explicit target paths in copy operations`).

Scope: architecture research only. This document does not propose a big-bang rewrite and does not require changing user-visible file-operation behavior as part of the research commit.

## 1. Current Architecture

### 1.1 Entry points

`CopyCommand` and `MoveCommand` gather both legacy path strings and provider-qualified locations:

```text
CopyCommand / MoveCommand
    |
    +-- Sources: IReadOnlyList<string>
    +-- SourceLocations: IReadOnlyList<PanelLocation>
    +-- Destination: string
    +-- DestinationLocation: PanelLocation
    |
    v
ApplicationCommandContext.ExecuteFileOperation
    |
    v
FileOperationService.ExecuteAsync
```

`FileOperationRequest` therefore currently has two representations of the same filesystem identity:

```csharp
IReadOnlyList<string> Sources
IReadOnlyList<PanelLocation>? SourceLocations
string? Destination
PanelLocation? DestinationLocation
```

This is the first architectural split: local paths are still first-class request data even though `PanelLocation` already exists and is the correct provider-qualified identity model.

### 1.2 Execution-path selection

`FileOperationService.ExecuteAsync` calls `UsesProviderLocations(request)` before choosing the execution path.

```text
FileOperationService.ExecuteAsync
    |
    +-- UsesProviderLocations == false
    |       |
    |       +-- Copy -> CopyAsync
    |       +-- Move -> MoveAsync
    |       +-- Delete -> Delete
    |       +-- CreateDirectory -> CreateDirectory
    |
    +-- UsesProviderLocations == true
            |
            v
        ExecuteProviderOperationAsync
            |
            +-- Copy -> CopyProviderAsync
            +-- Move -> MoveProviderAsync
            +-- Delete -> DeleteProviderAsync
            +-- CreateDirectory -> CreateProviderDirectoryAsync
```

`BuildPlan` repeats the same split:

```text
BuildPlan
    |
    +-- local -> BuildLocalCopyPlan / BuildLocalMoveOperationPlan
    |
    +-- provider -> BuildProviderPlan
                        |
                        +-- BuildProviderCopyPlan
                        +-- BuildProviderMovePlan
```

The split is therefore present in both preview/planning and execution.

### 1.3 Local Copy

Current local copy path:

```text
CopyAsync
    |
    v
BuildCopyPlan
    |
    +-- DestinationPattern / DestinationTemplate
    +-- local destination interpretation
    +-- recursive Directory/File traversal
    +-- mask filtering
    +-- duplicate target validation for wildcard/template cases
    +-- self-copy / destination-inside-source validation
    +-- source size + LastWriteTime snapshot
    |
    v
Create directory targets
    |
    v
ResolveDestinationPathAsync
    |
    +-- sticky OverwriteAll / SkipAll / RenameAll
    +-- OnlyNewer
    +-- Reliable resume analysis
    |
    v
CopyFileContentsAsync
    |
    +-- Normal
    +-- Reliable retry/resume
    +-- FastSalvage behavior
    |
    v
IFileSystemPlatformOperations
    |
    +-- metadata
    +-- ACL / Unix mode
    +-- symlink recreation
```

Local planning is largely eager: recursive directories and files are expanded into `CopyPlan` before content execution.

### 1.4 Provider Copy

Current provider copy path:

```text
CopyProviderAsync
    |
    +-- ValidateProviderCopyOptions
    +-- reject Provider A -> Provider B
    |
    v
BuildProviderCopyPlan
    |
    +-- DestinationPattern / DestinationTemplate
    +-- provider destination interpretation
    +-- mask filtering
    +-- same-provider self/descendant validation
    +-- wildcard/template collision validation
    |
    v
CopyProviderItemAsync
    |
    +-- recursive directory traversal at execution time
    +-- destination GetItem
    +-- provider-specific conflict switch
    +-- OpenReadAsync / OpenWriteAsync
    +-- ordinary stream copy
```

For an unmasked directory copy, provider recursion is not fully materialized into the plan. `CopyProviderItemAsync` recursively enumerates children while execution is already mutating the destination tree.

This differs from the local scan/plan model and also means provider total-byte progress for an unmasked directory is not derived from all recursive files before execution.

### 1.5 Local Move

Current local move path:

```text
MoveAsync
    |
    v
BuildLocalMovePlan
    |
    +-- destination mapping
    +-- wildcard/template handling
    +-- mask filtering
    +-- collision/self/descendant validation
    |
    v
TryMoveDirect
    |
    +-- File.Move / Directory.Move
    +-- conflict handling
    |
    +-- success -> done
    |
    +-- IOException / forced fallback
            |
            v
        CopyAsync
            +
        delete successfully copied source
```

Local Move therefore already has the desired conceptual form of `direct move when possible, otherwise copy + delete`, but it is implemented entirely in the local branch.

### 1.6 Provider Move

Current provider move path:

```text
MoveProviderAsync
    |
    +-- require every source SourceId == destination SourceId
    |
    v
BuildProviderMovePlan
    |
    v
provider conflict handling
    |
    v
IFilePanelSource.RenameAsync
```

Cross-provider move is rejected in `MoveCommand` and again in `MoveProviderAsync`. There is no copy-delete fallback for provider moves.

### 1.7 Existing provider abstraction

`IFilePanelSource` already contains the basic primitives needed for ordinary copy:

```csharp
NormalizePath
IsRootPath
GetParentPath
EnumerateDirectory
GetItem
OpenReadAsync
OpenWriteAsync
CreateDirectoryAsync
DeleteAsync
RenameAsync
```

`LocalFilePanelSource`, `DemoFilePanelSource`, SFTP and FTP all implement this same basic contract.

This means Normal Copy does not fundamentally require a separate Local engine.

The main gaps are not basic read/write primitives. They are path semantics, advanced operation capabilities, and the fact that orchestration currently duplicates provider-neutral business behavior.

## 2. Local vs Provider Behavior Matrix

| Behavior | Local | Provider | Shared today? | Difference justified? |
| --- | --- | --- | --- | --- |
| Request identity | strings, optional locations | locations | partly | no |
| Destination interpretation | `BuildCopyPlan` | `ResolveProviderCopyTargetPath` | no | no |
| Wildcard transformation | yes | yes | parser partly shared | no |
| Template evaluation | yes | yes | parser shared, mapping duplicated | no |
| Source mask filtering | recursive local traversal | recursive provider traversal | matcher shared | no |
| Recursive copy planning | eager plan | runtime recursion for normal directory copy | no | no |
| Duplicate final-target validation | wildcard/template | wildcard/template | separate | no |
| Self-copy validation | local path helpers | `ProviderPathRelations` | no | no |
| Destination-inside-source | local path helpers | `ProviderPathRelations` | no | no |
| File conflict handling | full | separate switch | no | no |
| `OverwriteAll` / `SkipAll` / `RenameAll` stickiness | yes | no operation-wide sticky state | no | no |
| `OnlyNewer` | yes | explicitly rejected | no | probably not inherently local; requires trustworthy timestamps |
| File/directory conflict safety | yes | move explicitly; copy behavior differs by item type | no | semantics should be shared |
| Scan progress | explicit scan phase | incomplete/different | no | no |
| Recursive byte totals | yes | unmasked directory total is not fully planned | no | no |
| Cancellation/pause | shared state, local loops | shared state, provider loops | partly | no |
| Normal stream copy | local `FileStream` path | `OpenReadAsync` / `OpenWriteAsync` | no | no |
| Reliable resume | yes | rejected | no | capability-dependent, not `Local`-dependent in principle |
| Fast salvage | yes | rejected | no | mostly strategy-dependent; provider opt-in may be prudent |
| Preserve timestamps | yes | option accepted but provider executor does not apply it | no | capability-dependent |
| Preserve attributes | yes | option accepted but provider executor does not apply it | no | capability-dependent |
| ACL / Unix mode | local platform service | not implemented | no | capability-dependent |
| Symlink recreation | local platform service | not implemented | no | capability-dependent |
| Direct move | `File.Move` / `Directory.Move` | `RenameAsync` | no | primitive differs, orchestration need not |
| Move fallback to copy-delete | yes | no | no | capability/topology-dependent |
| Provider A -> Provider B copy | n/a | explicitly blocked | no | current implementation limit, not a fundamental stream-copy limit |
| Cross-provider move | local cross-volume fallback exists inside local FS | explicitly blocked | no | current implementation limit; should be copy-delete when capabilities allow |

Two concrete divergences are already visible in production code:

1. Local conflict handling remembers `OverwriteAll`, `SkipAll`, and `RenameAll` in `OperationState.StickyConflictDecision`; provider conflict handling handles the `*All` enum value for one item but does not store it for subsequent conflicts.
2. Local metadata/symlink options are implemented, while provider copy accepts most of the same `FileOperationOptions` but does not apply them. Only `Reliable`, `FastSalvage`, and `OnlyNewer` are explicitly rejected.

The second point should eventually become explicit capability negotiation rather than silent option degradation.

## 3. Duplicated Responsibilities

The important duplication is behavioral, not just identical source text.

### 3.1 Destination semantics

The commit immediately preceding this research, `340d9faf...` (`Fix explicit target paths in copy operations`), had to fix the same user-visible rule independently in:

- local `BuildCopyPlan`;
- provider `BuildProviderCopyPlan` / `ResolveProviderCopyTargetPath`;
- separate local and provider regression tests.

That is the clearest recent example of semantic drift caused by two planners.

### 3.2 Path handling

Provider-aware code still contains explicit Local branches:

- `GetProviderRelativePath`;
- `CombineProviderRelativePath`;
- `ResolveProviderCopyTargetPath`;
- `CombineProviderPath`;
- `GenerateProviderName`;
- `DestinationPattern.Parse`.

So the current provider path is not actually provider-neutral. Routing Local through it today would replace the top-level `if Local` with many smaller `SourceId == Local` decisions.

### 3.3 Recursion and filtering

Local copy recursively builds `CopyFilePlanItem` and `CopyDirectoryPlanItem` entries.

Provider copy has two different recursion shapes:

- masked directories are recursively collected during planning;
- unmasked directories are recursively traversed during execution.

This duplicates traversal behavior even inside the provider branch and makes scan/progress/preflight behavior inconsistent.

### 3.4 Conflict resolution

Local and provider implementations separately construct conflicts, generate renamed targets, enforce type compatibility, delete overwrite targets, and interpret conflict decisions.

The duplicated implementations have already diverged on sticky `*All` decisions and `OnlyNewer`.

### 3.5 Move planning and execution

Local and provider move separately implement destination interpretation, conflict handling and target validation.

The primitive difference is legitimate (`File.Move` versus provider rename), but that does not justify separate planning and conflict semantics.

## 4. Local-only Features

### 4.1 Reliable

Reliable copy is currently path-based and tightly coupled to local `FileStream` mechanics:

- source/destination length;
- source snapshot (`Length`, `LastWriteTimeUtc`);
- seek source;
- open destination read/write;
- truncate destination;
- reread destination state after failures;
- tail validation through `CopyResumeAnalyzer`;
- reopen/retry loop.

These are real capabilities, but they do not require a separate orchestration engine.

Correct architectural rule:

```text
if source and destination expose resumable/random-access capabilities
    Reliable is available
else
    Reliable is unavailable
```

Not:

```text
if Local
    Reliable
```

The existing SFTP/FTP implementations must not be assumed resumable merely because a returned `Stream` happens to report `CanSeek`. Reliable depends on reopen, length, truncation, stable source snapshots and well-defined failure semantics. It should be explicit provider capability negotiation.

### 4.2 Fast salvage

Fast salvage is primarily a copy-engine strategy:

- isolate a source read failure to the current file;
- clean up or rename a partial destination;
- record an item error;
- continue later files;
- do not silently continue after unsafe destination-write failure.

Most of that is provider-independent orchestration. Base `IFilePanelSource` already provides delete and rename primitives required for partial cleanup.

However, initial provider support should remain opt-in until error/close semantics are tested. The architecture should permit Fast salvage without treating it as intrinsically local.

### 4.3 Metadata

Current local metadata preservation is delegated to `IFileSystemPlatformOperations` and includes:

- creation/last-write/last-access times;
- file attributes;
- Unix file mode;
- Windows ACL.

`FilePanelItem` is not sufficient as a complete metadata snapshot because it does not contain creation time, last-access time, Unix mode or ACL data.

Metadata belongs behind optional typed operation capabilities, not in the core copy planner.

### 4.4 Symbolic links

Current local code detects links through platform operations and can recreate a link instead of copying target contents.

The base provider contract does not expose link identity or link-target creation. This needs an optional link capability.

The common planner/executor should own the policy (`CopyLink` versus `CopyTargetContents`); the provider capability should only expose link inspection/recreation mechanics.

### 4.5 Platform-specific filesystem behavior

Recycle bin, Windows ACL, Unix mode and platform-specific link handling should remain outside generic orchestration.

The existing `IFileSystemPlatformOperations` is useful local implementation machinery, but in the unified architecture it should sit behind Local provider capability services rather than be called directly by the generic executor based on a Local branch.

## 5. Missing Provider Capabilities

### 5.1 Mandatory path semantics

This is the most important missing abstraction for unification.

`IFilePanelSource` currently has `NormalizePath`, `GetParentPath`, `IsRootPath` and `PathSeparators`, but generic planning still needs Local-specific branches for:

- combine;
- relative path;
- filename extraction;
- extension splitting used by rename generation;
- trailing-directory-separator semantics;
- rooted/relative destination interpretation;
- path equality comparer / case sensitivity.

A mandatory path-semantics object is preferable to continuing to add static `if (SourceId == Local)` helpers.

Example:

```csharp
public interface IFilePathSemantics
{
    StringComparer Comparer { get; }

    string Normalize(string path);
    bool IsRoot(string path);
    bool IsRooted(string path);
    bool EndsInDirectorySeparator(string path);

    string? GetParent(string path);
    string GetName(string path);
    string GetNameWithoutExtension(string path);
    string GetExtension(string path);

    string Combine(string parent, string child);
    string GetRelativePath(string root, string child);
}
```

Exact names are not important. The important point is that Local, SFTP, FTP, Demo, ZIP and future cloud providers own their path grammar.

This also fixes an existing architectural ambiguity around case sensitivity. The generic layer must not hard-code `Ordinal` for every provider or `OrdinalIgnoreCase` for every local platform.

### 5.2 Typed feature services

Do not expand `IFilePanelSource` with every optional operation.

Recommended shape:

```csharp
public interface IFilePanelSource
{
    PanelSourceId SourceId { get; }
    PanelProviderCapabilities Capabilities { get; }
    IFilePathSemantics Paths { get; }
    IFileOperationFeatures Features { get; }

    // Basic provider primitives only.
    ...
}

public interface IFileOperationFeatures
{
    bool TryGet<TFeature>(out TFeature? feature)
        where TFeature : class;
}
```

Possible typed features:

```csharp
IResumableFileOperations
IFileMetadataOperations
ISymbolicLinkOperations
IAccessControlOperations
IDirectMoveOperations
```

This is a capability/service model, not a bag of booleans. A boolean such as `SupportsResume` is insufficient because the executor then still needs somewhere to obtain the actual operations.

### 5.3 Keep `PanelProviderCapabilities` at the UI/command level

The existing enum is useful for coarse user-facing command availability:

```text
CopyFrom / CopyTo / MoveFrom / MoveTo / Delete / Rename / Edit ...
```

It should not become the low-level mechanics capability model.

For example, `CopyTo` says that a panel may be a copy destination; it does not answer whether it supports random access, metadata writeback or symbolic links.

### 5.4 Async discovery is a secondary limitation

`EnumerateDirectory` and `GetItem` are synchronous today, including SFTP/FTP adapters that perform network operations.

This is not a correctness blocker for unification, but a fully eager remote preflight may become slow for large trees. An eventual async enumeration API may be desirable.

Do not make that a prerequisite for the first unification phases unless profiling shows it is necessary.

## 6. Architecture Options

### Option 1 - keep two implementations and extract helpers

```text
Local pipeline          Provider pipeline
      \                    /
       shared small helpers
```

Advantages:

- smallest change;
- lowest immediate regression risk;
- local advanced features remain untouched.

Disadvantages:

- does not solve the core semantic drift;
- future destination/conflict/template/mask changes still need two implementations;
- recent explicit-target bug remains the expected failure mode of the architecture;
- parity tests can detect drift but cannot prevent duplicated implementation effort.

Effort: low.

Regression risk: low per change, high cumulative maintenance risk.

Extensibility: poor.

Recommendation: reject as target architecture.

### Option 2 - common planning, separate execution engines

```text
               Common planner
                    |
                 CopyPlan
                 /      \
        Local executor  Provider executor
```

Advantages:

- directly prevents the class of bug fixed by `340d9faf...`;
- unifies destination interpretation, wildcard/template mapping, masks and preflight;
- can be reached incrementally with relatively low risk.

Disadvantages:

- conflict handling, progress, cancellation and recursive execution can still drift;
- `Reliable`/Fast salvage remain structurally tied to the Local executor;
- Move still wants the same second unification later.

Effort: medium.

Regression risk: medium-low with parity tests.

Extensibility: better, but incomplete.

Recommendation: use as an intermediate migration state, not as the final architecture.

### Option 3 - one orchestration/execution pipeline plus typed provider capabilities

```text
             FileOperationService
                    |
                 Planner
                    |
                 CopyPlan
                    |
                 Executor
                    |
          +---------+---------+
          |                   |
       source              destination
       provider             provider
          |                   |
       basic I/O + optional typed features
```

Advantages:

- one implementation of destination semantics, recursion, conflicts, progress and cancellation;
- Local is not special in orchestration;
- advanced features remain available through explicit capabilities;
- future providers can opt into features independently;
- Provider A -> Provider B becomes architecturally possible without a new engine;
- one contract test suite can enforce user-semantic parity.

Disadvantages:

- requires a careful path-semantics abstraction;
- requires adapters for current local Reliable/metadata/link mechanics;
- feature negotiation must be designed to avoid a new forest of checks;
- remote eager planning can add latency.

Effort: medium-high.

Regression risk: medium if migrated incrementally; high if attempted as one rewrite.

Extensibility: high.

Recommendation: **preferred architecture**.

### Option 4 - fully provider-based filesystem engine

Interpretation: no Local-specific orchestration branch exists anywhere. Local differs only by provider path semantics and available feature services.

Advantages:

- cleanest conceptual model;
- strongest guarantee against Local/provider semantic drift;
- best long-term base for SFTP, FTP, ZIP, cloud and future providers.

Disadvantages:

- dangerous if interpreted as "put every filesystem operation into a giant `IFilePanelSource`";
- high migration risk if attempted directly;
- can over-generalize platform-specific features that are correctly local-only.

Effort: high as a direct rewrite.

Regression risk: high as a direct rewrite.

Extensibility: highest if implemented with capabilities; poor if implemented as a bloated interface.

Recommendation: adopt its **invariant** (no Local-specific orchestration), but reach it through Option 3 and small migration phases.

## 7. Recommended Architecture

### Decision

**Unify completely the Copy/Move orchestration and execution path.**

This means one canonical planner, one conflict mechanism, one progress/cancellation model and one executor. It does **not** mean every filesystem primitive becomes identical.

The chosen architecture is **Option 3**, with the end-state invariant of Option 4:

> Generic file-operation orchestration must never branch on `LocalFilePanelSource` or `PanelSourceId.Local`. Differences are expressed by mandatory path semantics and optional typed operation capabilities.

Local remains special only because it exposes more capabilities.

### Why not stop at planning only?

Common planning would solve the most recent explicit-target bug, but current code already shows execution-level semantic divergence:

- sticky conflict decisions differ;
- provider recursive progress/totals differ;
- metadata/symlink options are applied only locally;
- provider move has no copy-delete fallback;
- provider directory recursion is structurally different.

Therefore planning-only unification is a valuable phase but not a sufficient endpoint.

### Why not make `IFilePanelSource` huge?

Because features such as ACL, symlinks, resumable random access and direct/atomic move are independent and provider-specific.

A new provider should be able to implement ordinary Copy with the small base contract and add advanced features only when supported.

## 8. Proposed Unified Execution Flow

### 8.1 Copy

```text
Command/UI
    |
    v
Canonical FileOperationRequest
(PanelLocation sources + provider-qualified destination spec)
    |
    v
Resolve source/destination providers
    |
    v
Source discovery / recursive scan
    |
    v
Destination interpretation
    |
    v
source -> final target mapping
    |
    +-- wildcard transformation
    +-- template evaluation
    +-- mask filtering
    |
    v
Preflight validation
    |
    +-- invalid paths
    +-- duplicate final targets
    +-- same item
    +-- destination inside source
    +-- required basic capabilities
    |
    v
Immutable CopyPlan
    |
    v
Common executor
    |
    +-- directory creation
    +-- common conflict resolution + sticky state
    +-- copy-mode strategy selection
    +-- common progress / pause / cancellation
    |
    v
Provider primitives / typed features
    |
    +-- Normal stream copy
    +-- Reliable feature path when available
    +-- metadata feature when available
    +-- link feature when available
```

Important properties:

1. The plan contains provider-qualified source and destination locations.
2. Recursive source discovery uses `source.Paths` and `source.EnumerateDirectory`, never `System.IO` directly.
3. Destination interpretation uses `destination.Paths`, never `Path.*` from generic planning.
4. Conflict policy is resolved once in the common executor.
5. Feature-specific mechanics are invoked only after the common policy decision.

### 8.2 Move

```text
MovePlan
    |
    v
for each planned item
    |
    +-- same provider + direct move capability supports this topology
    |       |
    |       v
    |    direct move
    |
    +-- otherwise
            |
            v
        common Copy executor
            |
        copy completed safely?
            |
            v
        source.DeleteAsync
```

Do not define "any `IOException` from Rename means copy-delete" as the generic rule. Providers need a way to distinguish "direct move not supported for this topology" from a real permission, integrity or transport failure.

An `IDirectMoveOperations` capability can return a structured result such as `Moved`, `NotSupported`, or throw for an actual failure.

## 9. Proposed Interfaces / Models

Names are illustrative; responsibilities are the important part.

### 9.1 Canonical request

Target shape:

```csharp
public sealed record FileOperationRequest
{
    public required FileOperationKind Kind { get; init; }
    public required IReadOnlyList<PanelLocation> Sources { get; init; }
    public FileOperationDestination? Destination { get; init; }
    public required FileOperationOptions Options { get; init; }
    public IFileOperationPauseController? PauseController { get; init; }
}

public sealed record FileOperationDestination(
    PanelSourceId SourceId,
    string Text,
    bool UseTemplate);
```

UI text may remain a string, but by the time it reaches the planner its provider identity must be explicit.

### 9.2 Path semantics

```csharp
public interface IFilePathSemantics
{
    StringComparer Comparer { get; }

    string Normalize(string path);
    bool IsRoot(string path);
    bool IsRooted(string path);
    bool EndsInDirectorySeparator(string path);

    string? GetParent(string path);
    string GetName(string path);
    string GetNameWithoutExtension(string path);
    string GetExtension(string path);

    string Combine(string parent, string child);
    string GetRelativePath(string root, string child);
}
```

`ProviderPathRelations` can then use the provider comparer and path semantics instead of a fixed `StringComparison`.

### 9.3 Unified plan

```csharp
public sealed record CopyPlan(
    IReadOnlyList<CopyPlanItem> Items,
    long TotalBytes);

public sealed record CopyPlanItem
{
    public required PanelLocation Source { get; init; }
    public required PanelLocation Destination { get; init; }
    public required FilePanelItem SourceSnapshot { get; init; }
    public required CopyPlanItemKind Kind { get; init; }
}
```

The plan should be provider-neutral. It should not store raw Local paths as a second identity system.

Directory items should be materialized in a deterministic order. Directory metadata post-processing can still run in reverse order after children complete.

### 9.4 Feature services

```csharp
public interface IFileOperationFeatures
{
    bool TryGet<TFeature>(out TFeature? feature)
        where TFeature : class;
}

public interface IResumableFileOperations
{
    // random-access/reopen/snapshot operations required by Reliable
}

public interface IFileMetadataOperations
{
    Task<FileMetadataSnapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken);

    Task ApplyAsync(
        string path,
        FileMetadataSnapshot metadata,
        CancellationToken cancellationToken);
}

public interface ISymbolicLinkOperations
{
    // inspect link and recreate link target
}

public interface IDirectMoveOperations
{
    Task<DirectMoveResult> TryMoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);
}
```

Local implementations can internally reuse `IFileSystemPlatformOperations` and the existing `CopyResumeAnalyzer` during migration.

### 9.5 Conflict service

Conflict policy should become provider-neutral:

```text
planned target
    |
    v
destination.GetItem
    |
    v
FileOperationConflict
    |
    v
ConflictPolicyState
    |
    +-- Ask
    +-- Overwrite
    +-- Skip
    +-- Rename
    +-- OnlyNewer when supported
    +-- sticky *All normalization
```

The provider performs only the primitive action selected by common policy.

## 10. Migration Plan

No big-bang rewrite.

### Phase 0 - lock current semantics with parity tests

Before structural changes, add a provider-parameterized contract suite for behaviors that should be identical.

Use at least Local and Demo.

The recent explicit-target regression should become one contract test executed against both providers rather than two independently maintained tests.

### Phase 1 - canonicalize request identity

- Make `PanelLocation` the internal canonical source identity for every request, including Local.
- Normalize old string fields at the UI/application boundary while old engines still exist.
- Do not change execution yet.

Goal: stop propagating two identity models deeper into new code.

### Phase 2 - extract mandatory path semantics

- Introduce `IFilePathSemantics` (or equivalent).
- Implement Local, Demo, SFTP and FTP path semantics.
- Move `CombineProviderPath`, relative-path logic, name splitting, destination pattern parsing and path comparer decisions behind it.
- Remove `PanelSourceId.Local` checks from generic path helpers.

Goal: make the provider-aware planner genuinely provider-neutral before routing Local through it.

### Phase 3 - unify destination mapping and CopyPlan

- Create one source-discovery and target-mapping planner.
- Flatten recursive Local and provider directory sources into the same plan shape.
- Share wildcard, template, mask, duplicate-target, self-target and descendant validation.
- `BuildPlan` preview uses this exact plan.
- Keep old Local/provider executors temporarily consuming the common plan.

This is the safe Option-2 intermediate state.

### Phase 4 - unify conflict policy and preflight

- Extract one conflict builder/policy state.
- Centralize `OverwriteAll`, `SkipAll`, `RenameAll` stickiness.
- Centralize directory/file compatibility and rename generation.
- Decide explicitly whether `OnlyNewer` is available from common `LastWriteTime` or needs a timestamp-quality capability.

Goal: remove user-semantic conflict differences before executor unification.

### Phase 5 - route Normal Copy through one executor

- Implement one provider-neutral Normal stream-copy executor.
- Route Local -> Local Normal Copy through `LocalFilePanelSource` just like Demo/SFTP/FTP.
- Keep local advanced post-copy behavior behind temporary feature adapters that call current platform services.
- Verify Local parity before deleting any legacy local code.

At the end of this phase, the most common copy path is canonical.

### Phase 6 - extract Reliable and Fast salvage as strategies/capabilities

- Adapt current `CopyResumeAnalyzer` into a Reliable strategy selected by explicit source/destination capabilities.
- Keep Local as the first provider exposing Reliable.
- Move Fast salvage control flow into the common executor/strategy layer; initially expose it only for providers validated by tests.
- Remove `ValidateProviderCopyOptions` checks based on "provider" identity; availability comes from capability negotiation.

### Phase 7 - extract metadata and link capabilities

- Put timestamp/attribute/ACL/Unix-mode mechanics behind typed metadata capabilities.
- Put symlink inspection/recreation behind link capability.
- Local capability adapters reuse current platform implementations.
- Provider UI/options must no longer silently imply unsupported preservation behavior; any user-visible change here should be captured in product intent before implementation.

### Phase 8 - unify Move

- Use the same destination planner and conflict policy as Copy.
- Add direct-move capability for same-provider optimized moves.
- Use common Copy + Delete fallback when direct move is unavailable and both endpoint capabilities allow it.
- Keep Provider A -> Provider B and cross-provider Move disabled at UI level until contract tests and product intent explicitly enable them, but do not preserve an architectural prohibition in the engine.

### Phase 9 - remove legacy branches

Only after parity tests pass:

- remove `UsesProviderLocations` Copy/Move branching;
- remove `BuildLocalCopyPlan` / `BuildProviderCopyPlan` split;
- remove duplicated local/provider conflict implementations;
- remove legacy string identity fields from the internal request model;
- remove Local-specific path helpers from generic orchestration;
- collapse duplicate Local/provider regression tests into contract tests plus feature-specific tests.

Delete/CreateDirectory can be migrated separately after Copy/Move if desired; they should not block this work.

## 11. Risks

### 11.1 Destination-text ambiguity

Local paths, FTP/SFTP paths and future archive/cloud paths have different rooted/relative and separator rules.

Mitigation: make destination parsing provider-owned through `IFilePathSemantics` before planner unification.

### 11.2 Case sensitivity

Hard-coded `Ordinal` or `OrdinalIgnoreCase` is not correct for every provider/platform.

Mitigation: provider-defined comparer/identity semantics and contract tests on Windows and Unix CI.

### 11.3 Remote preflight cost

A fully materialized remote directory plan may require many network round trips before the first copy byte.

Mitigation:

- preserve a visible scanning phase;
- avoid duplicate enumeration;
- consider async enumeration later;
- optimize provider implementations without changing plan semantics.

Correctness should remain preflight-first for target mapping and collisions.

### 11.4 TOCTOU between plan and execution

A provider may change after planning.

Mitigation: the plan is not a transaction. Recheck destination existence/conflict immediately before mutation while retaining the planned target mapping.

### 11.5 Reliable generalization

A seekable stream alone does not prove safe resumability.

Mitigation: explicit Reliable capability with reopen, snapshot, length and truncate semantics; Local is the first implementation.

### 11.6 Move fallback error classification

Blindly translating any direct-move `IOException` into copy-delete may hide permission or integrity failures.

Mitigation: structured direct-move capability result (`Moved` / `NotSupported`) and actual exceptions for errors.

### 11.7 Metadata semantics differ by provider

Creation time, Unix mode, DOS attributes and ACL are not universal.

Mitigation: typed optional metadata capabilities; do not force fake values into the base provider contract.

### 11.8 Symlink semantics differ by provider

Some providers expose links, some dereference them, and some cannot create them.

Mitigation: explicit link capability and common policy, with provider-specific mechanics.

### 11.9 Regression surface

Local Copy has mature behavior around Reliable, Fast salvage, read-only overwrite, metadata and platform-specific links.

Mitigation: route only Normal Copy first, keep capability adapters to existing local mechanics, then migrate one feature at a time.

## 12. Test Strategy

### 12.1 Provider contract suite

Create one parameterized suite whose fixtures expose at least:

```text
LocalFilePanelSource
DemoFilePanelSource
```

Core parity cases:

```text
CopyFileIntoDirectory
CopyFileWithExplicitNewName
CopyFileToExistingExplicitName
CopyDirectoryRecursively
CopyMultipleItems
CopyWithWildcard
CopyWithTemplate
CopyWithMask
OverwriteExistingFile
SkipExistingFile
RenameOnConflict
OverwriteAllIsSticky
SkipAllIsSticky
RenameAllIsSticky
FileDirectoryConflictRejected
DuplicateFinalTargetsRejected
CopyToSameItemRejected
CopyDirectoryIntoDescendantRejected
CancellationStopsOperation
PreviewMatchesExecutionPlan
```

Where semantically valid, also run:

```text
OnlyNewerCopiesNewer
OnlyNewerSkipsOlder
```

If timestamp reliability differs by provider, put these behind a declared timestamp capability rather than provider-name checks.

### 12.2 Path-semantics contract suite

For every provider path implementation:

```text
NormalizeIsIdempotent
ParentEventuallyReachesRoot
CombineThenParentRoundTrips
RelativePathRoundTrips
NameAndExtensionAreStable
ComparerMatchesProviderIdentityRules
DescendantDetectionUsesProviderSemantics
```

Run Local path contracts on Windows and Unix CI.

### 12.3 Move contract suite

```text
MoveFileIntoDirectory
MoveFileWithNewName
MoveDirectory
MoveConflictOverwrite
MoveConflictSkip
MoveConflictRename
MoveSameItemRejectedOrNoOpPerChosenSemantic
MoveDirectoryIntoDescendantRejected
DirectMoveUsedWhenAvailable
CopyDeleteFallbackUsedWhenDirectMoveUnavailable
SourceDeletedOnlyAfterSuccessfulCopy
```

### 12.4 Feature-specific tests

Do not force these into every provider contract:

Reliable:

- valid resume;
- corrupted tail rollback;
- changed source snapshot;
- source read retry;
- destination write retry;
- truncate and resume progress.

Fast salvage:

- unreadable file does not stop later files;
- partial cleanup;
- keep-partial rename;
- destination write failure is not silently ignored.

Metadata:

- file timestamps;
- directory timestamps restored after children;
- attributes;
- Unix mode;
- Windows ACL.

Links:

- CopyLink;
- CopyTargetContents;
- clear error when recreation is unsupported.

### 12.5 Provider adapter tests

SFTP and FTP should keep adapter-level tests for:

- normalization;
- basic read/write;
- create/delete/rename;
- exception translation;
- feature declarations.

These tests complement, not replace, the common semantic contract suite.

### 12.6 Regression requirement

A user-semantic bug that affects both Local and provider paths should normally add **one contract test** executed for both, not two separately coded tests.

That is one of the measurable success criteria for the architecture change.

## 13. Final Recommendation

Yes, CSharpFar can and should have one canonical Copy/Move orchestration path for Local and providers.

The current `IFilePanelSource` already supplies the basic primitives required for Normal Copy. The missing pieces are:

1. provider-owned path semantics rich enough to eliminate Local checks from generic planning;
2. one provider-neutral recursive Copy/Move plan;
3. one common conflict-policy implementation;
4. typed optional capability services for Reliable/random access, metadata, links and direct move;
5. parity contract tests that define common user-visible semantics once.

The recommended target is **Option 3: unified pipeline + typed provider capabilities**, with the Option-4 invariant that generic orchestration contains no Local-specific branch.

Do not preserve separate Local and provider engines merely to protect Reliable, Fast salvage, metadata or symlink behavior. Preserve those behaviors by moving their mechanics behind capabilities and strategies while keeping their policy and orchestration in the single pipeline.

Migration should proceed through a common-plan intermediate state, route Local Normal Copy through the provider abstraction first, and only then migrate advanced Local features and Move. No big-bang rewrite is justified.

The architectural boundary should be:

```text
COMMON
-----
selection
source discovery
recursive planning
destination interpretation
source -> final target mapping
wildcards/templates/masks
preflight validation
conflict policy
progress/cancellation
copy/move orchestration

PROVIDER / FEATURE MECHANICS
----------------------------
path grammar
read/write
random access / truncate / resume primitives
direct move primitive
metadata
ACL / Unix mode
symbolic links
platform-specific filesystem operations
```

That boundary removes the duplication that caused the recent explicit-target bug without sacrificing the richer capabilities of the local filesystem.