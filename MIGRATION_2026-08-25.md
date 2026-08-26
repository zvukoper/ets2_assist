# ETS2 Assist migration package — 2026-08-25

## Что уже изменено

1. `Ets2Telemetry.exe` больше не запускается из ETS2 Assist.
2. `ets2-telemetry-server.dll` больше не обязателен.
3. Пауза/состояние паузы читается через прямой TruckTel-compatible API:
   `GET http://localhost:8080/api/rest/single/frame/paused`
4. Во время перехода `trucktel.dll` остаётся допустимым telemetry provider.
5. После достижения случайной цели игра остаётся на паузе. Автоматический unpause удалён.
6. Пауза не переключается повторно, если цель достигнута в уже установленной паузе.
7. Splash переделан на `WS_EX_LAYERED` + `UpdateLayeredWindow`, без `TransparencyKey`, без полноэкранной формы и без изменения taskbar area.
8. Сохранение треков переведено на request/response correlation через `requestId`.

## Важная проверка

В среде разработки нет .NET SDK/Windows SDK, поэтому `dotnet publish` и native MSVC build здесь не запускаются.

Окончательную проверку выполнить на Windows:

```powershell
dotnet publish -c Release
```

Затем проверить:

- ETS2 запускается с `trucktel.dll` и `ets2_assist_input.dll`.
- Ets2Telemetry.exe НЕ появляется в процессах.
- WebSocket 8080 продолжает выдавать карту/телеметрию.
- Достижение цели: PAUSE → диалог.
- После Yes/No игра остаётся на паузе.
- Повторное нажатие штатного resume в ETS2 возвращает управление.
- Горячая клавиша сохранения реально сохраняет `.track` без гонки listener.

## Native plugin

`Native/ETS2AssistPlugin/` содержит целевую структуру собственного DLL. Это пока не финальный бинарный release: требуется собрать его против той же версии официальных SCS SDK headers, с которой проверен текущий input DLL/TruckTel.
