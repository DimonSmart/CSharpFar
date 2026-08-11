Remove redundant DialogService wrappers

Performed by: IDD Factory

Why:
Application code can call the established DialogService input API directly.
Removing pass-through wrappers keeps dialog responsibilities explicit.

Result:
- Removed the application-level InputDialog and migrated its consumers to DialogService.Input.
- Removed the pure CreateFolderDialog wrapper and migrated its command consumer.
- Preserved standard input-dialog behavior and retained domain-specific dialogs.
