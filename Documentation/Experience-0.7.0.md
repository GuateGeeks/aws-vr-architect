# Experiencia VR — 0.7.0

## Inspector de objetos

El panel derecho sigue al objeto seleccionado. Muestra definición, relaciones, estado e identificador. Cambiar de objeto conserva abierto el lector de AWS. **Inspeccionar este objeto en AWS** consulta el recurso del despliegue asociado; comprueba la identidad del stack antes de mostrarlo. También puedes consultar cualquier slot desde **Slots / inspección**.

## Lector de logs e ítems

La lista izquierda permite seleccionar un registro. El área derecha muestra su contenido con páginas de texto independientes de las páginas de resultados de AWS. **Actualizar** inicia una consulta nueva; **Más registros en AWS** carga la siguiente página. Los límites, recortes y lectura eventual se indican en pantalla. El lector es de solo lectura y conserva el JSON tipado de DynamoDB.

## Ajustes unificados

**Ajustes** abre un único panel móvil con tres pestañas:

- **Conexión AWS**: sesión, credenciales, slot, región y simulación de fallos en modo local.
- **Espacio**: fondo virtual, passthrough y comodidad.
- **Controles**: instrucciones, restaurar paneles y centrar vista.

Las credenciales se introducen dentro de este panel. El teclado conserva el bloqueo de interacción con la arquitectura mientras está abierto. La limpieza de slots continúa requiriendo su confirmación explícita.

## Manos y controles

Con el seguimiento de manos activado en Quest, deja los controles y muestra las manos al visor. Apunta con el rayo y junta pulgar e índice. Suelta sobre el mismo botón para activarlo. Mantén la pinza sobre un objeto o un asa **MOVER PANEL** para moverlo; suelta para dejarlo. Puedes iniciar un enlace en un puerto de salida y soltar sobre un puerto de entrada.

La recuperación del seguimiento requiere abrir la mano antes de otra pinza. Perder el seguimiento o activar un gesto del sistema libera lo que sostenías sin pulsar botones. Los controles conservan sus funciones y tienen prioridad cuando el visor los reporta activos. Las manos son líneas holográficas construidas con componentes Unity.

Usa **Controles → Restaurar paneles** para recuperar los paneles. Con controles también funciona A + X.

## Validación

Pruebas automatizadas: `Validation/EditMode-summary.txt` y `Validation/PlayMode-summary.txt`. Compilación y preflight: `Validation/quest3-build.txt` y `Validation/quest3-preflight.txt`. La aceptación física de seguimiento de manos se registra por separado: las pruebas de pinza no sustituyen esa comprobación.
