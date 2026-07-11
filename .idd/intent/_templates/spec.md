# NNNN.spec-short-title

## Intent

Describe the durable product intent.

## Related Specifications

List related specs, ADRs, or spikes that define adjacent, shared, or dependent
intent.

## Behavior

Describe observable behavior and domain contracts.

## Durable Architecture And Constraints

Describe only architecture boundaries and technical constraints that future
implementations must preserve.

Include implementation patterns, frameworks, or libraries only when changing
them would change product behavior, compatibility, public contracts, security,
operability, or an accepted architecture decision.

Do not include private class names, private methods, file names, constructor
signatures, dependency-wiring steps, temporary workarounds, migration steps, or
current code structure.

## Non-Goals

List behavior or scope that is intentionally excluded.

## Acceptance Criteria

List conditions that must hold for the specification to be satisfied.

## Verification

Describe durable verification properties and evidence required to establish
correctness.

State what must be verified, not the local command used to run verification.

Do not include build commands, test-runner commands, CI commands, test class
names, temporary source scans, or step-by-step execution instructions.

Good: Automated coverage verifies that nested modal overlays redraw correctly
after both viewport growth and shrink.

Bad: Run the local build and test commands.

Good: Large files are compared incrementally without loading the complete
content into memory.

Bad: FileComparerService must use a 64 KB buffer in CompareStreamsAsync().
