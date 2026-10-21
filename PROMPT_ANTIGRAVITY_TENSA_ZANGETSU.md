# Prompt maestro para Antigravity: Tensa-Zangetsu Pro Suite

## 0. Encargo y reglas de ejecución

Actúa como arquitecto principal e ingeniero de implementación de una suite de mantenimiento, diagnóstico y recuperación de firmware para dispositivos móviles propios o recibidos con autorización del titular. Implementa código en el workspace; no te limites a explicar una propuesta.

Objetivo: construir o refactorizar Tensa-Zangetsu Pro Suite con C#, .NET 10, Avalonia UI y MVVM. Debe tener GUI y CLI verdaderos, compartir el mismo motor asíncrono, preservar los assets originales y registrar de forma verificable qué operaciones funcionan realmente.

La referencia funcional es UnlockTool, pero no se solicita copiar su código, sus binarios, su marca, sus servidores, licencias ni materiales propietarios. Implementa una interfaz original con estética Matrix Cyber Dark. Modelos anunciados por terceros no equivalen a modelos soportados por esta aplicación.

No intentes eludir permisos, políticas del IDE, firmas, licencias, controles de acceso de servidores ni restricciones del sistema. No cambies el nombre de una operación para ocultar su comportamiento. Para operaciones de servicio que requieran autenticación OEM, usa mecanismos documentados, credenciales autorizadas y adaptadores legítimos. Si falta un backend, mantenlo explícitamente como no disponible y continúa implementando las partes independientes.

No prometas cero errores antes de compilar. No inventes protocolos, identificadores USB, asociaciones de loaders ni paquetes NuGet. No presentes simulaciones como hardware real. Una dependencia inaccesible no debe paralizar toda la solución: documenta el bloqueo concreto y continúa donde sea posible.

No solicites confirmación para editar código ordinario dentro del workspace. Sí requiere aprobación para instalaciones privilegiadas, cambios de drivers o registro, borrado irreversible, descarga de ejecutables, envío de datos o pruebas destructivas sobre dispositivos. No ejecutes estas acciones como efecto lateral del build ni de los tests.

Decisión de arquitectura ya tomada: observación pasiva en background desde el arranque; los botones y la CLI encolan una operación explícita. Nunca bloquear el hilo de UI. Inicialmente admite una operación de dispositivo a la vez, sin mezclar sesiones.

## 1. Primera fase: inspección sin modificar assets

Antes de generar proyectos:

1. Lee las reglas del repositorio y localiza soluciones .sln/.slnx, csproj, global.json y archivos de paquetes.
2. Inspecciona git status y conserva cambios existentes. No uses reset --hard, clean -fd ni borrados generales.
3. Ejecuta dotnet --info y dotnet --list-sdks. Comprueba SDK .NET 10. Si no está instalado, documenta el requisito y solicita aprobación para instalarlo; no cambies silenciosamente el target a otra versión.
4. Examina la estructura real de Assets y TESTPOINTS. No presupongas las rutas del ejemplo. Admite un directorio externo de assets configurado por el usuario.
5. Genera un inventario con ruta relativa, longitud y SHA-256 de cada asset. En bibliotecas grandes usa streaming, cancelación y avance. Registra errores de lectura por archivo.
6. Escribe docs/INITIAL_AUDIT.md con proyectos existentes, sistema operativo, SDK, dependencias, assets encontrados y riesgos.
7. Escribe docs/IMPLEMENTATION_PLAN.md con fases pequeñas y criterios de aceptación. A continuación implementa las fases no bloqueadas.

No renombres, muevas, sobrescribas, ejecutes ni descomprimas sobre los originales para deducir metadatos.

## 2. Investigación verificable y comparación con UnlockTool

Consulta primero estas fuentes públicas oficiales:

- https://unlocktool.net/models
- https://unlocktool.net/download
- https://docs.avaloniaui.net/docs/get-started/install-avalonia
- https://antigravity.google/docs/permissions/

La lista oficial de UnlockTool relaciona modelo, nombre de dispositivo, SoC y funciones. Sus notas muestran variaciones por modelo, revisión BIT, seguridad y modo de conexión. No asumas un número fijo de modelos ni diez módulos oficiales inmutables. La organización de módulos descrita abajo es nuestro diseño.

Crea docs/UNLOCKTOOL_GAP_ANALYSIS.md. Para cada función documenta: fuente, fecha de consulta, modelos o restricciones publicados, necesidad para nuestra suite, backend disponible, estado y prueba pendiente. Distingue hechos publicados, inferencias y objetivos de implementación.

No descargues versiones modificadas o cracks. No extraigas loaders del ejecutable de UnlockTool. No copies imágenes de testpoints sin permiso. No automatices acceso a áreas autenticadas ni hagas scraping masivo sin comprobar las condiciones del sitio. Si no se permite la importación de datos, usa importación manual de CSV/JSON autorizado.

Como referencia comprobable, la lista oficial contiene entradas como Xiaomi Mi 6 / Sagit / Snapdragon 835, Oppo A37fw / Neo 9 / Snapdragon 410 y Huawei Enjoy 6S / DIG-TL10 / Snapdragon 435. Puedes incluirlas como referencias externas con estado ReferenceOnly y fuente; no como soporte local verificado ni como asociación automática de un loader.

## 3. Estructura propuesta y dependencias

Adapta esta estructura si ya existe una solución compatible; evita duplicar proyectos:

```text
TensaZangetsuSuite/
  TensaZangetsu.slnx
  global.json
  Directory.Build.props
  Directory.Packages.props
  NuGet.config
  README.md
  .gitignore
  Assets/                         # binarios e imágenes originales
  Catalog/
    devices.json
    overrides.json
    AssetsManifest.schema.json
    asset-metadata.schema.json
    AssetsManifest.json           # generado; no editar a mano
  Config/
    appsettings.example.json
  src/
    TensaZangetsu.Core/
    TensaZangetsu.Infrastructure/
    TensaZangetsu.App/
    TensaZangetsu.Cli/
  scratch/
    AssetIndexer/
      AssetIndexer.csproj
      Program.cs
  tests/
    TensaZangetsu.Core.Tests/
    TensaZangetsu.Infrastructure.Tests/
    TensaZangetsu.App.Tests/
  scripts/
  docs/
```

Core define modelos, contratos, estados, validaciones y planificación; no referencia Avalonia ni APIs específicas de Windows. Infrastructure implementa filesystem, procesos, puertos, USB y backends. App y Cli referencian Core e Infrastructure; no se referencian entre sí. El indexador reutiliza modelos/esquemas sin depender de la GUI.

Verifica versiones estables compatibles de Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Controls.DataGrid y CommunityToolkit.Mvvm. Mantén la misma versión entre paquetes Avalonia. Verifica APIs contra documentación de la versión elegida: no copies estilos o efectos de WPF suponiendo compatibilidad. Usa Microsoft.Extensions.DependencyInjection/Logging/Configuration solo donde aporten valor. System.IO.Ports requiere verificación por plataforma y no sustituye una biblioteca USB.

Fija versiones exactas validadas, no comodines. Configura net10.0, Nullable enable, ImplicitUsings enable y análisis estático. Usa global.json con una versión de SDK realmente instalada y política explícita de roll-forward. Genera lockfiles y comprueba restore reproducible. No suprimas advertencias globalmente para aparentar limpieza.

Windows es el primer destino de integración hardware. La UI y Core deben ser portables. Linux/macOS solo se anuncian para transportes realmente disponibles y comprobados. Un backend Windows no debe ejecutarse por accidente en otra plataforma.

## 4. Catálogo de dispositivos separado del manifiesto físico

Implementa como mínimo:

- DeviceCatalogEntry: id estable, brand, marketingName, modelCodes[], codeNames[], platform, soc, boardRevisions[], storageTypes[], connectionModes[], sourceReferences[], verificationStatus.
- OperationCapability: operationId, implementationStatus, compatibleModes, securityConstraints, firmwareConstraints, backendId, evidence, lastVerifiedAt.
- MtkDaVariant: id, displayName, assetId, protocolFamily, compatibleDeviceIds[], socConstraints, bitConstraint, androidConstraint, hyperOsConstraint, storageConstraint, provenance, verificationStatus.
- AssetEntry: id estable, relativePath, kind, length, sha256, metadataSource, confidence, validationStatus y referencias a perfiles.
- LoaderBundle: relación explícita entre loader, auxiliares y modelos compatibles.
- PartitionEntry: nombre, GUID cuando corresponda, LUN, inicio, longitud, tamaño de sector, atributos y flags de protección.
- OperationRequest, OperationPlan, OperationResult, DeviceIdentity, SessionSnapshot y LogEvent.

Usa estados distintos: ReferenceOnly, IndexedUnverified, ImplementedUntested, Verified, Unavailable. Una operación anunciada por UnlockTool no cambia automáticamente ninguno de estos estados.

Un archivo físico no demuestra compatibilidad. Un nombre de modelo no identifica de forma suficiente una placa. Los valores desconocidos deben permanecer null/Unknown; no inventes Android, BIT, SoC ni seguridad.

## 5. AssetIndexer: indexación fiable y no destructiva

Implementa una CLI con esta interfaz prevista y documenta exactamente cualquier ajuste:

```bash
dotnet run --project scratch/AssetIndexer -- --assets ./Assets --catalog ./Catalog/devices.json --overrides ./Catalog/overrides.json --output ./Catalog/AssetsManifest.json --validate
```

Requisitos:

1. Recorrido recursivo y seguro con rutas relativas normalizadas y manejo explícito de mayúsculas según el filesystem.
2. Ignorar enlaces simbólicos por defecto; impedir salir del directorio autorizado. Evitar bucles, rutas absolutas externas y traversal mediante ../.
3. Clasificar .mtk, .bin, .elf, .mbn, .elt, .txt, .xml, .pac, .pit, .tar, .md5, .jpg, .jpeg y .png solo cuando exista evidencia suficiente. .elt desconocido se conserva como Unknown; no lo equipares a .elf.
4. No asumir que todo .bin es DA ni que todo .xml es un protocolo MTK V6. Distinguir scatter, rawprogram y patch por estructura y contexto. Desactivar DTD y resolución externa al leer XML.
5. Detectar duplicados por hash conservando todas sus ubicaciones; no eliminar nada.
6. Metadatos con precedencia: override explícito validado, sidecar validado, cabecera interpretada por parser probado, inferencia de ruta/nombre. Registrar conflictos en lugar de ocultarlos.
7. Los sidecars usan el nombre completo del archivo más .metadata.json. Validar contra esquema y registrar procedencia.
8. Mantener relaciones DA/Preloader/EMI y FDL1/FDL2 por bundle explícito. No emparejar por proximidad ni por similitud casual.
9. Unificar el índice de preloaders bajo la sección UNIFIED_PRELOADERS_DB sin concatenar ni modificar los binarios.
10. Indexar testpoints por modelo y revisión de placa explícitos. Una imagen no es una instrucción automática de puenteo.
11. Para imágenes grandes usar miniaturas derivadas en caché, nunca reescribir originales.
12. Manifest con schemaVersion, fecha, raíz lógica configurable, assets, bundles, referencias y diagnósticos. No guardar una única ruta absoluta que rompa la portabilidad.
13. Orden estable para diffs, IDs estables y hash de contenido separados de fechas de generación. Escribir a temporal y reemplazar de forma atómica; nunca dejar un JSON truncado como manifiesto activo.
14. Indexación incremental mediante tamaño/fecha como pista y SHA-256 como evidencia cuando sea necesario; ofrecer --rehash completo.
15. Cancelación, errores por archivo, conteos y código de salida documentado. Un manifiesto inválido no sustituye al último válido.

Para firmware con rawprogram/patch, conserva la estructura relativa y valida referencias. No extraigas automáticamente grandes paquetes ni ejecutes scripts de firmware. Protege cualquier futura extracción contra zip-slip/tar traversal y límites de tamaño.

## 6. AutoLoaderResolverService y selector inteligente

El resolvedor recibe identidad observada, modo, operación y selección manual. Filtra por compatibilidad demostrada de modelo/placa, HW ID, SoC, protocolo, almacenamiento, revisión BIT y restricciones de seguridad o firmware.

No uses un simple ranking para convertir incompatibilidades en coincidencias. Primero aplica restricciones obligatorias; después ordena candidatos compatibles por especificidad y evidencia. Si no hay evidencia suficiente, devuelve Ambiguous o Unsupported y explica por qué. No cargues el primer binario disponible.

Una selección manual no omite validaciones. El fallback automático solo está permitido entre variantes explícitamente compatibles, antes de cualquier escritura y sin repetir cargas sobre una sesión de estado desconocido.

El ComboBox de variantes debe mostrar opciones como [BIT 5 - SoC verificado], [HyperOS 1.0 - perfil documentado], [BROM - protocolo validado], únicamente si esos metadatos existen. Nunca inventes etiquetas a partir de un ejemplo del prompt. Auto siempre visible; elección manual habilitada cuando haya candidatos. Cambiar modelo invalida la selección anterior y cualquier plan pendiente.

La presencia de un loader no implica que pueda superar autenticación del dispositivo. Si se necesita autorización OEM, informa AuthRequired y solicita el mecanismo autorizado configurado, sin incorporar exploits ni suplantar una autorización.

## 7. Motor asíncrono compartido por GUI y CLI

Implementa estos estados con transiciones comprobables:

Idle -> WaitingForDevice -> CandidateDetected -> AcquiringTransport -> Handshaking -> Identifying -> ResolvingLoader -> Authorizing -> LoadingAgent -> Ready -> Planning -> AwaitingConfirmation -> Executing -> Verifying -> Completed -> Ready.

Incluye Cancelled, TimedOut, Disconnected, Unsupported, AuthRequired, RecoveryRequired y Failed. Saltar fases es válido solo cuando el protocolo no las requiere, por ejemplo lectura ADB sin DA.

La observación al arrancar es pasiva: enumera y recibe eventos sin abrir cada dispositivo ni enviar comandos. Solo una solicitud explícita adquiere el transporte y empieza un handshake documentado.

Requisitos de comunicación:

- IDeviceDiscovery y adaptadores de enumeración por plataforma.
- IDeviceTransport e IDeviceSession para serie, USB bulk, ADB y Fastboot según corresponda.
- No todos los dispositivos exponen COM: Samsung Download, ADB/Fastboot y USB bulk deben tener su propio transporte.
- COM por sí solo no identifica MTK, Qualcomm o UNISOC. Usa descriptores, interfaces e identidad obtenida mediante protocolos válidos.
- Capturar puerto significa adquirir una sesión exclusiva cuando el backend y el SO lo permiten, no congelar físicamente el bootloader ni garantizar que no habrá timeout.
- VID/PID y hwcodes provienen de documentación o evidencia real, nunca de valores inventados.
- Apertura/cierre asíncronos donde el backend lo admita; adapta APIs bloqueantes sin inmovilizar el Dispatcher.
- Timeouts de adquisición, handshake, carga, lectura y escritura por separado; CancellationToken hasta la capa más baja que permita cancelación.
- Manejar lecturas y escrituras parciales, framing, checksum, endianess y desconexión. Nunca asumir que una lectura devuelve un paquete completo.
- Eventos de conexión con debounce y una nueva enumeración tras cambios de modo. Una reenumeración no equivale a autorizar otro teléfono.
- Device fingerprint y session ID para no reanudar una escritura sobre un dispositivo distinto.
- Exclusión con SemaphoreSlim y, cuando corresponda, bloqueo entre procesos para impedir que GUI y CLI controlen el mismo dispositivo simultáneamente.
- Evitar doble clic, reentrancia, carreras y tareas fire-and-forget sin supervisión.
- Reintentos limitados solo para fases idempotentes. No repetir automáticamente un erase/write tras un timeout de resultado incierto.
- Cancelación durante escritura se atiende en un punto seguro del protocolo. Si no puede interrumpirse sin riesgo, mostrar CancelRequested y no afirmar que el dispositivo ya está detenido.
- Ready conserva la sesión únicamente si el protocolo lo permite. Si termina o el equipo se desconecta, actualizar estado real, sin fingir conexión persistente.

CONNECT / READ INFO nunca envía WDT, reboot, erase o write como efecto lateral. REBOOT TO SYSTEM es una operación independiente. Tampoco reinicies al finalizar automáticamente un dump o flash salvo opción explícita, compatible y confirmada.

## 8. Backends y alcance real de los servicios

Implementa MtkService, QualcommService, UnisocService, SamsungService, XiaomiService y AppleDiagService como adaptadores del motor, no como fachadas que siempre devuelven éxito.

Una implementación funcional requiere protocolo documentado y probado o integración con una herramienta legítima y mantenida. Si utilizas una herramienta externa, documenta versión, origen, hash, licencia, plataforma, argumentos y exit codes. No instales ni descargues ejecutables sin aprobación. No redistribuyas componentes cuya licencia no lo permita.

Usa ProcessStartInfo.ArgumentList sin shell para evitar inyección; permite rutas con espacios. Consume stdout y stderr concurrentemente; soporta cancelación y evita procesos huérfanos. No guardes credenciales en argumentos, logs ni repositorio. Un exit code cero no basta para declarar escritura verificada si el backend no la comprobó.

Admite ADB/Fastboot oficiales configurados por ruta y un directorio Tools de componentes proporcionados por el usuario. Si la API o herramienta no está disponible, devuelve un resultado tipado Unavailable con la dependencia exacta, no NotImplementedException al hacer clic ni un éxito simulado.

La simulación es un backend separado, con indicador SIMULATION permanente, sin acceso de escritura al hardware y fixtures ficticios claramente identificados.

## 9. Cobertura funcional inspirada en UnlockTool

Conserva cinco áreas principales: Conexión & Boot, Servicios & Seguridad, Particiones & Firmware, Ayuda & TestPoints y Ajustes & Engine. Dentro usa submódulos por plataforma. Evita duplicar la misma operación en múltiples servicios.

### 9.1 MediaTek

Perfiles BROM, Preloader y META cuando exista backend. Lectura de identificación; selector DA; asociación Preloader/EMI; validación scatter; GPT; readback; flash con plan; backup y restauración NV del mismo dispositivo; factory reset documentado; estado de bootloader y procedimiento OEM soportado; información de almacenamiento y diagnóstico de salud cuando el protocolo lo exponga.

No inventes soporte MTK V5/V6 a partir del nombre del archivo. Operaciones RPMB se muestran solo si hay soporte real; no habilites escrituras o borrados genéricos sobre almacenamiento protegido. No incluyas un bypass SLA/DAA ficticio.

### 9.2 Qualcomm

Distingue Sahara de Firehose; loader explícito; validación rawprogram/patch; eMMC/UFS y LUN según respuesta real del dispositivo; lectura de particiones, dump, flash y verificación; EFS/QCN mediante backend apropiado y vínculo al mismo dispositivo; slot A/B cuando sea soportado; DIAG independiente de EDL.

No fijes tamaño de sector basándote únicamente en eMMC o UFS. No anuncies que cualquier Firehose sirve para cualquier placa.

### 9.3 UNISOC / Spreadtrum

Selección de bundles FDL1/FDL2, restricciones de firma/versión y perfiles compatibles. Inspección PAC solo mediante parser validado o backend documentado. Lectura de información, mapa, readback y flash si existe soporte real. No deduzcas direcciones de carga FDL de ejemplos genéricos.

### 9.4 Samsung

Distingue MTK, Qualcomm y Exynos. Perfiles Download, MTP y ADB solo si están implementados. UI para BL/AP/CP/CSC/HOME_CSC y PIT. Mostrar diferencia entre CSC y HOME_CSC sin prometer preservación universal de datos. Validar modelo, revisión binaria, integridad y restricciones antes de flash.

Repartition, NAND erase, escritura PIT y cambios en EFS deben permanecer deshabilitados hasta disponer de validación y procedimiento específico. Mostrar diagnóstico de Knox/KG; los cambios de administración deben usar canales autorizados, no un supuesto reset genérico.

### 9.5 Xiaomi / Redmi / POCO

Perfiles Fastboot, Recovery/Sideload y EDL/MTK cuando proceda. Leer producto, codename, bootloader, slots e información disponible. Firmware matching por identidad y región comprobadas. Preservar restricciones anti-rollback: detectar y rechazar downgrade incompatible, nunca añadir un botón para omitirlas. Desbloqueo/rebloqueo solo mediante procedimiento OEM válido, con advertencias de borrado y compatibilidad.

### 9.6 Huawei / Honor y otras marcas

Catálogo extensible para Oppo, Realme, OnePlus, Vivo, Motorola, Tecno, Infinix, itel, Nokia, ZTE, Lenovo, LG, Asus, TCL y otras marcas verificadas en las fuentes. No atribuyas soporte a todos sus modelos. OEMINFO, NV y calibración solo mediante perfiles específicos y respaldos del mismo equipo. Identificadores de radio y seriales no se modifican mediante comandos genéricos.

### 9.7 Apple

Diagnóstico de identidad, estado de conexión, información de recuperación/DFU y restauración oficial cuando existan dependencias legítimas compatibles. Una UI de ramdisk o Purple no acredita soporte real A12/A13. La disponibilidad requiere documentación del backend, hardware auxiliar cuando corresponda y pruebas por modelo/versión.

No implementar evasión de Activation Lock, extracción de credenciales, restauración de registros de activación ajenos ni cambios de identidad para suplantación. La arquitectura puede registrar estas categorías publicadas por herramientas externas como referencias no implementadas, sin fingir equivalencia funcional.

### 9.8 Servicios de seguridad y cuentas

Incluye en la matriz comparativa las categorías FRP, cuentas OEM/MiCloud, screen lock, MDM, KG, activación y bootloader porque aparecen en la referencia del producto. Distingue diagnóstico, recuperación oficial, borrado autorizado y bypass de controles: no son sinónimos.

Para nuestra aplicación usa información de estado, flujos oficiales de recuperación/desvinculación y proveedores autorizados cuando existan. Si falta un mecanismo legítimo documentado, muestra Unavailable con motivo. No renombres FRP como aprovisionamiento para ocultar una eliminación de protección. No añadas exploits, payloads de evasión o desactivación encubierta de administración.

Esta limitación no bloquea indexador, interfaz, CLI, catálogos, diagnóstico, validación, backups, firmware y pruebas. Completa todo lo independiente y registra la diferencia frente a la referencia.

## 10. Gestor de particiones, backups y protección de escritura

DataGrid: selección, nombre, GUID, LUN, sector inicial, sectores, offset en bytes, longitud en bytes, tamaño de sector, archivo asociado y protección. Usa ulong/checked para geometría, evita overflow y documenta unidades y fin inclusivo/exclusivo.

Parser GPT de imágenes offline: protective MBR cuando exista, firma, revisión, tamaño de cabecera, CRC32 de cabecera y entradas, límites de LBAs, consistencia de primaria/secundaria y solapamientos. No reparar automáticamente una tabla dudosa.

Toda escritura requiere OperationPlan inmutable ligado a fingerprint de dispositivo, sesión, geometría, hashes de entrada y particiones elegidas. Invalida el plan si cambia cualquiera. Incluye dry-run, preview, backup previo cuando sea técnicamente posible, confirmación explícita y verificación posterior.

Bloquea escrituras fuera de límites, offsets inválidos, tamaños incompatibles y modelos/firmware no coincidentes. No escribas por nombre de partición solamente. Una restauración parcial fallida no debe declararse reversible: registrar RecoveryRequired y pasos manuales documentados.

Dump por streaming con nombre temporal .partial, avance en bytes, hash y metadatos. Renombrar al finalizar correctamente. Detectar espacio insuficiente antes y durante la operación. Al reanudar exigir identidad y geometría coincidentes.

Backups con manifiesto, hashes, modelo/placa, fecha, herramienta/backend, sesión y geometría, minimizando identificadores personales. EFS/NV no se clonan entre dispositivos. Registra datos sensibles con redacción o cifrado según configuración.

No llames a una reparación GPT genérica AMONET GPT FIX: Amonet es un mecanismo específico, no un sinónimo de CRC/GPT. Puede existir un perfil separado solo si su implementación y compatibilidad están documentadas. En ausencia de ello, usar Analyze GPT y Plan GPT Repair para imágenes offline; escritura hardware deshabilitada.

## 11. GUI Avalonia / MVVM

Archivos obligatorios: Views/MainWindow.axaml, Views/MainWindow.axaml.cs y ViewModels/MainViewModel.cs. Divide vistas y viewmodels por módulo para evitar un monolito. Code-behind solo para comportamiento visual inevitable.

Paleta: fondo #000000/#0b0f0c, bordes #00FF66/#00cc44, secundario #b8ffda, warning #FFD700, error #FF3366 y protocolo #00E5FF. Usa estilos Avalonia válidos comprobados. No bloquees el proyecto por un glow: aplica BoxShadow si es compatible o fallback sin efecto. Fuente monoespaciada del sistema; una fuente distribuida requiere licencia.

Header: estado real, identidad, modo, transporte, chipset y almacenamiento si se conocen. Selector COM no aparece como única vía en modos USB bulk. Selectores marca/modelo/variante derivados del catálogo y manifiesto. Mostrar grado de verificación y dependencias faltantes.

AsyncRelayCommand o equivalente validado; CanExecute según estado real. ObservableCollection actualizada en Dispatcher.UIThread. x:DataType y bindings compilados. Evita .Result/.Wait en UI y async void salvo handlers de eventos necesarios. Desuscribir eventos y disponer servicios al cerrar.

Consola inferior: timestamps, severidad, operación/session ID y buffer acotado. Colores por severidad, búsqueda, copiar, exportar y autoscroll suspendido al desplazarse hacia arriba. Redactar secretos. No llamar ANSI terminal a un TextBox sin parser: implementa SGR mínimo probado o usa eventos estructurados y representa colores en UI mientras CLI emite ANSI.

Mostrar CONNECT / READ INFO, STOP / CANCEL y REBOOT TO SYSTEM con funciones separadas. Confirmaciones destructivas con identidad y detalle del plan. Nunca enviar operaciones por cambiar un ComboBox, cargar una vista o conectar físicamente el teléfono.

Visor testpoints con zoom/pan, búsqueda, origen, revisión de placa y archivos faltantes manejados. No relacionar automáticamente modelos de nombre parecido.

## 12. CLI verdadera

Implementa TensaZangetsu.Cli consumiendo el mismo ApplicationEngine. Interfaz deseada:

```text
tensa assets index --root <path> --output <manifest>
tensa assets validate --manifest <manifest>
tensa catalog search --query <text>
tensa devices watch
tensa device info --wait --timeout 120 --device <selector>
tensa partitions inspect --image <file>
tensa operation plan --profile <id> --operation <id> --device <selector>
tensa operation run --plan <file> --confirm-device <identity>
```

Los comandos que dependen de hardware esperan el dispositivo con cancelación y mensajes Waiting for device..., sin reinicios implícitos. devices watch es pasivo. Ctrl+C usa el mismo mecanismo de cancelación segura que la GUI. --timeout aplica a espera/adquisición según documentación; no aborta ciegamente una escritura en curso.

Salida humana con ANSI solo si la terminal lo soporta. --json produce eventos JSON Lines en stdout sin mezclar mensajes decorativos; diagnósticos a stderr. Progreso monotónico, estados y bytes reales. Códigos de salida distintos y documentados para éxito, argumentos inválidos, timeout, cancelación, no soportado, autenticación requerida, fallo y recuperación necesaria. Un --yes global no omite identidad ni restricciones de escritura.

## 13. Pruebas obligatorias

Core y parsers deben probarse sin hardware. Implementa como mínimo:

1. Indexación con rutas con espacios, acentos, mayúsculas, duplicados y archivos desconocidos.
2. XML malicioso con DTD y rutas ../ rechazadas.
3. Manifiesto inválido no reemplaza al válido.
4. Metadatos contradictorios, modelo ambiguo y falta de BIT no generan loader automático.
5. Cambiar modelo invalida DA y planes anteriores.
6. Puerto aparece, desaparece o reenumera durante handshake.
7. Cancelación/timeout y doble clic sin carreras ni UI bloqueada.
8. Dispositivo distinto tras reconectar nunca recibe una operación anterior.
9. CONNECT / READ INFO no invoca reboot, write ni erase, comprobado con spy/fake.
10. Backend ausente devuelve Unavailable; autenticación pendiente devuelve AuthRequired.
11. GPT con CRC inválido, solapamientos, overflow y sector de tamaño variable.
12. Plan de flash con hash, identidad o geometría cambiados se rechaza.
13. Errores de proceso externo, stdout/stderr concurrentes y argumentos con espacios.
14. Dump interrumpido conserva .partial y no informa éxito.
15. Simulación se distingue de hardware y no abre transportes físicos de escritura.
16. Contratos de CLI, --json limpio y exit codes.
17. Pruebas Avalonia headless de comandos, bindings y estados donde la versión elegida lo permita.

No ejecutes pruebas destructivas automáticamente. Pruebas reales requieren un dispositivo de laboratorio, consentimiento, backup y una lista explícita de pasos. Documenta qué modelos y versiones se probaron, sin generalizar a toda la marca.

## 14. Ejecución por fases y control de avance

Fase A: auditoría, inventario y matriz comparativa.
Fase B: solución mínima con Core, GUI y CLI compilables; paquetes fijados y smoke test.
Fase C: catálogo, esquemas, indexador, resolver y fixtures offline.
Fase D: máquina de estados, simulación explícita y pruebas de cancelación/concurrencia.
Fase E: integración ADB/Fastboot de lectura cuando las herramientas estén disponibles.
Fase F: adaptadores serial/USB y backends por plataforma, uno por uno y solo con protocolos verificables.
Fase G: parsers firmware/GPT, planes, backups y verificación.
Fase H: pulido GUI/CLI, documentación, empaquetado y reporte de validación.

Tras cada fase ejecuta el subconjunto de pruebas y build correspondiente. Mantén docs/PROGRESS.md con Completed, InProgress, Blocked y Next. Si una fase se bloquea por permisos, herramientas o hardware, registra el requisito exacto y sigue con tareas independientes. No declares que el software ya equivale a UnlockTool porque la interfaz contiene sus nombres.

## 15. Build, calidad y entrega

Detecta el nombre real de la solución antes de usar estos comandos; no recrees la solución si ya existe:

```bash
dotnet restore TensaZangetsu.slnx
dotnet build TensaZangetsu.slnx -c Release --no-restore
dotnet test TensaZangetsu.slnx -c Release --no-build
dotnet format TensaZangetsu.slnx --verify-no-changes
```

Activa TreatWarningsAsErrors en los proyectos propios una vez resueltas las advertencias. No uses NoWarn global, deshabilitación de nullable ni desactivación de tests para alcanzar un resultado aparente. No ocultes advertencias de dependencias: evalúalas y registra su tratamiento. Para verificación reproducible adicional utiliza restore --locked-mode una vez generados y revisados los lockfiles.

Guarda salida completa de comandos y exit codes en artifacts de validación sin secretos. Después del build vuelve a comprobar hashes de los assets originales y compara con el inventario inicial. Verifica que ejecutar desde un directorio distinto no rompe rutas configuradas.

Entrega docs/BUILD_REPORT.md con SDK y paquetes exactos, SO, comandos ejecutados, resultados, warnings, tests, funciones verificadas, soporte de hardware realmente probado y bloqueos. Escribe README con instalación, configuración, indexación, GUI, CLI, simulación, backup y limitaciones.

No afirmes 0 errors / 0 warnings salvo que un build real lo demuestre. Si no puedes ejecutar .NET en tu entorno, entrega código y el informe indicando NotExecuted y el requisito para verificarlo. Compilación correcta no equivale a protocolo hardware correcto.

Criterio final: un repositorio que se puede construir y probar, con interfaz y CLI utilizables, indexación no destructiva, capacidades honestas y operaciones no disponibles claramente señaladas. La ausencia de loaders, claves, servicios OEM o hardware nunca se reemplaza por resultados inventados.
