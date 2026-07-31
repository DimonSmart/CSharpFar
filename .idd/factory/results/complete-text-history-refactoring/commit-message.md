Complete text field history refactoring

Performed by: IDD Factory

Why:
Finish the provider-based form history migration and remove the remaining test-time compatibility usage.
Preserve stable persisted identifiers while making history ownership explicit at every field.

Result:
- Application and module dialogs use the injected history provider and stable typed IDs.
- FTP and SFTP fields own their history bindings through named TextField form state.
- Search text fields preserve expected initial-selection and history behavior.
- Production fallback and positional-history APIs are removed; tests and build pass.
