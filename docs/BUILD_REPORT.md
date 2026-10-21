# Informe de compilación y validación

**Fecha local:** 2026-10-20  
**Workspace:** Windows 11 (`win-x64`)  
**SDK usado:** .NET `10.0.402`  
**Solución:** `TensaZangetsu.slnx` (31 proyectos)

## Resultados

| Verificación | Comando | Resultado |
| --- | --- | --- |
| Build Release completo | `dotnet build TensaZangetsu.slnx -c Release --no-restore` | Correcto; 0 advertencias, 0 errores |
| Pruebas locales del indexador | `dotnet test .tests\TensaZangetsu.AssetIndexing.Tests\TensaZangetsu.AssetIndexing.Tests.csproj -c Release --no-restore` | 6 aprobadas, 0 fallidas |
| Higiene del diff | `git diff --check` | Correcto |
| Controles GUI | Parseo local de `MainWindow.axaml`; revisión de los 39 botones con comando | Ninguna acción de hardware no verificada quedó habilitada |
| Indexación local | `dotnet run --project scratch\AssetIndexer\AssetIndexer.csproj -c Release --no-build -- --assets external=ExternalAssets --assets testpoints=TESTPOINTS --assets app=src\TensaZangetsu.App\Assets --assets services=src\TensaZangetsu.Services\Assets --catalog Catalog\devices.json --overrides Catalog\overrides.json --output Catalog\AssetsManifest.json --validate` | Válido; 31.369 archivos, 0 fallidos, 7 diagnósticos, 7.094 grupos de duplicados, 0 bundles explícitos |

El manifiesto generado se guarda localmente en `Catalog/AssetsManifest.json` (37.525.237 bytes) y está ignorado por Git. La comparación con `artifacts/initial-assets-audit-20261020.csv` encontró las 31.366 rutas auditadas previamente y los mismos SHA-256; también observó 3 rutas que no estaban en el CSV inicial. No se renombraron, movieron ni ejecutaron binarios.

Los siete diagnósticos del manifiesto son advertencias `XmlNotClassified` de archivos de prueba XML incluidos entre los assets. Se mantienen conservadoramente sin clasificación. El manifiesto no crea bundles por inferencia de nombres.

## Alcance no validado

- No se conectó ni se probó ningún dispositivo físico.
- No se validaron loaders por modelo, BIT, revisión, chipset o modo de conexión.
- No hay pruebas de GUI, protocolos, hardware ni flujos de escritura en esta fase; seis pruebas locales sólo cubren el indexador.
- No se debe interpretar que compilar los módulos de protocolo equivale a soporte operativo.
