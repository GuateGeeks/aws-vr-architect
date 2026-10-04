# Plan: Quest autónomo, sin USB ni connect_quest.py

**Estado: A–D implementados en 0.4.0; publicación en canal Meta y aceptación del usuario en VR pendientes.**

Actualización solicitada: contraseña Basic de exactamente seis caracteres generada en el backend y límite de seis en Unity/API. El teclado la mantiene siempre enmascarada. La ampliación opcional de emparejamiento continúa fuera de esta entrega. Consulta [la guía actual](Cloud-Integration.md); el texto siguiente conserva el diseño original. Preparada el 1 de octubre de 2026. La limpieza de la arquitectura de prueba se realizó por separado; el backend permanece activo.

## Resultado esperado

Abrir el laboratorio en Quest, conectarse por Wi-Fi y construir infraestructura sin cable, terminal, adb ni un PC encendido. Mantener Unity como frontend, HTTPS + Basic Auth, la Lambda de control y sus roles IAM. No introducir Cognito. Conservar menús móviles, passthrough, modo demo y confirmación explícita antes de crear recursos.

La conexión de red **ya es inalámbrica**: Unity llama directamente a la API. El USB se usa únicamente para entregar la URL y credencial mediante un archivo de 60 segundos. Por eso no necesitamos cambiar el motor de despliegue ni sustituir `AwsCloudApi`; debemos reemplazar el alta de la sesión y resolver su persistencia.

## Decisión recomendada

Implementar primero un perfil de conexión configurable dentro del visor, con credencial recordada de forma cifrada. Permite funcionar sin USB utilizando el backend actual y sin añadir servicios AWS. El costo de interacción es introducir la contraseña una vez; después, el visor puede reconectar al abrir la app.

Para varios visores o asistentes de AWS Day, añadir posteriormente emparejamiento mediante código corto. Mejora el alta inicial, pero necesita estado de dispositivos, aprobación y revocación en el backend. Se describe al final como ampliación independiente.

## Experiencia dentro de VR

1. El panel **CONEXIÓN AWS** muestra **Conectar**, **Configurar conexión**, **Desconectar** y **Olvidar credencial**. Desaparecen las referencias al USB y al script.
2. La primera vez, **Configurar conexión** abre un panel móvil con nombre del entorno, endpoint HTTPS, cuenta de servicio, contraseña enmascarada, slot y **Recordar este visor**. La URL actual y `quest-demo` pueden venir preconfigurados: son configuración pública, nunca un secreto.
3. Un teclado construido con botones Unity/TMP permite escribir con los controles, incluyendo mayúsculas, números, borrar y confirmar. El foco de texto bloquea las acciones de edición del grafo. La contraseña sólo se revela mientras se mantiene pulsado un control de mostrar.
4. **Probar conexión** consulta sesión y catálogo. Si ambas validaciones pasan, aplica la región del servidor y permite recordar la credencial. No crea recursos ni envía eventos de prueba.
5. En aperturas posteriores, un perfil válido con reconexión habilitada recupera la sesión automáticamente. Siempre muestra entorno, región, slot y estado real de conexión. Recuperar una arquitectura no vuelve a desplegarla automáticamente.
6. Sin red, ofrece reintentar o entrar explícitamente en demo. Un 401 solicita actualizar credenciales; un 403 explica la restricción de red. No presenta datos simulados como resultados AWS.
7. **Desconectar** detiene solicitudes y la reconexión hasta que el usuario vuelva a conectar. **Olvidar credencial** elimina el secreto local y su opción de reconexión. Ninguno borra infraestructura AWS.

Usar el teclado propio como ruta principal. Unity ofrece `TouchScreenKeyboard` y detección de soporte, pero la documentación general de Android no demuestra su comportamiento dentro del entorno OpenXR de este Quest; un teclado del sistema sólo sustituirá al propio tras una prueba física. [Referencia de Unity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TouchScreenKeyboard.html).

## Persistencia y autenticación

Separar dos objetos: un perfil público —URL, entorno, slot y preferencias— y una credencial protegida. No guardar la contraseña ni la cabecera Basic en PlayerPrefs, escenas, ScriptableObjects, JSON de arquitectura, checkpoints o APK.

Crear una pequeña integración Android con `AndroidKeyStore`: clave AES no exportable, cifrado AES-GCM del secreto y archivo cifrado en almacenamiento interno privado de la app. Asociar el dato cifrado al perfil y endpoint; cambiar de servidor obliga a configurar de nuevo la credencial. Excluir el archivo de copias de seguridad. La clave no se guarda junto al archivo. Android documenta el aislamiento de claves y recomienda ejecutar operaciones potencialmente lentas fuera del hilo principal; la disponibilidad de protección por hardware se verificará en Quest, sin asumirla. [Android Keystore](https://developer.android.com/privacy-and-security/keystore).

Ante clave perdida, archivo alterado o reinstalación, volver al formulario; nunca continuar con un secreto inválido. Mantener la contraseña descifrada solamente en memoria durante la sesión y conservar TLS normal, HTTPS obligatorio y bloqueo de redirecciones. El usuario puede elegir sesión sólo en memoria.

La credencial de la primera configuración se obtiene desde Secrets Manager por un operador autorizado, sin `connect_quest.py`. No convertir el endpoint de sesión en un distribuidor público de contraseñas. El backend actual comparte una cuenta de servicio entre clientes: rotarla invalida las credenciales guardadas en todos los visores, sujeto a la caché actual del servidor. La revocación individual requiere la ampliación de dispositivos descrita después.

## Trabajo concreto

| Entrega | Archivos/componentes | Condición de finalización |
|---|---|---|
| A. Configuración en VR | `ArchitectureLab.Cloud.cs`, nuevo panel y teclado Unity/TMP, nuevo `CloudProfile` | Configurar y probar sesión desde el visor sin leer `cloud-session.json`. |
| B. Almacén seguro | Nueva interfaz `ICredentialStore`, implementación Android y doble de pruebas en Editor | Recordar, recuperar y olvidar una credencial; probar archivo corrupto y clave ausente. |
| C. Sesión autónoma | Adaptar `CloudConnection` y `ConfigureCloud`, conservar `AwsCloudApi` | Reconectar con consentimiento previo, cancelar esperas, cambiar de modo y manejar 401/403/red perdida. |
| D. Migración | Guías, textos del laboratorio y pruebas | `connect_quest.py` queda documentado sólo para la versión 0.3; la nueva versión no consume archivos USB. No importar silenciosamente credenciales antiguas. |
| E. Distribución y prueba física | Firma estable, APK y canal privado Meta | Instalar/actualizar por Wi-Fi y completar el flujo con el USB desconectado. |

Implementar A–D como versión 0.4.0 antes de distribuirla. No cambiar el contrato de creación de infraestructuras para resolver el inicio de sesión. Actualizar las pruebas de UI para la nueva configuración y mantener las regresiones de edición, recuperación, agarres, menús y passthrough.

## Instalación y actualizaciones sin cable

Resolver también la distribución: iniciar sesión sin USB no basta si cada actualización requiere adb. Preparar una aplicación en Meta Developer Dashboard, una firma de lanzamiento estable y un canal privado ALPHA/BETA con acceso para las cuentas de los visores. Meta permite distribuir builds a usuarios invitados mediante estos canales y mostrarlos en su biblioteca; las builds deben cumplir los requisitos de empaquetado incluso en ALPHA. [Canales de distribución de Meta](https://developers.meta.com/vr/resources/publish-release-channels/).

La instalación actual usa firma de desarrollo. Antes de migrar, comprobar compatibilidad de firma y respaldar cualquier diseño que se quiera conservar: un cambio de certificado puede requerir reinstalación y volver a configurar la sesión. No prometer conservar el secreto después de reinstalar. La firma de lanzamiento y sus contraseñas quedan fuera del repositorio. [Firma de aplicaciones Android](https://developer.android.com/studio/publish/app-signing).

Esta entrega depende del acceso del propietario al proyecto de Meta y de las cuentas de prueba; no se ha publicado nada con este plan. La depuración inalámbrica puede ayudar durante desarrollo, pero no será un requisito de uso de la demo.

## Red y continuidad

El backend sigue restringido a `186.151.64.244/32`. El Quest necesita Wi-Fi con esa salida pública; cambiar de red puede devolver 403 aunque la credencial sea correcta. El operador actualizará `AllowedCidr` para el evento. Mantener esa restricción durante esta entrega; quitar USB no exige abrir la API a todo Internet.

Permitir editar el endpoint cuando se reinstale el backend, sin recompilar para un simple cambio de URL. Requerir HTTPS y nueva autenticación al cambiar el destino. Una URL con dominio estable puede añadirse después, con su configuración DNS/certificado correspondiente; no bloquea este trabajo.

## Pruebas de aceptación

- USB físicamente desconectado: abrir la app, configurar la sesión por controles, validar arquitectura y ver **AWS · SLOT 1**.
- Cerrar y reiniciar app y visor: reconectar con la credencial recordada; repetir con la opción desactivada y comprobar que vuelve a solicitarla.
- Verificar que contraseña, cabecera Authorization y texto descifrado no aparecen en APK, preferencias, diseños, checkpoints, copias de seguridad ni logs.
- Probar red caída, 401 después de rotación, 403 por red no permitida, cambio de endpoint, credencial olvidada, clave ausente y archivo alterado.
- Crear una arquitectura pequeña mediante confirmación en VR, revisar CloudFormation, enviar un evento, comprobar su procesamiento y eliminarla al terminar la prueba autorizada.
- Mantener cancelación local, recuperación de slot, modo demo, menús móviles y passthrough. Probar que escribir en el teclado no activa servicios detrás del formulario.
- Instalar y actualizar desde el canal privado sin USB, scripts locales ni PC conectado durante la experiencia.

**Criterio de terminado:** el uso normal y los reinicios no requieren `connect_quest.py`, adb, USB ni PC. La administración del backend y la rotación de secretos siguen siendo tareas del operador.

## Ampliación opcional: emparejamiento por código

Para evitar introducir una contraseña larga en cada visor: Quest solicita un emparejamiento y muestra un código corto; un operador lo aprueba desde una página HTTPS en móvil o PC; el visor obtiene su propia identidad de dispositivo y la guarda en el mismo almacén seguro. La página puede servirla la Lambda actual. La primera aprobación requiere otro dispositivo; las sesiones posteriores serían autónomas.

Esto añade una tabla DynamoDB de emparejamientos/dispositivos al **backend**, separada de las tablas creadas por las demos. Mantendría Basic Auth para operaciones de arquitectura, ahora con credenciales por dispositivo, y no usaría Cognito. Los roles IAM de aprovisionamiento permanecen exclusivamente en el servidor.

Antes de implementarlo, especificar endpoints de solicitud, aprobación, consulta/canje y revocación; identidad administrativa separada de las cuentas Quest; secreto de solicitud de alta entropía además del código visible; caducidad de cinco minutos; límites de intentos y solicitudes; aprobación y canje atómicos, de un solo uso; y permisos de slots por dispositivo. Ninguna ruta de emparejamiento sin credenciales activas puede desplegar recursos. La caducidad se comprueba en cada solicitud: el TTL de DynamoDB limpia registros en segundo plano y no sustituye esa comprobación. [Caducidad y TTL en DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ttl-expired-items.html).

Esta ampliación evita compartir la contraseña global y permite revocación individual, a cambio de más lógica y un recurso de estado permanente. Se deja fuera de la primera entrega para cumplir la autonomía con la arquitectura actual.
