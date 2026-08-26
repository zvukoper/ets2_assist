# Splash regression fix 1.0.29

Restored the exact SplashForm.cs implementation from the earlier build whose runtime log showed source=314x314, display=314x314, form=314x314, dpi=96, pad=0.

Changes:
- per-thread PerMonitorV2 DPI context inside SplashForm before any form sizing;
- AutoScaleMode=None and 96 DPI form dimensions;
- fixed MinimumSize/MaximumSize to bitmap size;
- clone/fallback PNG resolution forced to 96 DPI;
- UpdateLayeredWindow uses physical bitmap dimensions;
- removed the process-wide SetProcessDpiAwarenessContext override from Program.cs because it regressed the known-good Splash behavior.
