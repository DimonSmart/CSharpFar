# CSharpFar.Ui Demo

This sample is an independently runnable, deterministic Git-client-shaped showcase for the public `CSharpFar.Console` and `CSharpFar.Ui` APIs. It uses only in-memory fake data: it does not inspect a repository, invoke Git, read configuration, persist changes, use the network, or model Git completely.

## Build and run

From the repository root:

```powershell
dotnet build samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj
dotnet run --project samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj
```

Run it in an interactive terminal. Redirected input or output is deliberately rejected because the production terminal drivers require a real terminal and must be able to restore its state.

## Routes and showcased workflows

The top menu contains Repository, Commit, Branch, View, Demo, and Help. Press `F9` or use menu mnemonics to open it; use arrows and Enter to choose an item, or click menu items with the mouse.

The commit table supports Up/Down, Page Up/Page Down, Home/End, mouse clicks, the mouse wheel, Enter, and double-click. Enter or double-click opens Commit Details. The details dialog combines metadata, a scrollable changed-files table, totals, and Copy Hash.

The function-key bar routes these actions:

- `F1` keyboard help; `F2` create commit; `F3` commit details; `F4` switch branch.
- `F5` copy hash; `F6` compact/full dates; `F7` find a Unicode example.
- `F8` delete commit; `F9` menu; `F10` exit.

The same actions can be reached by clicking the function-key bar. Dialog buttons, fields, choices, checkboxes, dropdowns, lists, tables, scrollbars, and menus support their normal keyboard routes and the mouse.

Repository workflows include fake Pull and Push, Open Recent, settings, and confirmed data reset. Commit workflows include creation with inline validation, details, copy, deletion, and search by text, author, branch, case, and merge inclusion. Branch workflows include switching plus validated create, rename, protection, and deletion. View and Demo expose palette changes, date and merge visibility, Unicode data, a merge-conflict choice, and simulated pull failure. Help includes shortcuts and About. Clone and Online Help are visibly disabled because the sample intentionally has no real Git or online integration.

All mutations are local and deterministic apart from a newly created commit's current timestamp. Reset restores the original 34 commits, five branches, protection matrix, settings, filters, and selection normalization. Fake hashes, changed files, Pull progress, success, cancellation, and failure are demonstrations rather than Git-compatible behavior.

## Real-terminal walkthrough

Use this checklist on each production terminal backend you want to validate:

1. Start the sample and confirm the menu, commit table, selected row, status line, and key bar appear without raw escape text. Move the selection with arrows, Page Down, a click, and the wheel.
2. Press `F1`, resize the terminal while Help is open, then close it. Open Commit Details with Enter and scroll the changed-files table. Use Copy Hash and close the dialog.
3. Press `F2`. Submit an empty subject and confirm inline validation keeps the dialog open and returns focus to Subject. Fill the controls, create the commit, and confirm the created row remains selected. Repeat and cancel; confirm no row is added.
4. Use Commit > Search Commits. Apply author/branch/text criteria, observe `[FILTER ACTIVE]`, then clear the search. Cancel a second search and confirm the prior state is unchanged.
5. Use Branch > Switch Branch, then Manage Branches. Exercise create/rename/protect/delete and confirm current or protected branches explain why unsafe operations are unavailable. Cancel dialogs and confirm state is preserved.
6. Open Repository Settings. Resize it to a short height, scroll through all fields and the tri-state matrix, provoke required-field validation, then save. Reopen it to confirm the in-memory values; cancel without changing them.
7. Run fake Pull to completion. Run it again and cancel with Escape or Cancel, then run Demo > Pull failure. Confirm completed, cancelled, and failed states have distinct messages and the UI remains usable.
8. Open Demo > Merge conflict warning and try Keep Local, Keep Remote, and Cancel. Confirm the warning appearance and resulting status text.
9. Narrow the terminal until optional table columns disappear and text truncates; make the terminal wider again. Confirm selection remains coherent. Also test resizing while a dialog and menu are open.
10. Use View > Select Theme and choose a different palette. Confirm the main surface and subsequent dialogs use it. Toggle compact dates and merge visibility.
11. Click a table row, double-click it for details, drag/click available scrollbars, click dialog and key-bar buttons, and verify hover/reveal behavior supported by the terminal.
12. Choose Reset data, first cancelling and then confirming. Confirm the initial branch, data, settings, filtering, and selection return. Exit with `F10`, then start once more and exit through Repository > Quit.
13. After normal exit, cancelled operations, and (where safely testable) an exceptional shutdown, verify the terminal cursor, input mode, mouse tracking, screen buffer, and colors are restored and ordinary shell input/echo work normally.

