# Distribución privada por Wi-Fi

La conexión AWS de 0.4.0 funciona desde el visor. Para instalar y actualizar también sin USB se necesita un canal ALPHA/BETA de Meta. La publicación está pendiente del App ID y acceso del propietario; no se ha creado ni publicado una app en Meta.

## Preparación de build

- Identificador Android: `com.guategeeks.awsarchitectlab`; 0.4.0, versionCode 4, Quest 3, ARM64, IL2CPP, OpenXR e INTERNET.
- El APK de desarrollo en `Builds/Quest3` conserva la firma debug existente para actualizar la instalación actual sin borrar datos. No subirlo como versión final del canal.
- Crear o recuperar una clave de lanzamiento estable fuera del proyecto; guardar una copia protegida y conservarla para todas las actualizaciones. No almacenar contraseñas en Git ni argumentos de comandos.
- Iniciar Unity con `GG_RELEASE_KEYSTORE`, `GG_RELEASE_ALIAS`, `GG_RELEASE_STORE_PASSWORD` y `GG_RELEASE_KEY_PASSWORD` en su entorno. Los dos últimos son secretos. El editor ya abierto no hereda variables nuevas.
- Ejecutar **GuateGeeks → Meta Quest 3 → Build signed private-channel APK**. También existe el método de batch `GuateGeeks.AwsVr.Editor.QuestRelease.Build` para una sesión Unity exclusiva. Genera `Builds/Release/GuateGeeksAWSVR-Quest3.apk`; rechaza configuración de firma ausente y limpia los campos de contraseña al finalizar.
- Verificar certificado, permisos y empaquetado antes de subir; incrementar versionCode para cada actualización posterior.

## Publicación y aceptación

1. Seleccionar la organización y la aplicación del propietario en Meta Developer Dashboard y confirmar App ID y paquete Android.
2. Subir el APK firmado al canal privado y resolver las comprobaciones de Meta. Invitar sólo las cuentas de prueba designadas por el propietario.
3. Aceptar la invitación y descargar la app desde la biblioteca del visor mediante Wi-Fi.
4. Si la firma cambia respecto a la instalación debug, respaldar diseños y reinstalar deliberadamente; no intentar sobreescribir certificados incompatibles ni prometer conservar la credencial cifrada.
5. Con USB desconectado, introducir la contraseña de seis caracteres, activar Recordar, conectar, cerrar y abrir la app, y reiniciar el visor. Verificar que reconecta sin PC y nunca despliega automáticamente.
6. Probar Desconectar, Olvidar, cambio de endpoint y red sin acceso. Descargar una segunda versión del canal por Wi-Fi para verificar actualizaciones.

La firma preparada no equivale a una publicación completada. Hasta disponer del proyecto Meta y las cuentas destinatarias, la distribución por canal permanece pendiente.

Referencia: [canales privados de Meta](https://developers.meta.com/vr/resources/publish-release-channels/).
