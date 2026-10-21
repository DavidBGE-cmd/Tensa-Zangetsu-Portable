# Tensa-Zangetsu Portable

Framework de escritorio y CLI para catalogar assets de firmware y desarrollar herramientas de servicio para dispositivos Android. La solución contiene módulos para varios protocolos, pero la presencia de código, assets o nombres de modelos **no acredita compatibilidad operativa**.

## Estado actual

- La solución compila en Release con el SDK .NET 10.
- El indexador de assets funciona offline: inventaría rutas, tamaños y SHA-256 sin modificar los archivos originales.
- El catálogo separa identidades de dispositivos, overrides y el manifiesto físico generado.
- Los flujos de operación de hardware de la GUI están cerrados: no se ha validado ningún backend de dispositivo y las acciones no deben presentarse como exitosas.
- No se declara ningún modelo probado en hardware. Los protocolos presentes en la solución requieren validación independiente, pruebas y evidencia antes de habilitar una operación.

Consulta [docs/PROGRESS.md](docs/PROGRESS.md), [docs/INITIAL_AUDIT.md](docs/INITIAL_AUDIT.md) y [docs/UNLOCKTOOL_GAP_ANALYSIS.md](docs/UNLOCKTOOL_GAP_ANALYSIS.md) para el progreso, la auditoría y el estado de capacidades.

## Estructura

- `src/TensaZangetsu.App`: interfaz Avalonia.
- `src/TensaZangetsu.CLI`: CLI del producto.
- `src/TensaZangetsu.Services`: servicios y catálogo.
- `src/TensaZangetsu.Protocol.*`: contratos y módulos de protocolo.
- `src/TensaZangetsu.AssetIndexing`: inventario local y validación de assets.
- `scratch/AssetIndexer`: CLI auxiliar del indexador.
- `Catalog/`: identidades, overrides y esquemas. `AssetsManifest.json` se genera localmente y se ignora en Git.
- `.tests/`: pruebas locales ignoradas por Git.

## Requisitos y compilación

Se requiere el SDK .NET 10. Para compilar:

```powershell
dotnet build TensaZangetsu.slnx -c Release
```

Para iniciar la interfaz:

```powershell
dotnet run --project src\TensaZangetsu.App\TensaZangetsu.App.csproj
```

La GUI inicializa los catálogos y enumera puertos de forma asíncrona. Enumerar un puerto no equivale a identificar un dispositivo ni a validar una conexión.

## Indexación local de assets

El indexador funciona sin conectar un dispositivo, no ejecuta binarios ni altera los archivos de entrada. Ejemplo:

```powershell
dotnet run --project scratch\AssetIndexer -- --assets external=ExternalAssets --assets testpoints=TESTPOINTS --assets app=src\TensaZangetsu.App\Assets --assets services=src\TensaZangetsu.Services\Assets --catalog Catalog\devices.json --overrides Catalog\overrides.json --output Catalog\AssetsManifest.json --validate
```

Añade `--rehash` para recalcular todos los hashes. Se omiten enlaces simbólicos; los XML se leen con DTD y resolución externa deshabilitados; los binarios sin evidencia suficiente quedan sin clasificar. Los sidecars opcionales siguen el formato `<archivo original>.metadata.json`. Las asociaciones explícitas se mantienen en overrides revisados.

El CLI principal también ofrece:

```text
tensa assets index --root external=ExternalAssets --root testpoints=TESTPOINTS --json
tensa assets validate --manifest Catalog\AssetsManifest.json --json
```

Los archivos originales deben conservar sus nombres y relaciones de paquete. No se deben inferir SoC, BIT, revisión, función ni compatibilidad a partir del nombre, la carpeta o un hash coincidente.

## Criterios de capacidad

Cada combinación de modelo y operación necesita evidencia propia:

- **Available**: backend integrado, pruebas pertinentes y evidencia de validación documentadas.
- **ImplementedUntested**: implementación presente, aún sin validación suficiente; no se presenta como soporte probado.
- **Unavailable**: falta una dependencia, autorización o backend verificable.
- **ReferenceOnly**: información de referencia que no habilita operaciones.

No se implementan bypasses de autenticación/cuentas ni exploits. Los cambios de firmware, escritura, borrado, desbloqueo y acceso a datos sensibles no se habilitan por el mero hecho de que exista un botón, protocolo o asset. Las capacidades pendientes deben permanecer explícitamente no disponibles.

## Licencia

Consulta [LICENSE](LICENSE) para los términos de distribución.
