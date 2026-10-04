# Conexión autónoma 0.4.0 — 1 de octubre de 2026

## Entregado

- Formulario y teclado VR con endpoint y cuenta editables, slot 1–3, contraseña enmascarada de 1–6 caracteres y Recordar voluntario.
- Credencial guardada únicamente tras validar sesión y catálogo, cifrada AES-GCM en almacenamiento Android privado excluido de backup, con clave AndroidKeyStore y enlace criptográfico al endpoint, usuario y slot.
- Reconexión de inicio, desconexión persistente y eliminación de la credencial. No hay despliegue automático ni importación del archivo USB legado.
- Teclado modal que impide seleccionar/mover el grafo o activar otros paneles mientras está abierto; panel móvil y passthrough conservados.
- Backend actualizado en `guategeeks-aws2026`, `us-east-1`: nueva contraseña aleatoria de exactamente seis caracteres; API rechaza longitud vacía o mayor de seis; CIDR continúa en `186.151.64.244/32`.

## Evidencia

| Verificación | Resultado |
|---|---|
| Backend unit/contract | 40 pruebas correctas |
| Unity EditMode | 32 correctas, 0 fallidas |
| Unity PlayMode | 12 correctas, 0 fallidas |
| Plantillas CloudFormation | 11 variantes, 0 hallazgos |
| Almacén nativo en Quest 3 | Cifrado/recuperación, ausencia de texto claro, binding incorrecto, archivo alterado, clave eliminada, olvidar y reinscripción: PASS |
| Backend real | Contraseña de seis caracteres, sesión, catálogo y validación de tres presets: PASS |
| Slots | 1, 2 y 3 vacíos; no se creó una arquitectura durante esta entrega |
| Paquete final | 0.4.0, versionCode 4, ARM64, min API 32, target API 36, INTERNET y passthrough |
| Compilación final | 0 errores, 8 avisos; informe en `Validation/quest3-build.txt` |
| Firma | APK Signature Scheme v2, certificado debug anterior para actualización local |
| Búsqueda de secretos | Contraseña activa (UTF-8/UTF-16) y Basic codificado ausentes del contenido descomprimido del APK |
| Instalación | Actualización conservando datos en Quest `2G0YC5ZG9J06XL`; apertura solicitada, Meta pide controles despiertos |

APK: `Builds/Quest3/GuateGeeksAWSVR-Quest3.apk`.

SHA-256: `605DC41FCC2A52D728A0B62DE8953D624ACC0818EFA44062EBC3CB113629906F`.

Las pruebas nativas compilan el archivo de producción `CredentialVault.java` dentro de una aplicación de instrumentación con UID distinto; no usan ni eliminan credenciales de la aplicación real. La aplicación de prueba se desinstaló al terminar. El informe está en `Validation/android-vault-tests.txt`; la herramienta reproducible es `Tools/Test-AndroidVault.ps1`.

La captura `Validation/08-wireless-connection-form.png` fue inspeccionada visualmente. Los tests Unity verifican límite/enmascaramiento, bloqueo del grafo, persistencia sólo tras éxito, error 401, fallo del almacén, desconexión y olvido, además de las regresiones previas.

## Pendiente de aceptación y publicación

- El usuario debe despertar los controles y verificar entrada de contraseña, Recordar y reinicio de la app/visor con USB desconectado. La prueba nativa del cifrado no sustituye esa aceptación del flujo Unity en VR.
- La instalación de esta transición utilizó el USB conectado. La distribución futura sin cable requiere App ID, acceso de propietario a Meta, firma de lanzamiento estable y cuentas destinatarias. Se preparó `QuestRelease.Build` y la [guía de distribución](Wireless-Distribution.md), pero aún no existe una publicación de esta entrega.
- No se realizó otra creación de arquitectura en AWS para validar el nuevo formulario; la demo permanece limpia.
- El emparejamiento por código era una ampliación opcional del plan y no se implementó.

La contraseña rotada se consulta como operador en Secrets Manager; no se incluye en este informe ni en el proyecto.
