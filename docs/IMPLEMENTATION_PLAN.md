# Propuesta de diseño e implementación

**Fecha:** 2026-10-20  
**Tarea:** `TZS-2026-10-20-AUDIT`

## Objetivos

- Convertir la solución existente en una suite verificable, con GUI y CLI que reflejen capacidades reales, catálogo de dispositivos separado de los archivos físicos e indexación no destructiva.
- Preservar las rutas, bytes y nombres de los assets originales.
- Mantener I/O de protocolo fuera del hilo de UI y no declarar soporte de hardware sin evidencia.
- Completar primero las capacidades offline y de lectura segura; marcar explícitamente los backends ausentes.

## No objetivos y límites de seguridad

- No reproducir UnlockTool ni importar sus binarios, licencias, servicios o material propietario.
- No implementar bypasses de FRP, cuentas OEM, MDM/KG, Activation Lock, SLA/DAA, ni exploits o payloads para evadir autenticación.
- No automatizar instalación de drivers, cambios de registro, descarga/ejecución de herramientas, ni operaciones destructivas en dispositivos.
- No afirmar compatibilidad de marca, modelo, loader o revisión a partir del nombre de un archivo o de una lista de referencia.
- No introducir nuevos proyectos Core/Infrastructure que dupliquen los 31 proyectos existentes.

## Arquitectura adaptada al repositorio

1. Mantener `TensaZangetsu.slnx`, `TensaZangetsu.App`, `TensaZangetsu.CLI`, `TensaZangetsu.Services` y los módulos de protocolo/transportes actuales.
2. Evolucionar `TensaZangetsu.Services.Catalog` y `TensaZangetsu.Services.Engine` para alojar contratos de catálogo, capacidades, estados de operación y orquestación compartida. Las abstracciones públicas permanecen libres de tipos Avalonia y de tipos de paquetes de terceros.
3. Añadir `Catalog/` con identidades, overrides validados, esquemas y manifiesto generado. El manifiesto físico no será fuente de verdad para la compatibilidad: cada relación de bundle y variante requiere metadatos y procedencia.
4. Añadir un indexador CLI dedicado y offline, reutilizando contratos del catálogo, con entrada de roots explícitos. No se hará scraping de páginas autenticadas ni se ejecutará firmware.
5. Evolucionar GUI y CLI como clientes del mismo motor y estado de operación. Enumeración al inicio pasiva; sólo una solicitud explícita adquiere el transporte y empieza el handshake.
6. Conservar los proyectos de protocolo existentes. Integrar o habilitar una operación sólo cuando el backend, restricciones, pruebas y mecanismo de autorización estén documentados; el resto informa `Unavailable`, `AuthRequired` o `ImplementedUntested`.
7. La simulación será un backend aparte, etiquetado permanentemente y sin acceso a transporte físico de escritura.

## Requisitos de comportamiento

- Máquina de estados serializada, cancelación propagada y timeouts diferenciados; una sesión no se reutiliza tras desconexión o resultado incierto.
- No usar `.Wait()`/`.Result` en UI, no ejecutar trabajo pesado en handlers, no dejar tareas fire-and-forget sin supervisión.
- La detección de dispositivos es pasiva. `CONNECT / READ INFO` no envía reboot, escritura, borrado ni WDT.
- Resolver loaders aplicando restricciones obligatorias primero; sin evidencia suficiente, retornar ambiguo/no soportado, no elegir el primer archivo.
- Todas las escrituras requieren plan inmutable ligado a identidad/sesión/geometría/hash, previsualización, confirmación específica, límites y verificación posterior. No se habilitan en esta etapa.
- Inventario por streaming, SHA-256, rutas relativas, symlinks ignorados por defecto, errores por archivo y sustitución atómica del manifiesto.
- Parser XML sin DTD/entidades externas y validación de rutas contenidas en el firmware.

## Fases y criterios de aceptación

| Fase | Alcance | Criterio de salida |
| --- | --- | --- |
| A | Auditoría, inventario de hashes, matriz comparativa y plan | Evidencia de workspace, build baseline y assets; limitaciones documentadas |
| B | Guardrails de capacidades, contratos/esquemas, wiring común | Estados tipados, configuración explícita y solución construible |
| C | Indexador y validación del catálogo/manifiesto | Tests de rutas, duplicados, metadatos, XML, errores y reemplazo atómico |
| D | Motor compartido, estados, cancelación, concurrencia y simulación | Tests sin hardware; simulación visible; ninguna operación real implícita |
| E | Integración ADB/Fastboot de solo lectura | Dependencia configurada por usuario, argumentos seguros, cancelación y pruebas con proceso fake |
| F | Adaptadores de protocolos por plataforma | Sólo backends verificables; sin bypass de autenticación; fixtures y protocolo documentados |
| G | GPT offline, firmware, planes y backups | Validación CRC/rangos/overflow, streaming y rechazo de planes obsoletos |
| H | UI/CLI, pruebas, empaquetado y reportes | Builds/tests ejecutados, capacidades honestas, guía y reporte con resultados reales |

Las fases independientes se implementan por separado y se validan antes de ampliar el alcance. El código de pruebas se escribirá primero para cada comportamiento nuevo. No se ejecutarán operaciones de hardware ni pruebas destructivas.

## Validación y entregables

- Build Release de la solución y `git diff --check` tras cada cambio relevante.
- Tests offline para indexador, catálogo, resolución, estados, CLI, GPT y ViewModels donde el framework lo permita.
- `docs/PROGRESS.md` con fecha, tarea, estado y evidencia.
- `docs/UNLOCKTOOL_GAP_ANALYSIS.md` distinguiendo fuentes publicadas, referencias aportadas, inferencias y estado local.
- `docs/BUILD_REPORT.md` sólo con comandos realmente ejecutados, SDK, paquetes, warnings, tests y limitaciones.
- Comparar hashes de assets antes/después de builds; las rutas bajo `.gitignore` no se confunden con entregables rastreados.

## Dependencias y riesgos abiertos

- El portal de modelos y actualizaciones de UnlockTool respondió HTTP 403 en la consulta del 2026-10-20; esas características no se tratarán como verificadas.
- La solución mezcla `net8.0` en bibliotecas y `net10.0` en App/CLI/Services. La compatibilidad se evaluará antes de una migración global; no se cambiarán TFM como una limpieza cosmética.
- No se detectaron proyectos de pruebas y no hay hardware de laboratorio disponible. Esto limita cualquier declaración de validación real.
- Los assets ocupan aproximadamente 18,5 GB. El indexador debe ser incremental y streaming; la primera suma de hashes puede tardar.
- Las operaciones de cuenta/bypass existentes no pasan a ser capacidades permitidas por estar ya presentes en el código; quedarán fuera de línea de implementación y se marcarán como no disponibles.

## Aprobación del diseño

Esta propuesta conserva la arquitectura y los datos existentes; no reemplaza ni reestructura la solución. El trabajo de auditoría y el build baseline ya se pueden completar sin alterar el producto. Antes de iniciar cambios multi-módulo, se requiere confirmar este diseño adaptado al workspace.

## Estado de ejecución — 2026-10-20

- **A completada:** auditoría, baseline de build, conteo de assets e inventario SHA-256 no destructivo.
- **B/C implementadas parcialmente y validadas:** modelos y esquemas del catálogo, indexador offline, CLI de producto y validación del manifiesto. No se han asignado compatibilidades por inferencia; el manifiesto generado permanece local e ignorado.
- **Guardrails de UI adelantados como corrección segura:** carga inicial de catálogo/testpoints, sondeo de puertos, análisis local de scatter/paquetes y decodificación de preview fuera del hilo de interfaz. Las operaciones sin backend verificado se deniegan sin abrir transporte ni ejecutar callbacks; esto no reemplaza aún la máquina de estados compartida propuesta en D.
- **D-H pendientes:** motor compartido y tests de concurrencia/cancelación; backends ADB/Fastboot; integraciones protocolarias verificadas; GPT/planes/backup seguros; cobertura de GUI/CLI y empaquetado.

Los resultados ejecutados y las limitaciones se mantienen en [docs/PROGRESS.md](./PROGRESS.md) y [docs/BUILD_REPORT.md](./BUILD_REPORT.md).
