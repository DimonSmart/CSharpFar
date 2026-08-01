Standardize modal form APIs

Performed by: IDD Factory

Why:
Standard dialog mechanics were repeatedly reimplemented by application forms.
Central helpers now keep layout, submit handling, spacing, footer, and button conventions consistent.

Result:
- Added shared modal layout, input-policy, footer, spacer, and button APIs.
- Added semantic source-row IDs to routed form results.
- Migrated standard application, FTP/SFTP, search, file-operation, and file-attributes dialogs.
- Removed invisible separators and retained only intentional non-standard input behavior.
