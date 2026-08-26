# ETS2 Assist 1.0.24 — Web runtime synchronization

This package intentionally updates the publish-time web runtime as well as MainForm/BuildInfo.
Stable WebOverlay URLs remain unchanged. Local JS/CSS references are rewritten by MainForm to a per-run Unix epoch cache token.

Included web sources:
- web_pda_map.html + map JS/CSS from 1.0.21
- web_ui_hybrid.html from 1.0.17 (including indicator placement fixes)
- hybrid JS/CSS from the known working runtime set

The archive does not contain the obsolete root data folder.
