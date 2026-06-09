# Worklog Specification Import Report

Date: 2026-06-09

## Source roots inspected

- .worklog/
- .specs/README.md
- .specs/INDEX.md

## Source methodology detected

The source root contains numbered Markdown documents using spec, adr, and spike naming. Many later files already use an IDD-like structure with Goal, Context, Scope, Non-goals, Acceptance criteria, and Verification. Early files include older task/specification material with implementation-order sections.

## Imported as current intent

- `.worklog/0001.spec-csharpfar-file-manager.md` -> `.specs/0001.spec-csharpfar-file-manager.md`
- `.worklog/0002.adr-render-buffering-strategy.md` -> `.specs/0002.adr-render-buffering-strategy.md`
- `.worklog/0003.spec-display-modes-palettes-settings-menu.md` -> `.specs/0003.spec-display-modes-palettes-settings-menu.md`
- `.worklog/0004.spec-column-navigation-ctrl-o-resize.md` -> `.specs/0004.spec-column-navigation-ctrl-o-resize.md`
- `.worklog/0005.spec-drive-volume-selection.md` -> `.specs/0005.spec-drive-volume-selection.md`
- `.worklog/0006.spec-file-folder-highlighting.md` -> `.specs/0006.spec-file-folder-highlighting.md`
- `.worklog/0007.spec-file-panel-options.md` -> `.specs/0007.spec-file-panel-options.md`
- `.worklog/0008.spec-menu-bar-popup-shadow.md` -> `.specs/0008.spec-menu-bar-popup-shadow.md`
- `.worklog/0009.spec-open-panel-item.md` -> `.specs/0009.spec-open-panel-item.md`
- `.worklog/0010.spec-file-operations.md` -> `.specs/0010.spec-file-operations.md`
- `.worklog/0012.spec-search-feature.md` -> `.specs/0012.spec-search-feature.md`
- `.worklog/0013.spec-dynamic-function-key-bar.md` -> `.specs/0013.spec-dynamic-function-key-bar.md`
- `.worklog/0014.spec-quick-view-panel.md` -> `.specs/0014.spec-quick-view-panel.md`
- `.worklog/0016.adr-console-viewport-rendering.md` -> `.specs/0016.adr-console-viewport-rendering.md`
- `.worklog/0017.spec-current-console-program-launch.md` -> `.specs/0017.spec-current-console-program-launch.md`
- `.worklog/0018.spec-command-history-completion.md` -> `.specs/0018.spec-command-history-completion.md`
- `.worklog/0019.spec-paranoid-copy-resume.md` -> `.specs/0019.spec-paranoid-copy-resume.md`
- `.worklog/0020.spec-unified-window-scrollbars.md` -> `.specs/0020.spec-unified-window-scrollbars.md`
- `.worklog/0021.spec-scrollbar-mouse-interaction.md` -> `.specs/0021.spec-scrollbar-mouse-interaction.md`
- `.worklog/0022.spec-hidden-console-history-scroll.md` -> `.specs/0022.spec-hidden-console-history-scroll.md`
- `.worklog/0023.spike-architecture-review.md` -> `.specs/0023.spike-architecture-review.md`
- `.worklog/0027.spec-far-command-line-shortcuts.md` -> `.specs/0027.spec-far-command-line-shortcuts.md`
- `.worklog/0028.spec-panel-visibility-modes.md` -> `.specs/0028.spec-panel-visibility-modes.md`
- `.worklog/0029.spec-sftp-file-system-provider.md` -> `.specs/0029.spec-sftp-file-system-provider.md`
- `.worklog/0030.spec-panel-source-error-retry.md` -> `.specs/0030.spec-panel-source-error-retry.md`
- `.worklog/0032.spec-unified-file-viewer.md` -> `.specs/0032.spec-unified-file-viewer.md`
- `.worklog/0033.spec-viewer-encoding-selection.md` -> `.specs/0033.spec-viewer-encoding-selection.md`
- `.worklog/0034.spec-panel-quick-search.md` -> `.specs/0034.spec-panel-quick-search.md`
- `.worklog/0035.spec-ftp-ftps-file-system-provider.md` -> `.specs/0035.spec-ftp-ftps-file-system-provider.md`
- `.worklog/0036.spec-application-icon.md` -> `.specs/0036.spec-application-icon.md`
- `.worklog/0038.spec-history-enabled-text-input.md` -> `.specs/0038.spec-history-enabled-text-input.md`
- `.worklog/0046.spec-built-in-text-editor.md` -> `.specs/0046.spec-built-in-text-editor.md`
- `.worklog/0047.spec-farnet-10-managed-api-compatibility.md` -> `.specs/0047.spec-farnet-10-managed-api-compatibility.md`
- `.worklog/0048.spec-create-new-file-dialog.md` -> `.specs/0048.spec-create-new-file-dialog.md`
- `.worklog/0049.spec-editor-far-editing-commands.md` -> `.specs/0049.spec-editor-far-editing-commands.md`
- `.worklog/0050.spec-editor-textmate-syntax-highlighting.md` -> `.specs/0050.spec-editor-textmate-syntax-highlighting.md`
- `.worklog/0051.spike-editor-utf8-4byte-cursor.md` -> `.specs/0051.spike-editor-utf8-4byte-cursor.md`
- `.worklog/0052.spec-viewer-far-hotkeys.md` -> `.specs/0052.spec-viewer-far-hotkeys.md`
- `.worklog/0053.spec-delete-progress-dialog.md` -> `.specs/0053.spec-delete-progress-dialog.md`
- `.worklog/0054.spec-paranoid-copy-auto-retry-and-control-mouse.md` -> `.specs/0054.spec-paranoid-copy-auto-retry-and-control-mouse.md`
- `.worklog/0055.spec-directory-shortcuts.md` -> `.specs/0055.spec-directory-shortcuts.md`
- `.worklog/0056.spec-built-in-cd-panel-navigation.md` -> `.specs/0056.spec-built-in-cd-panel-navigation.md`
- `.worklog/0057.spike-unix-macos-portability.md` -> `.specs/0057.spike-unix-macos-portability.md`
- `.worklog/0058.spec-test-and-fallback-cleanup.md` -> `.specs/0058.spec-test-and-fallback-cleanup.md`
- `.worklog/0059.spec-panel-cursor-normalization.md` -> `.specs/0059.spec-panel-cursor-normalization.md`

## Archived instead of imported as current

- `.worklog/0024.spec-unused-leftover-cleanup.md` -> `.specs/archive/0024.spec-unused-leftover-cleanup.md`
- `.worklog/0025.spec-application-function-key-refactor.md` -> `.specs/archive/0025.spec-application-function-key-refactor.md`
- `.worklog/0026.spec-application-command-handlers-refactor.md` -> `.specs/archive/0026.spec-application-command-handlers-refactor.md`

These documents describe cleanup or refactor tasks whose primary purpose is local implementation work rather than rebuildable product behavior. They are retained for traceability only.

## Structural normalization decisions

- Source file boundaries were kept only where a source file already represented one distinct product area, ADR, or spike.
- .worklog/0001.spec-csharpfar-file-manager.md was imported as the baseline product specification, but its development stages, agent instructions, backlog, and prompt text were omitted from the current spec.
- .worklog/0003.spec-display-modes-palettes-settings-menu.md was imported as current display/settings intent, but its implementation-order and Definition-of-Done sections were omitted; the later Far-like visual polish section was kept because it defines product behavior.
- .worklog/0006.spec-file-folder-highlighting.md and .worklog/0008.spec-menu-bar-popup-shadow.md were imported as current intent with their implementation-stage sections omitted.
- Existing .specs/INDEX.md was regenerated to point at imported current documents and historical archive documents.

## Skipped process-only fragments

- Development stages and stop points from .worklog/0001.spec-csharpfar-file-manager.md.
- Agent prompt/instruction text from .worklog/0001.spec-csharpfar-file-manager.md.
- Implementation order from .worklog/0003.spec-display-modes-palettes-settings-menu.md.
- Implementation stages from .worklog/0006.spec-file-folder-highlighting.md.
- Implementation stages from .worklog/0008.spec-menu-bar-popup-shadow.md.
- Cleanup/refactor task specifications 0024, 0025, and 0026 were not imported as current normative specs.

## Conflicts found

No direct semantic conflicts were resolved during import. Potentially obsolete early MVP non-goals, such as FTP/SFTP being outside the first version while later FTP/SFTP provider specs exist, were left visible as version/scope history rather than automatically rewritten.

## Documents requiring human review

- .specs/0001.spec-csharpfar-file-manager.md: still broad and may later be split into smaller overview, architecture, panel, shell, viewer, editor, settings, and testing specs.
- .specs/0057.spike-unix-macos-portability.md: contains implementation-slice recommendations inside a spike result; kept because spike documents may retain research outcome and follow-up recommendations.

## Quality gate notes

- Current specs describe product behavior, durable constraints, ADRs, or spikes.
- Process sections from mixed old documents were not imported as normative intent.
- Historical cleanup/refactor documents are outside current numbered specs under .specs/archive/.
