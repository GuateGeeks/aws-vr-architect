# Laboratorio 0.8.0

## Experiencia visual

El laboratorio incorpora una pared de reactor con anillos contrarrotatorios, núcleo holográfico, seis módulos de cómputo, columnas de titanio, vigas superiores, un halo suspendido y una mesa con pedestal. Las guías de suelo y los marcadores ambientales se animan con el reloj de confort: **Animación: NO** congela estos efectos. Son decoración, no métricas ni tráfico observado en AWS. Fondo oculto y passthrough conservan sus controles.

Los paneles tienen marcos recortados, una segunda línea interior, registro ámbar y superficies oscuras para favorecer la lectura. Los servicios conservan su símbolo y color, y añaden volúmenes propios: procesador, discos, contenedor, cartuchos, puerta, concentrador y barras. Las piezas metálicas estáticas se combinan por material para reducir llamadas de dibujo. El sombreado de titanio no requiere luces adicionales, sombras ni bloom.

Las manos usan una malla dinámica por mano y un emisor dorsal. Los segmentos siguen las articulaciones de XR Hands; la palma tiene volumen, placa dorsal y muñeca. Los gestos, rayos de selección, propiedad de los agarres y protección al recuperar tracking se conservan. La geometría desaparece si no hay poses válidas; no se inventan poses cuando se pierde seguimiento.

## Logs e ítems automáticos

Abre **Slots / inspección**, selecciona el slot y una Lambda o tabla, y pulsa **Inspeccionar logs** o **Inspeccionar ítems**. El lector consulta automáticamente cada tres segundos después de cada respuesta. Esta es actualización periódica de la API existente, no WebSocket: la latencia de red, los reintentos y el recorrido de páginas se suman al intervalo. CloudWatch puede tardar en publicar eventos; DynamoDB usa lectura eventual.

La consulta recorre los cursores de AWS automáticamente. Los logs corresponden a la ventana de quince minutos definida por el backend; se conservan hasta cien entradas, mostrando las más recientes primero al terminar el recorrido. Los ítems son una muestra de hasta cien entradas de un Scan sin orden garantizado. Una primera página puede mostrarse como muestra parcial; los siguientes recorridos conservan la vista anterior hasta terminar. No se deduplican mensajes idénticos: pueden corresponder a eventos distintos.

- Seleccionar un registro o avanzar su texto fija la lectura. El contenido se conserva aunque deje de aparecer en la siguiente consulta; el pie del lector lo indica.
- **Seguir recientes** vuelve a la primera entrada y libera la lectura fijada.
- Las flechas bajo la lista recorren grupos de diez entradas locales. El contenido largo mantiene sus páginas de texto.
- **Pausar lectura** detiene las nuevas consultas y conserva la vista; **Reanudar lectura** continúa.
- Abrir Ajustes, ocultar el lector, pausar la aplicación o ejecutar una operación principal suspende las nuevas consultas. Cerrar o reemplazar el lector, desconectar o cambiar sesión cancela su observación.
- Errores transitorios conservan los datos y reintentan con espera creciente. Errores de identidad, autorización o recurso inexistente detienen la observación y muestran el motivo. Vuelve a slots para seleccionar el recurso vigente.

La observación usa su propio transporte HTTP en ejecución real, de modo que cerrarla no cancela un despliegue ni un evento de prueba. No modifica recursos, no despliega el backend y no escribe los datos inspeccionados en disco.

## Verificación

Las pruebas de Play Mode cubren renovación de ítems, paginación automática de logs, lectura fijada, pausa, cambio de identidad, cierre, geometría de manos y congelación de animación. Los fixtures son sintéticos y no consultan AWS real. Las capturas `Validation/21-reactor-lab.png` y `Validation/22-armored-hand.png` se renderizan en Unity; la pose de mano de la segunda es sintética.

La aceptación física de manos, legibilidad estéreo y rendimiento sostenido requiere una prueba en Quest. El APK local se genera con el flujo existente de `build-quest`; no se publica ni se instala automáticamente.
