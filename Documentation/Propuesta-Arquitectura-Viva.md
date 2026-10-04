# Arquitectura Viva — plataforma de diseño e interacción con objetos AWS

Propuesta revisada el 1 de octubre de 2026 según la aclaración de alcance del usuario. Sustituye la propuesta anterior de laboratorio de sistemas distribuidos. Propuesta aprobada e implementada en la versión 0.5.0; ver el estado de entrega abajo.

## Objetivo de producto

Crear, definir, conectar, revisar y desplegar una arquitectura AWS mediante objetos manipulables en VR. El valor está en convertir decisiones de arquitectura en acciones comprensibles y configuraciones que el backend pueda materializar. La UI, la interacción espacial y la definición de cada objeto son el centro de esta entrega.

Recorrido principal: **elegir un componente → colocarlo → definirlo → conectarlo → comprender el diseño → validarlo → crear los recursos**.

El movimiento de mensajes permanece como una ayuda visual para explicar las relaciones definidas. Observabilidad en vivo, trazas, captura de paquetes, inyección de fallos, laboratorios de reintentos, DLQ, puntuación y módulos de protocolos quedan fuera de esta propuesta revisada. Tampoco se incorpora colaboración multiusuario, un editor de código Lambda ni un catálogo AWS ilimitado.

## 1. Crear objetos con intención

El catálogo se convierte en una bandeja holográfica por función: entrada, cómputo, datos, mensajería y observabilidad. Cada ficha presenta el servicio, para qué sirve y sus conexiones disponibles.

Al elegir una ficha aparece una previsualización del objeto. El usuario apunta a la mesa y confirma su ubicación; antes de confirmar puede cancelar. Se conservan también acciones explícitas con botones para que el uso no dependa de descubrir gestos.

El objeto recién creado muestra su nombre y una acción «Definir». Un símbolo y una etiqueta indican si necesita configuración, conexiones o validación. La forma identifica el tipo de servicio; el color refuerza esa identificación.

Las tres plantillas existentes siguen siendo puntos de partida editables. Una mesa vacía también es válida durante el diseño, aunque todavía no pueda desplegarse.

## 2. Definir objetos con una ficha contextual

Seleccionar un objeto abre un inspector compacto junto a él, orientado al usuario y con opción de fijarlo en el espacio. Sólo hay un inspector activo; no se llenará la mesa de formularios simultáneos.

La ficha tiene tres secciones:

- **Identidad:** nombre visible, tipo y breve propósito dentro de la arquitectura. El nombre visible no se presenta como el nombre físico exacto que AWS generará.
- **Configuración:** controles claros para las propiedades realmente soportadas por el backend, con valor actual, predeterminado y explicación breve de su efecto. Aplicar y Cancelar hacen explícita la edición; Deshacer permite revertirla.
- **Relaciones:** entradas, salidas y servicios conectados. Seleccionar una relación resalta ambos extremos.

Prioridad: reemplazar el botón que alterna valores sucesivos por opciones visibles y comparables. La ayuda aparece al solicitarla o ante una decisión relevante.

### Alcance inicial de propiedades

| Objeto | Definición respaldada por el proyecto actual |
|---|---|
| API Gateway | HTTP API; conexión a una Lambda. El backend actual no admite REST API aunque exista como opción de simulación. |
| Lambda | Memoria de 128, 256, 512 o 1024 MB; función de demo predefinida y destinos permitidos. |
| DynamoDB | Capacidad bajo demanda o aprovisionada; clave fija `id`. El modo aprovisionado actual utiliza 1 RCU y 1 WCU. |
| S3 | Versionado activado o desactivado; destinos de notificación permitidos. Mostrar que el backend aplica configuración adicional de protección y expiración. |
| SQS | Estándar o FIFO; un consumidor Lambda según el contrato actual. |
| EventBridge | Nombre visible y destinos; explicar el bus y las reglas generadas. No mostrar los perfiles «AWS Day / Aplicación» como comportamientos distintos mientras el compilador no los distinga. |
| CloudWatch | Servicios asociados y retención donde el backend la aplica; explicar el dashboard y los logs generados. |

Si se amplían propiedades después, cada campo debe tener validación, persistencia y traducción comprobada en el backend. Ningún control de configuración se ofrecerá como efectivo si sólo cambia la apariencia.

## 3. Conectar mediante puertos con significado

Cada objeto expone puertos identificables de entrada y salida. Al iniciar una conexión, los destinos compatibles se resaltan y aparece una guía elástica desde el origen. Soltar sobre un destino válido crea la relación con una confirmación visual y háptica breve.

Antes de confirmar, la conexión dice qué significa: «invoca», «escribe», «publica», «notifica» o «asocia observabilidad». Las relaciones de observabilidad utilizan un estilo distinto y no transportan cápsulas como si fueran una ruta de negocio.

Una conexión rechazada explica el motivo junto al puerto: por ejemplo, «En esta plataforma, una cola admite un consumidor Lambda». Distinguir siempre las restricciones de la plataforma de las capacidades generales de AWS.

La compatibilidad combina tipo y configuración. No basta con permitir un enlace S3 → SQS si la cola elegida es FIFO. La validación local ayuda a editar; la API conserva la decisión final de validez.

## 4. Comprender el flujo que se está construyendo

La acción **Previsualizar flujo** anima cápsulas únicamente sobre las relaciones de datos definidas. Es una representación del diseño, rotulada como tal; no indica tráfico AWS observado ni éxito de procesamiento.

Al seleccionar un objeto, se iluminan sus entradas y salidas. Al seleccionar una conexión, se muestran el origen, el destino y la operación que representa. Una animación corta permite seguir la dirección sin ocultar nombres ni controles.

Esta ayuda permite responder: «¿Qué activa este objeto?», «¿Dónde termina la información?» y «¿Qué relación me falta definir?». No requiere una plataforma de telemetría, un motor de fallos ni cambios en los servicios desplegados.

## 5. Una UI espacial con jerarquía

Organizar el espacio en tres zonas principales:

- **Bandeja de componentes:** lateral, compacta y plegable.
- **Mesa de arquitectura:** centro despejado para manipular objetos y conexiones.
- **Barra de trabajo:** acciones Crear, Definir, Conectar, Revisar y Desplegar, con el modo activo siempre visible.

El inspector es contextual. Conexión AWS, passthrough y comodidad se agrupan en una zona de ajustes accesible, con estado de conexión visible en la barra de trabajo. Se conservan menús móviles y recuperación de posiciones.

La estética holográfica utiliza contornos, profundidad y pulsos para comunicar selección, compatibilidad y confirmación. Los formularios usan superficies suficientemente opacas para leerlos sobre passthrough. Evitar texto flotante sobre texto, brillo constante y animaciones que compitan con la edición.

Grip mueve; gatillo selecciona o confirma; cancelar siempre tiene el mismo comportamiento. Un teclado o formulario abierto bloquea acciones sobre objetos detrás de él. Mover el objeto o un panel nunca crea recursos AWS.

## 6. Organizar y reutilizar arquitecturas

Mantener Guardar, Cargar y Deshacer, mejorar su visibilidad y añadir una biblioteca local de diseños con nombre y miniatura. Organizar la mesa mediante alineación y distribución, conservando la colocación manual.

Separar la disposición espacial de la definición desplegable. Reordenar objetos no vuelve a provisionar infraestructura. El estado «desplegado» se asocia a la definición confirmada; una modificación funcional posterior muestra «diseño modificado».

La biblioteca local conserva el grafo y su disposición, nunca credenciales. Su primera versión debe seguir respetando los límites actuales: hasta 12 objetos y 36 conexiones, con al menos dos objetos para desplegar.

## 7. Convertir la revisión en confianza

Antes del despliegue, presentar una revisión legible:

- Objetos definidos y sus propiedades efectivas.
- Conexiones y recursos auxiliares que el backend generará, como reglas, permisos, logs e integraciones.
- Ajustes necesarios con una acción «Ir al objeto» que lo resalta y abre su ficha.
- API, región y slot elegidos, junto con la advertencia existente de creación real y costos.

El número de objetos visuales puede ser menor que el de recursos AWS generados; la UI debe explicarlo. Validar el diseño no equivale a haberlo desplegado.

Conservar la confirmación explícita de creación. Durante la operación, mostrar progreso y estado real por objeto con nombres comprensibles, detalles ampliables y recuperación de errores.

El contrato actual crea o retoma un diseño en un slot; no implementa actualizaciones arbitrarias sobre un despliegue existente. La UI debe manejar un slot ocupado sin sugerir que puede aplicar cambios o borrar datos automáticamente. Una comparación de diseños puede ser visual, pero no se presentará como un changeset de AWS.

## Valor como plataforma de interacción

**Para quien construye:** comprender cada objeto, decidir sus propiedades y materializar una arquitectura con menos fricción.

**Para quien aprende:** entender el propósito de un componente mientras lo define y conecta, con retroalimentación inmediata dentro del trabajo de diseño.

**Para quien presenta:** explicar una arquitectura manipulando sus objetos y mostrando relaciones, manteniendo la atención sobre lo que se está creando.

El resultado evaluable es un diseño claro, válido, recuperable y consistente con los recursos que el backend crea.

## Implementación propuesta

### Entrega 1 — Crear y definir mejor

Reorganización de paneles, catálogo plegable, colocación con previsualización, inspector contextual, nombres editables y selectores de propiedades. Conservar el contrato existente y corregir opciones visuales que no representen capacidades reales del backend.

Aceptación: completar una plantilla y definir sus objetos desde los controles; editar, cancelar y deshacer sin perder el diseño; leer el inspector en passthrough y mantener las interacciones existentes.

### Entrega 2 — Conectar y comprender

Puertos de conexión, compatibilidad contextual, significado de enlaces, validación junto al objeto, resaltado de dependencias y previsualización animada del flujo definido.

Aceptación: crear, inspeccionar y quitar conexiones sin ambigüedad; explicar errores concretos; no permitir que la animación se confunda con tráfico real o atraviese relaciones no definidas.

### Entrega 3 — Revisar, reutilizar y crear

Biblioteca local, organización de mesa, revisión de propiedades y recursos auxiliares, estado de diseño modificado y seguimiento del despliegue existente con acceso directo al objeto afectado.

Aceptación: guardar y recuperar el diseño, validar en servidor, confirmar una creación y asociar cada objeto a su resultado. Una edición espacial no cambia la definición desplegable; un conflicto de slot no provoca un borrado implícito.

## Base técnica y límites

Reutilizar `Architecture`, `NodeView`, `LinkView`, `LabRig`, `LabMenu`, `ArchitectureLab` y `AwsCloudApi`. Separar los formularios de propiedades, las reglas de compatibilidad y la presentación para que describan el mismo contrato.

Mantener Unity como frontend y el backend actual como responsable de validar y generar infraestructura. Las formas, paneles y cápsulas se construyen con componentes Unity y recursos propios, manteniendo una carga visual acotada. Validar legibilidad, selección, memoria y rendimiento en Quest 3, con objetivo de 72 Hz medido en el visor.

El alcance se considera cumplido cuando crear y definir una arquitectura se siente coherente de principio a fin: cada interacción modifica algo comprensible, cada propiedad tiene efecto real y cada recurso creado puede relacionarse con su objeto en la mesa.


## Estado de entrega — 0.5.0

Implementados los tres bloques en el proyecto Unity: colocación confirmada, catálogo plegable, definición con valores explícitos y nombres, teclado VR, ficha fija/contextual, puertos y conexiones con significado, explicación de incompatibilidad, previsualización finita de flujo, revisión por objeto y auxiliares, biblioteca local con miniaturas reconstruidas y distribución reversible de objetos.

El inspector conserva por defecto su ubicación fija para respetar los menús ya colocados. «Ayuda / ficha» permite acercarlo al objeto; moverlo manualmente vuelve a fijarlo. Los controles básicos de conexión también permanecen como alternativa a los puertos. La biblioteca conserva hasta 48 instantáneas de sólo diseño; los nombres visibles nunca se usan como rutas de archivos.

Los recursos auxiliares se describen por categoría según el compilador del backend; la UI no inventa un número exacto ni presenta esto como un changeset. No se añadieron dependencias ni nuevas propiedades al contrato de despliegue. Los cambios no crean infraestructura por sí solos. La confirmación AWS muestra API, región, slot y costo real; un slot ocupado sigue sujeto a la respuesta del backend.

La validación automatizada cubre cancelación de colocación, edición pendiente/aplicar/cancelar, bloqueo detrás del teclado, restricciones de enlaces, ausencia de paquetes en observabilidad, orden y duración de la previsualización, persistencia local, deshacer disposición sin invalidar despliegue, credenciales y flujos AWS con transporte de prueba. La comprobación visual usa capturas renderizadas por Unity. Legibilidad en passthrough, comodidad física y rendimiento sostenido requieren la verificación dentro del visor; el objetivo de 72 Hz no se presenta como resultado medido.

Evidencia de entrega: `Validation/studio-release-0.5.0.json`. Resultado: 35 pruebas EditMode y 15 PlayMode aprobadas; APK ARM64 0.5.0 (código 5), firma v2 verificada, build sin errores y con 9 advertencias de Unity/paquetes. Instalación sobre la versión anterior completada en Quest 3. Meta detuvo el arranque en el aviso de controles requeridos; la aceptación física y el rendimiento sostenido quedan pendientes. No se creó infraestructura AWS en esta validación.
