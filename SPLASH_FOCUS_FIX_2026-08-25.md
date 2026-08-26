# Splash and quest dialog focus fix

- Quest completion and success dialogs are dedicated modal WinForms windows with their own HWND.
- Foreground transitions use AttachThreadInput/SetForegroundWindow/SetActiveWindow/SetFocus.
- After the final quest dialog closes, foreground is explicitly returned to eurotrucks2.exe.
- Splash uses PerMonitorV2 DPI awareness, disables WinForms autoscaling, and passes the PNG physical bitmap size to UpdateLayeredWindow.
