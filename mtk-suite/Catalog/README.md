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
