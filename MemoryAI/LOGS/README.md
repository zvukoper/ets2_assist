# LOGS

Сюда пользователь загружает временные логи для диагностики проекта.

Агент использует файлы из этой папки как источник диагностики, прежде всего `app_workflow.log` и `app_data.log`, когда они присутствуют.

Логи являются временными. После успешной сборки их нужно удалить из этой папки; `README.md` не удалять.

## Автоматическая очистка

Удаление выполняет `compile.ps1` (Stage 3d) — вручную чистить не нужно.

Из папки удаляются **все** файлы, кроме `README.md`, а также подпапки (например,
распакованные архивы логов). Вывод сборки сообщает, что было сделано:

- `MemoryAI\LOGS cleaned: N temporary file(s) removed (README.md kept).` — очищено;
- `MemoryAI\LOGS already clean (only README.md).` — чистить нечего;
- `MemoryAI\LOGS cleanup SKIPPED: publish delivery check failed, keep the logs for diagnosis.` —
  сборка не прошла проверку доставки, логи **сохранены** (они нужны для разбора);
- `MemoryAI\LOGS cleanup WARNING: README.md is missing` — служебный файл потерян.

Очистка пропускается и при падении `dotnet publish` (скрипт выходит до очистки).
