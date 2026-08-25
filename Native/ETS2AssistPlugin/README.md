# ETS2 Assist native plugin

This is the native-plugin replacement for `ets2-telemetry-server.dll` + the separate telemetry-server process + `trucktel.dll` + the transitional `ets2_assist_input.dll`.

## Target architecture

- SCS Telemetry SDK is read directly from the ETS2 game process.
- WebSocket server is hosted by this DLL.
- REST endpoint `/api/rest/single/frame/paused` provides the pause state used by ETS2 Assist.
- Telemetry WebSocket remains compatible with the current web UI contract: `GET/WS /api/ws/delta/flat/?throttle=50`.
- Input is handled through the SCS Input SDK, not `SendInput` and not F1/window messages.
- Input WebSocket accepts the same semantic shape as TruckTel: `['press','pause']`, `['hold','<input>']`, `['release','<input>']`, `['set','<input>',value]`.

## SDK headers

The project intentionally uses the official SCS SDK headers rather than shipping a runtime DLL. Copy the headers from the SCS Telemetry/Input SDK package used by the installed ETS2 version into `sdk/include/` before building.

No third-party runtime DLL is required by this project.

## Build

Build with Visual Studio/MSVC x64 or CMake on Windows. The resulting DLL goes to:

`ets2_assist\\bin\\win_x64\\plugins\\ets2_assist_plugin.dll`

For the first migration run, keep `trucktel.dll` installed as a telemetry fallback. Once this DLL is verified against the live game, remove TruckTel and `ets2_assist_input.dll` from the ETS2 plugin folder.
