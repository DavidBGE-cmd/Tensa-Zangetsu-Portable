# Comparación de capacidades y brechas

**Fecha de consulta:** 2026-10-20  
**Tarea:** `TZS-2026-10-20-AUDIT`

## Método y límites de evidencia

Se consultaron las páginas oficiales de modelos y actualizaciones indicadas en el prompt. Ambas respondieron HTTP 403 al acceso disponible. Por tanto, no se verificaron listas actuales de modelos, funciones, BIT, chipsets, revisiones ni fechas de actualización.

La documentación oficial de Avalonia consultada indica .NET 8 o posterior y describe sus plantillas. La documentación de permisos de Antigravity consultada describe permisos Allow/Ask/Deny, permisos de lectura/escritura, comandos y dominios, y la aprobación necesaria para ciertas acciones.

Los ejemplos de Xiaomi Mi 6 / Sagit / Snapdragon 835, Oppo A37fw / Neo 9 / Snapdragon 410 y Huawei Enjoy 6S / DIG-TL10 / Snapdragon 435 proceden del prompt del usuario. Se registran como `ReferenceOnly`, no como comprobación local ni como soporte de UnlockTool verificado independientemente en esta sesión.

### Estados usados

- `ReferenceOnly`: referencia externa sin implementación local demostrada.
- `IndexedUnverified`: hay un archivo inventariado, pero no se ha probado su función o compatibilidad.
- `ImplementedUntested`: hay código, sin evidencia suficiente de validación de la operación en hardware.
- `Verified`: requiere evidencia reproducible específica para dispositivo/versión/operación; no se asigna por compilar.
- `Unavailable`: backend legítimo no existe/configura o la operación queda excluida por límites de seguridad.

## Matriz inicial

| Área/operación | Referencia y restricciones | Estado local inicial | Evidencia o brecha |
| --- | --- | --- | --- |
| Catálogo por modelo, SoC, función y revisión | Página oficial no accesible en esta consulta; ejemplos del usuario son sólo referencias | `ReferenceOnly` | Falta captura verificable de fuente y capacidades por modelo |
| MediaTek BROM/Preloader, DA y firmware scatter | La referencia del usuario distingue loader, modelo, seguridad y modo; un asset no valida compatibilidad | `ImplementedUntested` / `IndexedUnverified` | Hay proyectos y assets; no hay pruebas de hardware aportadas |
| Qualcomm Sahara/Firehose/EDL y rawprogram/patch | Loader/placa, almacenamiento y LUN deben coincidir | `ImplementedUntested` / `IndexedUnverified` | Hay proyectos de protocolo y firmware; ninguna matriz de modelos comprobada |
| UNISOC BSL, FDL1/FDL2 y PAC | FDL debe conservar su bundle, firma y versión | `ImplementedUntested` / `IndexedUnverified` | Hay proyectos y assets; no se declara un modelo verificado |
| Samsung Download, Odin, PIT, BIT | Plataforma, modelo y revisión binaria requieren validación específica | `Unavailable` hasta inventariar integración verificable | No se encontró evidencia de backend validado en esta auditoría |
| Xiaomi Fastboot/Recovery/EDL | Región, codename, modo y anti-rollback limitan la operación | `ImplementedUntested` para rutas de software presentes; escrituras no autorizadas por defecto | Código presente no demuestra cobertura por modelo |
| Otras marcas y servicios OEM | Se necesitan perfiles, procedencia y mecanismo OEM autorizado | `ReferenceOnly` o `Unavailable` por operación | No atribuir cobertura general de marca |
| Apple Recovery/DFU y restauración oficial | Requiere backend legítimo compatible y pruebas por modelo/versión | `Unavailable` | No verificado en el build baseline |
| Estado FRP, cuentas, MDM/KG y Activation Lock | Diagnóstico y recuperación oficial son distintos de eludir protecciones | `Unavailable` para bypass; diagnóstico/recuperación sólo si se prueba | No implementar bypass ni cambiar su nombre |
| Testpoints | Imagen/modelo/revisión y procedencia deben ser explícitos | `IndexedUnverified` hasta revisar metadatos | 201 archivos en `TESTPOINTS`; conteo no valida cada relación |
| GPT, firmware y dumps offline | Parsers, límites, CRC y streaming validables sin dispositivo | `ImplementedUntested` hasta ejecutar pruebas dirigidas | Hay proyectos GPT/Firmware; no se ejecutaron tests en baseline |
| GUI, CLI y simulación | UI nunca bloqueante; simulación separada de hardware | `ImplementedUntested` | App y CLI existen; faltan tests automatizados detectados |

## Capacidades que no se habilitan como soporte

No se crearán asociaciones de loaders por semejanza de nombre ni se habilitarán bypasses de FRP, cuentas, MDM/KG, Activation Lock o autenticación de BootROM. Los flujos de recuperación sólo podrán invocar documentación/proveedores oficiales y explícitamente configurados. La ausencia de un backend se expresa como `Unavailable` con su dependencia, nunca como éxito simulado.

## Siguiente evidencia necesaria

1. Captura o exportación autorizada y fechada de las páginas oficiales si se requiere comparar la cobertura por modelo; no automatizar extracción masiva ni acceso autenticado.
2. Pruebas offline de parsers, resolución de loaders y estados del motor.
3. Para hardware, registro manual de dispositivo/modelo/revisión/firmware, backend y resultado; una prueba no se generaliza a toda la marca.
4. Para herramientas externas, versión, origen, hash, licencia, argumentos, plataforma y exit codes antes de declarar una integración.

## Estado tras la primera implementación — 2026-10-20

- La indexación completa inventaría 31.369 assets con hashes; la clasificación y los metadatos no prueban compatibilidad. No se crean bundles implícitos.
- La GUI informa explícitamente la ausencia de backend validado y su gate deniega las operaciones antes de abrir transporte o ejecutar los callbacks heredados.
- Las cargas iniciales, el escaneo de puertos, el parseo de metadatos y la carga de preview se desplazan fuera del hilo de interfaz; esto no equivale a pruebas visuales ni de hardware.
- Ningún modelo/operación cambia a `Verified`. La comparación por modelo y la habilitación de backends continúan pendientes de evidencia y pruebas específicas.
