# Slots e inspección — 0.6.0

En la barra principal, abre **Slots / inspección** y selecciona **Slot 1**, **Slot 2** o **Slot 3**. No necesitas recuperar el diseño del slot para inspeccionarlo. La sesión de AWS debe estar conectada; la consulta funciona por Wi-Fi.

## Limpiar un despliegue

1. Selecciona el slot y revisa su estado y recursos.
2. Pulsa **Limpiar slot N…**.
3. Revisa slot, endpoint, región e identificador del despliegue en la confirmación.
4. Pulsa **Sí, eliminar slot N** para eliminar todos sus recursos y datos, incluidas las versiones de objetos S3, ítems DynamoDB y logs de las funciones.
5. Espera el resultado o usa **Dejar de observar**. Esta última opción no cancela AWS; **Actualizar slot N** vuelve a consultar el estado.

La app comprueba la identidad del despliegue antes de borrar. Si otro operador reemplazó el slot, exige actualizar y confirmar otra vez. Las peticiones de borrado no se repiten automáticamente ante una respuesta incierta. Una purga S3 incompleta puede continuar por solicitud explícita del backend, conservando la identidad confirmada.

El diseño local y la biblioteca se conservan. **Limpiar**, en la barra de diseño, sigue limpiando únicamente la mesa local.

## Logs e ítems

Selecciona un recurso del slot para consultar su identificador físico:

- **Lambda → Inspeccionar logs**: registros reales de CloudWatch de los últimos 15 minutos al actualizar, en orden cronológico. La hora se muestra en UTC. No representa captura ni trazado de paquetes de red.
- **DynamoDB → Inspeccionar ítems**: lectura eventual de una muestra de hasta 10 ítems por petición, sin orden garantizado. Se conserva el JSON tipado de DynamoDB: `S` significa texto y `N` número.
- **Texto / registro**: navegación local por el contenido recibido. Las páginas de texto tienen hasta 12 líneas para mantenerlas legibles en VR.
- **Más desde AWS**: solicita otra página. Puede existir otra página aunque la actual esté vacía. El cursor vence a los 15 minutos; **Actualizar** inicia una consulta nueva.

Cada registro se limita a 4096 caracteres e indica explícitamente si fue recortado. La inspección es de solo lectura y no guarda sus resultados en el almacenamiento del visor. Se pueden acercar los paneles con su asa **MOVER PANEL**.

## Validación

El backend pasó 47 pruebas automatizadas. La aceptación contra AWS creó temporalmente una Lambda conectada a DynamoDB en el slot 3 vacío, envió un evento y verificó su identificador tanto en los ítems como en los logs a través de la API desplegada. El borrado con una identidad incorrecta devolvió 409. El despliegue temporal se eliminó y se verificaron los tres slots vacíos.

Evidencia del backend: `GuateGeeksAWS2026/deployment/inspection-acceptance.json`. Pruebas y compilación finales de Unity: `Validation/EditMode-summary.txt`, `Validation/PlayMode-summary.txt` y `Validation/quest3-build.txt`. La instalación y aceptación física se registran por separado; las pruebas del editor no sustituyen la comprobación dentro del visor.
