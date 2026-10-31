# MTK asset catalog

Run this script from **Windows PowerShell** after placing the source files on
the machine:

```powershell
Set-Location 'C:\path\to\Tensa-Zangetsu-Portable\mtk-suite'
.\scripts\import-mtk-assets.ps1 -Source 'D:\dbge\Documents'
```

The importer recursively scans every subfolder, calculates SHA-256, copies
files into `Assets/devices/<derived-folder>/`, and writes
`Catalog/mtk-assets.json`.

The derived folder is only an inventory grouping. It does **not** prove that a
loader matches a phone. Review `device.json`, add verified model/SoC/BIT and
security constraints, and keep unresolved files under `inventory/`.

The source files are never executed or modified. Use `-Rebuild` only when
intentionally replacing previously imported copies.

Validate the imported files before selecting any resource:

```powershell
.\scripts\validate-mtk-assets.ps1
```

The validator checks manifest schema version, contained paths, file lengths and
SHA-256 values. It does not infer device compatibility and does not execute
loaders, preloaders, Auth or firmware files.

To make a resource selectable by the connection host, set its manifest entry
to `verificationStatus: "Verified"` and add explicit `hardwareCodes` and, when
needed, `hardwareSubCodes` plus `daKind`. Entries without those fields remain
inventory-only and are rejected by the runtime resolver.

## Importing external model metadata

The optional `UNLOCK` data tree can be indexed without copying or executing
its binaries:

```powershell
.\scripts\import-unlock-model-catalog.ps1 `
  -Source 'C:\path\to\Tensa-Zangetsu-Portable-main\UNLOCK'
```

This produces `Catalog/unlock-models.json` with model/platform/mode metadata
and profile hashes. It deliberately excludes DLL/EXE files, loaders, operation
scripts, credentials and bypass resources. All entries remain
`IndexedUnverified`.
