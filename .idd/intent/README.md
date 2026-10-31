# IDD Intent

This directory contains the current working model of product intent and current
decision/research records.

Read `INDEX.md` first, then read the numbered current documents directly under
this directory that apply to the change.

Numbered documents may be specs, ADRs, or active spikes.

Do not treat templates, support files, generated reports, or deleted Git history
as current product intent.

There is no `.idd/intent` archive lifecycle. Deleted or previous document
versions are available through Git history.

A spec document has no lifecycle status: its presence here means it is current.
Do not mark specs as Current, Completed, Deprecated, Retired, or Superseded.
Edit an owning spec in place or migrate its remaining current intent and delete
it. ADR status remains part of ADR decision records; a spike remains only while
its question is active.

## Ownership of product intent

`IDD-0001` defines product purpose, system-wide invariants and boundaries.
Feature-specific behavior, acceptance criteria and verification belong only
in the owning feature specifications. Do not add a duplicate requirement or
feature-level checklist to `IDD-0001` when changing an ordinary feature.

`INDEX.md` is the authoritative **routing catalog** for current documents,
not an additional normative source of behavior. Update an owning spec and its
index entry when responsibility changes; do not copy its rules into the index.
If an existing spec owns the behavior, update it instead of creating a second
owner. When moving a unique rule, retain it in the new owner before deleting it
from the old document. Do not enforce an arbitrary length limit on `IDD-0001`.
