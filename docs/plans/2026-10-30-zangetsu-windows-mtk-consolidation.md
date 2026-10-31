# Diseño: consolidación Windows y preparación MTK

**Fecha:** 2026-10-30
**Tarea:** `TZS-2026-10-30-ZANGETSU-CONSOLIDATION`

## Objetivos

- Convertir `zangetsu` en la única raíz publicable del repositorio, conservando su
  historial Git y su remoto.
- Mantener una solución principal reutilizable (`src/`) y una suite MTK
  standalone (`mtk-suite/`) sin duplicar protocolos ni declarar capacidades que
  no estén verificadas.
- Integrar detección Windows de puertos COM/WMI y una ruta VCOM MTK de
  identificación limitada, manteniendo LibUsb opcional y fail-closed.
- Preparar contratos, estados, recursos y validaciones para futuras operaciones
  de lectura, DA/EMI/preloader, autenticación legítima y escritura controlada,
  sin activar todavía operaciones destructivas ni bypasses.
- Dejar documentación, scripts y metadatos suficientes para compilar y publicar
  el repositorio en GitHub sin incluir artefactos generados, assets privados ni
  DLLs nativas no verificadas.

## No objetivos

- No copiar automáticamente todo el contenido de la carpeta portable ni
  sobrescribir el historial o el remoto de `zangetsu`.
- No implementar algoritmos Kamakiri, Carbonara, Kamakiri2, LineCode u otros
  exploits, generación de payloads ni evasión de SLA/DAA/FRP/OEM.
- No instalar drivers, servicios, filtros UsbDk ni cambiar el controlador del
  dispositivo.
- No habilitar flash, erase, format, reparación GPT, bypass o autenticación
  ficticia por la existencia de botones, loaders o nombres de modelos.
- No distribuir `libusb-1.0.dll` de origen desconocido. La publicación documentará
  cómo proporcionar una DLL x64 oficial y cómo comprobar su arquitectura.

## Estado actual y evidencia

- `zangetsu` es un repositorio Git limpio en `main`, con remoto
  `https://github.com/DavidBGE-cmd/Tensa-Zangetsu-Portable.git`.
- La solución principal contiene bibliotecas `net8.0` y aplicaciones/servicios
  `net10.0`; no se cambiarán los TFM globalmente como limpieza cosmética.
- `mtk-suite` ya separa Core, Infrastructure, CLI y App, y documenta Windows x64,
  Zadig WinUSB/libusbK y LibUsbDotNet.
- El `MtkBackend` actual usa directamente LibUsb para `ReadInfo`; no contiene aún
  la ruta VCOM/COM consolidada ni el catálogo GUI de la copia portable.
- La copia portable local mostró un cierre nativo reproducible con una DLL
  `libusb-1.0.dll` no verificada; esa DLL no se tomará como dependencia válida.
- No existe evidencia de hardware conectado en esta fase. Todo backend físico se
  marcará `ImplementedUntested` hasta pruebas con transporte simulado y hardware
  autorizado.

## Secuencia de conexión que debe preservarse

1. Enumeración pasiva de COM/WMI y USB, sin abrir ni enviar comandos.
2. Selección explícita de un candidato único; rechazar ambigüedad.
3. `Connect / ReadInfo` explícito.
4. Probe BROM/VCOM acotado y lectura de identificación.
5. Invalidar y liberar la sesión después de error, cancelación o desconexión.
6. Futuras fases: resolver recursos verificados, autenticar con credenciales
   legítimas, cargar DA sólo con un plan y autorización, y reenumerar validando
   identidad física. No se implementan automáticamente en esta fase.

## Contratos y propiedad de recursos

- Los protocolos síncronos conservan I/O síncrono; la GUI/CLI sólo orquesta
  cancelación, timeout y tareas fuera del hilo de UI.
- `MtkConnectionResources` futuro deberá recibir DA, preloader, EMI y Auth con
  hashes, procedencia, límites y propiedad de streams explícita.
- Un recurso proporcionado por el llamador no se libera salvo que el contrato lo
  indique; streams abiertos por el protocolo sí se liberan.
- Toda sesión tendrá estado y generación; una vista o transporte de una sesión
  inválida no podrá reutilizarse tras error, cancelación o reenumeración.
- Las conversiones de longitudes, rangos, sectores y tamaños de XML se validarán
  antes de asignar o transmitir.

## Seguridad, rendimiento y logging

- LibUsb no se escaneará automáticamente hasta validar una DLL x64 oficial y
  compatible; la ruta COM/WMI no dependerá de cargar una DLL nativa.
- Los assets se indexan por streaming y hash; no se materializan imágenes
  completas en memoria.
- DA, preloader, EMI y Auth se muestran como `IndexedUnverified` hasta validar
  SoC, etapa, seguridad, tamaño, hash y procedencia.
- No se registran tokens, firmas, challenges, digests completos, Auth ni XML
  íntegros.
- Bypass y exploits se representan, si procede, mediante contratos explícitos
  `NotApplicable`/`Unavailable`, sin I/O ni payload.

## Alcance de archivos

1. `src/TensaZangetsu.Services/Mtk/`: backend, sesión, recursos y selección de
   transporte.
2. `src/TensaZangetsu.Services/Mtk/` y transportes serial/USB: adaptación VCOM
   acotada y diagnóstico nativo.
3. `src/TensaZangetsu.App/`: catálogo, selección, deduplicación, estados y
   mensajes honestos.
4. `mtk-suite/`: contratos compartidos, documentación y publicación Windows x64.
5. `Catalog/`, `Config/`, `.gitignore`, `docs/`: metadatos, scripts y reportes.
6. `.tests/` ignorado: fixtures de transporte simulado, catálogo, cancelación,
   ambigüedad y liberación de recursos.

## Integración de metadatos externos `UNLOCK`

La carpeta externa `UNLOCK` contiene perfiles útiles por fabricante/modelo,
pero también binarios, DLL/EXE, loaders cifrados y scripts de operaciones
sensibles. La integración aprobada se limita a generar un índice offline de
`model.json`, conservando ruta relativa y SHA-256 del perfil. El índice no
copia ni ejecuta binarios, no selecciona recursos automáticamente y mantiene
todos los perfiles como `IndexedUnverified`.

Los nombres de DA/preloader/loader del perfil sólo sirven como pistas para una
revisión posterior. La selección real deberá exigir HW_CODE, etapa, seguridad,
hash del recurso y evidencia de hardware. FRP, cuentas, desbloqueo y bypass
no forman parte de esta integración.

## Matriz de validación

| Área | Validación |
| --- | --- |
| Compilación | `dotnet build TensaZangetsu.slnx -c Release --no-restore` |
| Suite MTK | `dotnet build mtk-suite/TensaZangetsu.Mtk.slnx -c Release --no-restore` |
| Tests | Proyectos disponibles bajo `.tests`, siempre con `-c Release` |
| Formato Git | `git diff --check` |
| Publicación | `git status --short --ignored`, revisión de archivos rastreados |
| Hardware | Sólo después de compilar; `ReadInfo` explícito, sin DA/escritura |

## División de cambios

1. Auditoría y baseline documentado.
2. Detección Windows/VCOM y diagnóstico LibUsb.
3. Catálogo y contratos de recursos MTK.
4. Pruebas offline y estados de sesión.
5. Build, diff check, empaquetado y revisión final.

## Riesgos pendientes

- La compatibilidad real del protocolo VCOM depende del driver y del modo
  expuesto por el equipo; no se asumirá que todo `COMn` acepta BROM.
- La ruta LibUsb requiere una DLL oficial x64 y un driver compatible para la
  interfaz exacta; no se sustituirá el driver automáticamente.
- DA/EMI/Auth pueden exigir firmas o credenciales OEM legítimas; el framework
  debe devolver `AuthRequired` o `Unavailable` sin intentar evasión.
- Sin hardware real, el resultado máximo de esta fase será
  `ImplementedUntested`.
