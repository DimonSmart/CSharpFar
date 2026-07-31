Исправь владение TextField и историю FTP/SFTP

Performed by: IDD Factory

Why:
TextField мог терять обязательную конфигурацию в альтернативных row-обёртках, а transient dropdown state разделялся между полями с одним history ID. Connection forms также дублировали долгоживущие controls и конфигурацию полей.

Result:
- TextField владеет одним input и всей его конфигурацией во всех row-представлениях.
- Persistent history отделена от per-field popup state.
- FTP/SFTP form-state владеет controls; пароли остаются masked и history-free.
- Добавлены regression tests для masking, popup lifecycle и connection history.
