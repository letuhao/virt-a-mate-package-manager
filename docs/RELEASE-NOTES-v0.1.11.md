# VarVault v0.1.11 — Release Notes

Windows x64 self-contained build of current `main` (Library Install targets the active AddonPackages profile).

## Highlights since v0.1.10
- **Library Install/Uninstall now targets the active profile.** Ops-bar Install no longer writes into a separate `"Library installs"` profile that VaM never loads. Packages are added to the currently active loading preset and materialize under the live AddonPackages symlink.
- **Green installed dots update correctly after install** when a real loading preset is already active (read-model refresh runs for the active profile).

## Tests
- `LibraryActivateTests.Install_selected_targets_active_profile_then_uninstall_removes_links`
- `LibraryInstallRefreshTests.Install_selected_refreshes_loaded_row_installed_state_without_full_refresh`

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer if configured.

## Known limitations
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
- Historical packages left only under an old `"Library installs"` profile are not auto-merged; re-install from Library onto the active preset if needed.
