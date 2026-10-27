# Auditoría inicial

**Fecha:** 2026-10-20  
**Tarea:** `TZS-2026-10-20-AUDIT`

## Estado del workspace

- La raíz contiene una solución funcional existente; no se generará otra solución ni se moverán proyectos.
- `PROMPT_ANTIGRAVITY_TENSA_ZANGETSU.md` ya estaba presente como archivo no rastreado. Se conserva sin modificar.
- `AssetsManifest.json` existe en la raíz, pero Git lo ignora (`.gitignore:460`) y no está rastreado. Se conserva; el manifiesto actual se genera en `Catalog/AssetsManifest.json`.
- No se encontraron proyectos de pruebas (`*Tests*.csproj`) ni directorios de pruebas en el escaneo inicial.

## Entorno y compilación de referencia

| Elemento | Evidencia |
| --- | --- |
| Sistema | Windows 11, build 26100, `win-x64` |
| SDK seleccionado | .NET SDK `10.0.402`, MSBuild `18.9.15` |
| SDK instalado | .NET 1.1.14, 2.1.202/818, 2.2.207, 3.0.103, 3.1.426, 5.0.408, 6.0.428, 7.0.410, 8.0.426, 9.0.319 y 10.0.402 |
| Workloads | No hay workloads instalados |
| Solución | `TensaZangetsu.slnx`, 31 proyectos |
| Build de referencia | `dotnet build TensaZangetsu.slnx -c Release --no-restore`: correcto; 0 advertencias, 0 errores |
| Tests de referencia | No ejecutados: no se encontraron proyectos de pruebas |

En el momento de la auditoría no existían `global.json`, `Directory.Packages.props`, `NuGet.config` ni `packages.lock.json` en la raíz. Después se añadió `NuGet.config` para permitir restaurar el proyecto de pruebas local. Avalonia está fijado en `12.1.4` en el proyecto de escritorio y CommunityToolkit.Mvvm en `8.4.2`; las dependencias están declaradas directamente en los proyectos.

## Estructura y arquitectura encontradas

La solución ya contiene aplicaciones Avalonia y CLI, servicios, abstracciones, transportes y módulos de protocolo. Los proyectos `TensaZangetsu.App`, `TensaZangetsu.CLI` y `TensaZangetsu.Services` apuntan a `net10.0`; los otros 28 proyectos apuntan a `net8.0`.

La GUI ya tiene cinco áreas de navegación y una vista principal extensa. Usa Avalonia `12.1.4`, CommunityToolkit.Mvvm `8.4.2` y bindings compilados desactivados en `MainWindow.axaml`. El CLI existente tiene manejo de Ctrl+C y logging. En el código examinado aparecen `Task.Run` en flujos de servicio y comandos; falta validar de forma sistemática que ninguna operación bloquee el Dispatcher, que todas las tareas tengan propietario y que GUI/CLI compartan el mismo motor.

Hay catálogos y un resolvedor existentes bajo `TensaZangetsu.Services`. Parte del catálogo es código estático; el manifiesto físico existente usa un formato anterior y la muestra inspeccionada no contiene hashes SHA-256. La presencia de un archivo o de un modelo en ese catálogo no se toma como evidencia de compatibilidad.

## Assets y preservación

Conteos obtenidos sin abrir ni ejecutar binarios:

| Raíz | Archivos | Bytes |
| --- | ---: | ---: |
| `ExternalAssets` | 21.785 | 11.430.509.362 |
| `TESTPOINTS` | 201 | 91.864.231 |
| `src/TensaZangetsu.App/Assets` | 9.377 | 6.991.579.330 |
| `src/TensaZangetsu.Services/Assets` | 3 | 4.362.448 |

Los assets ocupan aproximadamente 18,5 GB en esas raíces. El inventario no destructivo terminó correctamente en `artifacts/initial-assets-audit-20261020.csv` (ruta ignorada por Git): **31.366 archivos, 0 errores**, con ruta relativa, longitud y SHA-256. No se renombró, movió, ejecutó ni extrajo ningún original.

La guía de investigación detecta extensiones heterogéneas (`.bin`, `.mbn`, `.py`, `.elf`, `.mtk`, `.xml`, `.elt`, imágenes y otras). La extensión o carpeta no se considerará prueba de formato, chipset, revisión o compatibilidad.

## Investigación de fuentes

Consulta realizada el 2026-10-20:

- `https://unlocktool.net/models`: el acceso automatizado respondió HTTP 403. No se afirma haber verificado su catálogo.
- `https://unlocktool.net/download`: el acceso automatizado respondió HTTP 403. No se afirma haber verificado sus notas de versión.
- `https://docs.avaloniaui.net/docs/get-started/install-avalonia`: consultada; la documentación obtenida indica .NET 8 o posterior y documenta la instalación de plantillas.
- `https://antigravity.google/docs/permissions/`: consultada; describe permisos por acción, listas Allow/Ask/Deny y la aprobación explícita para acciones sensibles.

Los ejemplos de modelos Qualcomm indicados por el usuario se registrarán únicamente como `ReferenceOnly` hasta que la fuente pública sea accesible y se contraste. No se incorporarán asociaciones automáticas de loaders basadas en esos ejemplos.

## Riesgos iniciales

1. No hay evidencia de hardware real ni de pruebas de protocolo en este entorno.
2. Los 18,5 GB de assets requieren indexación streaming y un proceso de hash potencialmente prolongado.
3. El manifiesto ignorado existente tiene un esquema distinto; no se debe reutilizar ni sobrescribir a ciegas.
4. Las fuentes públicas de UnlockTool no fueron accesibles (HTTP 403); la comparación tendrá que separar hechos accesibles, datos aportados por el usuario e inferencias.
5. En el escaneo inicial no había proyectos de tests detectados. Después se creó un proyecto local ignorado en `.tests/TensaZangetsu.AssetIndexing.Tests`; sus resultados no forman parte de artefactos de producto.
6. La existencia de código de servicio en el repositorio no demuestra operación segura, autenticada o compatible con un modelo.

## Actualización de inventario — 2026-10-20

- `Catalog/AssetsManifest.json` se generó y validó para los cuatro roots identificados: 31.369 entradas, 0 fallos, 7 diagnósticos `XmlNotClassified`, 7.094 grupos de duplicados y 0 bundles inferidos. El archivo local de 37.525.237 bytes sigue ignorado por Git.
- La comparación de SHA-256 conservó las 31.366 rutas del CSV de auditoría inicial sin cambios. El manifiesto contiene además tres rutas observadas posteriormente:
  - `app/Loaders/Mtk/Preloaders/SM-A037F/preloader_hs03s.bin`
  - `external/Loaders/Mtk/Preloader-Auth-DA-Files-BFT/Preloader Auth DA/SAMSUNG/SM-A037F/preloader_hs03s.bin`
  - `external/Loaders/Mtk/Preloader-Auth-DA-Files-BFT/Preloader Auth DA/SAMSUNG/SM-A037F/MT676_Android_scatter.txt`
- Estas rutas adicionales se inventariaron sin alterarlas. Su aparición posterior no prueba compatibilidad ni su procedencia.

## Revalidación del workspace — 2026-10-27

- Entorno actual: Ubuntu 24.04 `linux-x64`; SDK .NET `10.0.401`; no existe `global.json`.
- La solución mantiene 33 proyectos (`TensaZangetsu.slnx` y `TensaZangetsu.sln`); no se generó ni sustituyó ninguna solución.
- `dotnet build TensaZangetsu.slnx -c Release`: correcto, 0 advertencias y 0 errores.
- El estado Git estaba limpio al iniciar la revalidación; los artefactos `bin/` y `obj/` permanecen ignorados.
- En el workspace actual no existen las raíces `ExternalAssets`, `TESTPOINTS`, `Assets` ni `assets` inspeccionadas por la auditoría anterior. Por tanto, no se repite ni se afirma el inventario histórico de 31.366/31.369 archivos.
- Los assets localizados en esta revalidación son internos al repositorio: recursos `.bin` de protocolos, dos archivos de autenticación en `src/TensaZangetsu.Services/Assets/Auth/` y `sprig/images/execution.png`. Su presencia no demuestra compatibilidad de hardware.
- No se encontraron proyectos de pruebas rastreados ni directorios `*Tests*.csproj` visibles fuera de `.git`; las pruebas históricas citadas en este documento no se vuelven a presentar como ejecutadas en este entorno.
- El siguiente trabajo documentado sigue siendo Fase F, Paso 3: adaptar el protocolo Spreadtrum a `IBackendProvider`. Requiere propuesta de diseño y confirmación antes de modificar varios módulos.
