# Progreso de implementación

## 2026-10-21 — `TZS-2026-10-21-PHASE-F2`

### Completed

- **Fase F (Paso 2): Adaptador de Protocolo Qualcomm (`QcomBackend`)** completado.
- Documento de diseño formal: [`docs/plans/2026-10-21-fase-f2-qcom-backend-design.md`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/docs/plans/2026-10-21-fase-f2-qcom-backend-design.md).
- Nuevo backend implementado en `src/TensaZangetsu.Services/Qcom/`:
  - [`QcomBackend.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/Qcom/QcomBackend.cs) — Implementa `IBackendProvider` para plataforma `"QCOM"`.
  - Orquesta `QcomProtocol.ProbeSahara()` sobre `ITransport`.
  - Soporta operaciones seguras de identificación: `ReadInfo`, `DeviceInfo`, `Connect`, `BootDevice`, `ProbeSahara`.
  - Mapea de forma estructurada información de silicio: `Serial Number` del SoC (uint64 en hex), `SBL Version`, `MsmId`, `SocHwVersion`, `OemId`, `ModelId` y `Root CA Hash` fingerprint.
  - Fail-closed estricto: operaciones destructivas (`WriteFlash`, `EraseFlash`, `FormatPartition`, `RawProgram`) y bypasses de seguridad (`ResetFrp`, `RemoveMiAccount`, `UnlockBootloader`) retornan inmediatamente `OperationSupportLevel.Unavailable` con 0 llamadas al transporte y 0 I/O enviada al dispositivo.
  - Soporta inyección de `QcomTransportFactory` para testing unitario desacoplado sin dependencias de hardware físico.
  - Resolución dual en producción: puerto COM directo (vía `SerialPortTransportFactory.Create`) y LibUsb (VID 0x05C6, PID 0x9008).
- `ApplicationEngine.cs` actualizado: registra `QcomBackend` por defecto junto a `SimulationBackend`, `AdbBackend`, `FastbootBackend` y `MtkBackend`.
- **Tests**: 53/53 pasando en `.tests/TensaZangetsu.Services.Tests` (41 previos + 12 nuevos casos en `QcomBackendTests.cs`):
  - Operaciones destructivas y bypasses: verificadas como `Unavailable` fail-closed sin abrir transporte.
  - Dispositivo no detectado en bus: fallo controlado con mensaje instructivo para conectar en modo EDL 9008 (TestPoint, cable EDL o fastboot oem edl).
  - `ReadInfo` exitoso: handshake Sahara completo simulado en `MockSaharaTransport` (SaharaHello request/response, ReadyResponse, Execute ReadSerialNum, ReadOemPkHash, ReadSblVersion, ReadMsmHwId), extrayendo serial y metadatos MSM.
  - Cancelación: `OperationCanceledException` durante el handshake devuelve `OperationState.Cancelled` y libera el transporte (`Dispose`).
  - Corrupción de Hello: paquete corrupto en detección de protocolo falla de forma controlada sin simular éxito.
  - Dispatch en `ApplicationEngine`: orquestación de `ReadInfo` para plataforma QCOM a través de la máquina de estados verificada.
- **Build Release solución completa**: 0 errores, 0 advertencias.

### Pending Risks

- `QcomBackend` se marca como `ImplementedUntested` para dispositivos físicos reales; validación con placa física Snapdragon en EDL 9008 pendiente para ascender a `Verified`.
- Flasheo Firehose (ELF/MBN programmer upload) y rawprogram/patch permanecen para una fase posterior cuando se configure el programador validado.

### Next

- **Fase F (Paso 3)**: Adaptar `SprdProtocolHostAdapter` a `IBackendProvider` (`SprdBackend`) con BSL sync e identificación BROM/FDL.

## 2026-10-21 — `TZS-2026-10-21-PHASE-F1`

### Completed

- **Fase F (Paso 1): Adaptador de Protocolo MediaTek (`MtkBackend`)** completado.
- Documento de diseño formal: [`docs/plans/2026-10-21-fase-f-mtk-backend-design.md`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/docs/plans/2026-10-21-fase-f-mtk-backend-design.md).
- Nuevo backend implementado en `src/TensaZangetsu.Services/Mtk/`:
  - [`MtkBackend.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/Mtk/MtkBackend.cs) — Implementa `IBackendProvider` para plataforma `"MTK"`.
  - Orquesta `MtkProtocol.Probe(ct)` mediante `IUsbTransport` determinista.
  - Soporta operaciones seguras de identificación: `ReadInfo`, `DeviceInfo`, `Connect`, `BootDevice`.
  - Mapea de forma estructurada información de hardware del chip: `HardwareCode`, `HardwareSubCode`, versiones HW/SW, etapa BROM/Preloader y Security Configuration flags (`SecureBoot`, `SLA`, `DAA`, `CertRequired`).
  - Utiliza `MtkChipCatalog` para asociar nombres comerciales (ej. MT6768 / Helio G85) a partir de los datos de silicio reales.
  - Fail-closed estricto: operaciones destructivas (`WriteFlash`, `EraseFlash`, `FormatPartition`) y bypasses de seguridad (`ResetFrp`, `RemoveMiAccount`, `UnlockBootloader`, `SlaBypass`) retornan inmediatamente `OperationSupportLevel.Unavailable` con 0 llamadas a transporte USB y 0 I/O enviada al dispositivo.
  - Soporta inyección de `MtkTransportFactory` para testing unitario desacoplado sin dependencias de hardware físico.
- `ApplicationEngine.cs` actualizado: registra `MtkBackend` por defecto junto a `SimulationBackend`, `AdbBackend` y `FastbootBackend`.
- **Tests**: 41/41 pasando en `.tests/TensaZangetsu.Services.Tests` (29 previos + 12 nuevos casos en `MtkBackendTests.cs`):
  - Operaciones destructivas y bypasses (`ResetFrp`, `RemoveMiAccount`, `UnlockBootloader`, `WriteFlash`, `EraseFlash`, `FormatPartition`, `SlaBypass`): todas verificadas como `Unavailable` fail-closed sin abrir transporte.
  - Dispositivo no detectado en bus USB: fallo controlado con mensaje instructivo para conectar en modo BROM (Vol+ y Vol-).
  - `ReadInfo` exitoso: handshake BROM completo simulado en `MockBromUsbTransport` (A0, 0A, 50, 05, GetHardwareCode 0xFD, DisableWatchdog Write32 0xD4 con secuencia de eco BE, GetTargetConfiguration 0xD8, GetBootLoaderVersion 0xFE, GetBromVersion 0xFF, GetHardwareSoftwareVersion 0xFC) con chip MT6768 identificado.
  - Cancelación: `OperationCanceledException` durante el handshake devuelve `OperationState.Cancelled` y libera el transporte (`Dispose`).
  - Corrupción de handshake: respuesta inesperada en el handshake inicial falla de forma controlada sin simular éxito.
  - Dispatch en `ApplicationEngine`: orquestación de `ReadInfo` para plataforma MTK a través de la máquina de estados verificada.
- **Build Release solución completa**: 0 errores, 0 advertencias.

### Pending Risks

- `MtkBackend` se marca como `ImplementedUntested` para operaciones de hardware real; validación con placa física MTK pendiente para ascender a `Verified`.
- Falta implementar DA download y `ReadGpt` para MTK dentro de `MtkBackend` cuando el usuario cargue un archivo DA.bin válido.

### Next

- **Fase F (Paso 2)**: Adaptar `QcomProtocolHostAdapter` a `IBackendProvider` (`QcomBackend`) con Sahara Hello e identificación BROM/EDL 9008, cubierto por tests de transporte mock.
- **Fase F (Paso 3)**: Adaptar `SprdProtocolHostAdapter` a `IBackendProvider` (`SprdBackend`) con BSL sync.

## 2026-10-21 — `TZS-2026-10-21-PHASE-E`

### Completed

- **Fase E: Integración ADB/Fastboot de solo lectura** completada.
- Nuevos archivos creados en `src/TensaZangetsu.Services/AdbFastboot/`:
  - [`IProcessRunner.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/AdbFastboot/IProcessRunner.cs) — Abstracción de proceso con `IReadOnlyList<string>` arguments (sin shell injection).
  - [`SystemProcessRunner.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/AdbFastboot/SystemProcessRunner.cs) — Implementación real: `ArgumentList`, draining concurrente de stdout/stderr, kill de árbol de procesos en cancelación/timeout.
  - [`AdbFastbootOptions.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/AdbFastboot/AdbFastbootOptions.cs) — Configuración centralizada de rutas; si `AdbPath`/`FastbootPath` está configurado, es autoritativo (sin fallback).
  - [`AdbBackend.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/AdbFastboot/AdbBackend.cs) — `IBackendProvider` ADB de solo lectura: `ReadInfo` y `PartitionList`. Operaciones destructivas/EDL → `Unavailable` inmediato.
  - [`FastbootBackend.cs`](file:///e:/rayitasgg/Documents/Tensa-Zangetsu-Portable/src/TensaZangetsu.Services/AdbFastboot/FastbootBackend.cs) — `IBackendProvider` Fastboot de solo lectura: `ReadInfo` (getvar all) y `BootloaderStatus` (getvar unlocked). Flash/erase/oem edl → `Unavailable` inmediato.
- `AdbFastbootService.cs` actualizado: usa `IProcessRunner` vía inyección; `IsMonitoring` restaurado correctamente.
- `ApplicationEngine.cs`: registra `AdbBackend` y `FastbootBackend` por defecto junto a `SimulationBackend`.
- `MainViewModel.cs`: `AvailableBases` incluye `"ADB"` y `"Fastboot"`; switch de plataforma mapea a `"ADB"` / `"FASTBOOT"`.
- `Config/appsettings.example.json`: plantilla de configuración de rutas a herramientas externas.
- **Tests**: 29/29 pasando en `.tests/TensaZangetsu.Services.Tests` (26 previos + 9 nuevos en `AdbFastbootBackendTests.cs`):
  - Tool not found → `Unavailable` + 0 comandos ejecutados (fail-closed verificado con directorio aislado).
  - `ReadInfo` ADB: parseo de serial, modelo, versión Android; `unauthorized` → `AuthRequired`.
  - `PartitionList` ADB: parseo de `/proc/partitions` con 5 entradas.
  - Fastboot `ReadInfo`: parseo de `getvar all` con salida `(bootloader)`.
  - Fastboot `BootloaderStatus`: detección de estado unlocked/locked.
  - Operaciones peligrosas (AdbToEdl, FastbootToEdl, Flash, ResetFrp, WriteFlash): 9 combinaciones → `Unavailable` + 0 I/O.
  - `ApplicationEngine` dispatch: registra backend ADB y ejecuta `ReadInfo` correctamente.
- **Build Release solución completa**: 0 errores, 0 advertencias.

### Pending Risks

- Backends ADB/Fastboot son `ImplementedUntested` — validación en hardware real pendiente para subir a `Verified`.
- `AdbFastbootService` (servicio legacy) sigue usando `SplitCommandLineArguments` para convertir string → lista; las operaciones con espacios en rutas de archivo se tokenizarán incorrectamente. No es bloqueante para Fase E (solo lectura).

### Next

- **Fase F**: Adaptar `MtkProtocolHostAdapter`, `QcomProtocolHostAdapter` y `SprdProtocolHostAdapter` a `IBackendProvider` en `Services`.
- Comenzar con MTK `ReadInfo` (handshake BROM confirmado en referencia); registrar en `ApplicationEngine`.

## 2026-10-20 — `TZS-2026-10-20-AUDIT`

### Completed

- Inspeccionada la solución existente: 31 proyectos, solución `TensaZangetsu.slnx`.
- Confirmado SDK .NET `10.0.402` en Windows 11 build 26100.
- Build baseline Release con `--no-restore`: correcto; 0 advertencias, 0 errores.
- Cuantificados los assets principales sin ejecutar ni modificar originales: 21.785 archivos en `ExternalAssets`, 201 en `TESTPOINTS`, 9.377 en `src/TensaZangetsu.App/Assets` y 3 en `src/TensaZangetsu.Services/Assets`.
- Inventario streaming SHA-256 completado: 31.366 archivos y 0 errores; salida local ignorada `artifacts/initial-assets-audit-20261020.csv`.
- Consultadas las páginas oficiales de Avalonia y permisos de Antigravity. Las páginas de modelos y descargas de UnlockTool respondieron HTTP 403.
- Redactados auditoría inicial, propuesta adaptada al repositorio y matriz de brechas con estados conservadores.

### InProgress

- Fase D: motor/estados compartidos y pruebas de UI; aún no implementados.

### Blocked

- No se encontró un proyecto de pruebas rastreado; existe un proyecto local ignorado para el indexador y se ejecutó por ruta directa.
- No hay evidencia de dispositivos físicos ni de validación real de protocolos en esta sesión.
- Fuentes oficiales de UnlockTool inaccesibles (HTTP 403); no se afirmarán capacidades por modelo a partir de esa web.

### Next

1. Terminar y validar el indexador/catalogación con fixtures locales.
2. Continuar con motor, cancelación y simulación separados de hardware.
3. Integrar operaciones sólo lectura verificables; documentar explícitamente lo que requiera autorización, herramientas o hardware.

### Riesgos

- La solución mezcla bibliotecas `net8.0` y aplicaciones/servicios `net10.0`; cualquier migración debe justificarse y verificarse.
- Un manifiesto raíz preexistente está ignorado por Git y tiene formato previo. No se sobrescribe.
- Las funciones que necesitan autorización OEM, herramientas externas, drivers o hardware permanecerán no disponibles hasta satisfacer sus requisitos.

## 2026-10-20 — `TZS-2026-10-20-UI-GUARDRAILS`

### Completed

- Integrados el catálogo, los esquemas JSON y los comandos de indexación/validación offline descritos arriba.
- La carga inicial de modelos y testpoints y el sondeo de puertos ahora se ejecutan en segundo plano; la GUI no declara conectado un dispositivo por encontrar un puerto COM.
- La lectura de metadatos scatter/firmware se ejecuta fuera del hilo UI y descarta resultados de cargas canceladas o sustituidas.
- Los selectores de modelo dejaron de resolver loaders por heurística de nombres y de asociar automáticamente diagramas de testpoint; los perfiles e imágenes se marcan expresamente como no verificados.
- Los controles de hardware se muestran deshabilitados y el panel de imágenes ya no ofrece instrucciones para puentes físicos basados en referencias no verificadas.
- Se retiraron mensajes promocionales y éxitos simulados de la inicialización. Las operaciones GUI sin backend verificado informan `Unavailable`; las rutas Fastboot/ADB a EDL, bypass, GPT repair y reboot no envían comandos.
- El manifiesto, los assets originales y los binarios no fueron alterados en esta fase.

### Validation

- `dotnet build TensaZangetsu.slnx -c Release --no-restore`: correcto, 0 advertencias y 0 errores.
- `dotnet test .tests\TensaZangetsu.AssetIndexing.Tests\TensaZangetsu.AssetIndexing.Tests.csproj -c Release --no-restore`: 6/6 pruebas correctas.
- Indexación y validación completa de `ExternalAssets`, `TESTPOINTS`, assets de App y Services: 31.369 entradas, 0 fallos, 7 advertencias conservadoras XML, 7.094 grupos de duplicados, 0 bundles explícitos; el manifiesto local es válido.
- Comparación del manifiesto contra `artifacts/initial-assets-audit-20261020.csv`: 31.366 rutas previas preservadas y 0 cambios SHA-256; aparecen 3 rutas adicionales no incluidas en la auditoría inicial.
- `git diff --check`: correcto.
- No se ha ejecutado ninguna operación con hardware real; ningún modelo se considera probado.

### Risks and next steps

- Los controles de operación existen en la GUI, pero el gate central deniega su ejecución hasta que haya un backend validado; el motor compartido con estados/cancelación aún está pendiente.
- Los cuerpos heredados de callbacks permanecen detrás del gate vacío y no se ejecutan. No se debe registrar ningún backend en ese gate antes de reemplazarlos por implementaciones verificables y pruebas específicas.
- La prueba del indexador no prueba la GUI, los protocolos ni la compatibilidad de modelos.
- `Catalog/AssetsManifest.json` es un artefacto local de 37.525.237 bytes y permanece ignorado por Git. Los 3 archivos extra observados y las 7 advertencias XML requieren revisar su procedencia/clasificación antes de atribuirles función.
- Mantener las operaciones de escritura, borrado, bypass y cuentas no disponibles. Continuar con pruebas del gate y del estado de UI antes de integrar cualquier backend.

## 2026-10-21 — `TZS-2026-10-21-PHASE-D`

### Completed

- **D.1 Máquina de estados y contratos del motor compartido**:
  - Implementado `OperationState.cs` con todos los estados del ciclo de vida (`Idle`, `WaitingForDevice`, `CandidateDetected`, `AcquiringTransport`, `Handshaking`, `Identifying`, `ResolvingLoader`, `Authorizing`, `LoadingAgent`, `Ready`, `Planning`, `AwaitingConfirmation`, `Executing`, `Verifying`, `Completed`, `Cancelled`, `TimedOut`, `Disconnected`, `Unsupported`, `AuthRequired`, `RecoveryRequired`, `Failed`) y extensiones de guardas (`IsTerminal`, `HasActiveSession`, `IsSafelyCancellable`).
  - Implementado `OperationStateMachine.cs` con transiciones algebraicas estrictas, soporte de guards, recuperación de estados terminales y eventos `StateChanged`.
  - Diseñado `IBackendProvider.cs` con `OperationSupportLevel` (`Unavailable`, `ReferenceOnly`, `IndexedUnverified`, `ImplementedUntested`, `Verified`), `OperationRequest`, `OperationResult` e `IOperationProgressReporter`.
  - Implementado `ApplicationEngine.cs` como orquestador compartido para hosts GUI y CLI, administrando la compuerta de ejecución serializada (`SemaphoreSlim`), cancelación limpia, enlace con `DevicePortTrapService` y despacho a backends.

- **D.2 Desacoplamiento estricto de la simulación del hardware**:
  - Implementado `SimulationBackend.cs` como backend aislado: `IsSimulation = true`, prefijo mandatorio `[SIMULATION]` en absolutamente todos los mensajes de log y progreso emitidos.
  - Eliminados de la producción todos los valores ficticios: retirado MEID inventado (`0xDC8E9AB41B672449A5B6A58C14D94295`), códigos HW hardcodeados, lista estática de 19 particiones ficticias y registros fingidos de "OK".
  - Política Fail-Closed: Si una operación se ejecuta en modo físico sin un backend de hardware verificado registrado en el motor, devuelve inmediatamente `OperationResult.MakeUnavailable` y registra `Unavailable: <Op>. No hardware backend is registered for platform '<Platform>'. No transport was opened and no device command was sent.`, sin demoras fingidas ni apertura de puertos.

- **D.3 Conexión de la GUI (MainViewModel) al motor compartido**:
  - `MainViewModel.cs` refactorizado para instanciar y suscribirse a `ApplicationEngine` (`StateChanged`, `LogEmitted`, `ProgressUpdated`, `DeviceChanged`) despachando a la UI mediante `PostToUi`.
  - Todas las operaciones de servicio (`ReadInfo`, `ResetFrp`, `FactoryReset`, `UnlockBootloader`, `ReadHardwareKeys`, `RemoveMiAccount`, `DisableOta`, `RemoveHuaweiId`, `PythonBypass`, `Rpmb`, `OemInfo`, `LkPatch`, `ReadFlash`, `WriteFlash`, `EraseFlash`, `FastbootToEdl`, `AdbToEdl`, `AmonetGptFix`) ahora delegan en `ApplicationEngine.ExecuteOperationAsync(request, ct)`.
  - Selector de loaders actualizado para incluir explícitamente `"Demo / Emulación Offline"`; cuando se selecciona, se activa el modo emulado etiquetado.
  - Al cambiar de modelo en la interfaz, se invalida y resetea la sesión en el motor y se vacían las listas de particiones previas.
  - `MainWindow.axaml` actualizado: botones enlazados a `IsEnabled="{Binding !IsBusy}"` para interactuar fluidamente con el motor sin bloquear el hilo de interfaz.

### Validation

- `dotnet build TensaZangetsu.slnx -c Release`: 33 proyectos compilan con 0 advertencias y 0 errores.
- `dotnet test .tests/TensaZangetsu.Services.Tests/TensaZangetsu.Services.Tests.csproj -c Release`: 12/12 pruebas pasadas (máquina de estados, ciclo de vida, transiciones inválidas, desconexión inesperada, guardas de cancelación y orquestación del motor).
- `dotnet test .tests/TensaZangetsu.App.Tests/TensaZangetsu.App.Tests.csproj -c Release`: 4/4 pruebas pasadas (rechazo fail-closed sin backend en hardware, modo demo etiquetado `[SIMULATION]`, invalidación por cambio de modelo y cancelación reactiva).
- `dotnet test .tests/TensaZangetsu.AssetIndexing.Tests/TensaZangetsu.AssetIndexing.Tests.csproj -c Release`: 6/6 pruebas pasadas.
- Total cobertura verificada: 22/22 pruebas unitarias e integradas exitosas.
- `git diff --check`: 0 errores.
- `.tests/` preservado en `.gitignore` sin rastreo en Git.

### Risks and next steps

- **Siguiente fase (Fase E)**: Integración del backend `AdbFastbootBackend` de solo lectura (`adb devices -l`, `fastboot getvar all`) con `ArgumentList` sin shell y timeout acotado.
- **Fase F**: Adaptar los módulos de protocolo existentes de la CLI (`MtkProtocolHostAdapter`, `QcomProtocolHostAdapter`, `SprdProtocolHostAdapter`) a implementaciones de `IBackendProvider` compartidas en `TensaZangetsu.Services`.
- **Riesgo**: Sin hardware físico conectado, las operaciones permanecen conservadoramente en `Unavailable` hasta que se conecte un dispositivo real y se confirme el comportamiento del protocolo en el transporte.

