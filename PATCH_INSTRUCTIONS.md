# ETS2 Assist 1.0.23 - Epoch cache busting

1. Extract this patch over `D:\repo\ets2_assist`.
2. Run `dotnet publish -c Release`.
3. The WebOverlay window URLs remain stable:
   - `http://localhost:8082/web_ui_hybrid.html`
   - `http://localhost:8082/web_pda_map.html`
4. On each application start, the static server creates one Unix-epoch-seconds token and rewrites local `.js` and `.css` references in served HTML to `?t=<epoch>`. Existing `?v=` parameters are replaced.
5. Startup log now fingerprints the actual publish `data\web_pda_map.html` and `data\web_ui_hybrid.html` files so stale runtime data is immediately visible.
