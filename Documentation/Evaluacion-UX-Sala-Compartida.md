# Evaluación integral de UX: fluidez, interacción y sala para 4 personas

**Versión evaluada:** 0.20.0 (Quest 3). **Versión con mejoras:** 0.21.0.
**Enfoque:** usabilidad, fluidez ("smooth"), experiencia visual y de interacción, y una interfaz preparada para **hasta 4 personas en la misma sala** alrededor de la mesa virtual. La red entre visores llega en una fase posterior; esta versión deja la interfaz, las reglas y el contrato de software listos para conectarla.

Capturas de referencia: `Validation/44-shared-room-settings.png`, `45-shared-room-console.png` y `46-simulated-teammates.png`, frente a `01-lab-overview.png` (modo individual).

## 1. Método

Se aplicaron las heurísticas de Nielsen adaptadas a RV, con cinco criterios propios de un visor:

| Criterio | Cómo se mide aquí |
|---|---|
| **Legibilidad angular** | Altura de la letra (em) en grados de visión. El objetivo del proyecto es **≥ 1.2°**, unos 30 px en Quest 3 (≈ 25 px por grado). |
| **Alcance y ergonomía** | Los paneles de uso frecuente deben estar a ±40° del frente y a menos de 1.4 m. Los controles primarios deben quedar a la altura de la cintura o el pecho. |
| **Comodidad** | Sin movimiento de cámara no iniciado por la persona y sin giros forzados. Todo debe poder pausarse con *Animación: NO*. |
| **Fluidez** | A 72 Hz cada cuadro dura 13.9 ms. No debe haber asignaciones de memoria por cuadro en el bucle de interacción (evitan pausas del recolector) y el *overdraw* transparente debe ser contenido. |
| **Convivencia** | Con 4 personas, ningún panel personal debe invadir el sector de un vecino, cada persona tiene que saber dónde pararse y debe verse quién hace qué. |

Severidad: **0** no es problema, **1** cosmético, **2** menor, **3** mayor, **4** bloqueante para el objetivo.

## 2. Hallazgos en 0.20.0

### 2.1 Fluidez y rendimiento

| # | Hallazgo | Sev. | Estado en 0.21 |
|---|---|---|---|
| F1 | Se asignaba memoria en cada cuadro: `LabVisuals.Beam()` construía una llave de texto (`"Beam+" + hex`) en cada consulta. Lo llamaban todos los nodos (anillo de selección), cada traza de la mesa y cada conexión, unas 30 o más veces por cuadro. Esa basura provoca pausas periódicas del recolector, que en un visor se sienten como tirones. | 3 | **Corregido**: caché con llaves de valor `(Color, bool)` y `(shader, Color)`. Una prueba verifica **0 bytes** asignados. |
| F2 | `LinkView` recalculaba la curva de 25 puntos y la flecha en cada cuadro aunque nada se moviera. Durante la vista previa además concatenaba `"SIMULACIÓN · " + texto`, lo que obligaba a TMP a regenerar la malla del texto en cada cuadro. | 2 | **Corregido**: la curva solo se recalcula si un extremo se mueve o cambia de escala, y el texto de simulación queda en caché. |
| F3 | Las trazas de la mesa y el estado de los nodos convertían colores hexadecimales (`Hex("#…")`) en cada cuadro y reasignaban el material siempre. | 1 | **Corregido**: colores estáticos y el material solo se asigna cuando cambia. |
| F4 | El renderizado foveado estaba apagado. En Quest 3 reducir la resolución periférica es la medida de GPU más rentable. | 3 | **Corregido**: se activa la función *Foveated Rendering* de OpenXR (Android) y `foveatedRenderingLevel = 1` al iniciar XR. Es foveado fijo porque Quest 3 no tiene seguimiento ocular. |
| F5 | El SSAO está activo en el *renderer* de PC. | 0 | Sin cambio: Android usa el *renderer* Mobile, que no tiene efectos. |
| F6 | El *overdraw* transparente (conos de proyección, haces, paneles de cristal) no se ha medido en el visor. | 2 | **Pendiente**: medir con OVR Metrics Tool (ver §6). |

### 2.2 Ergonomía y legibilidad (modo individual)

| Superficie | Distancia | Letra (em) | Ángulo | Lectura |
|---|---|---|---|---|
| Mesa de control (botones 22 px, escala .0017) | 1.77 m | 3.7 cm | **1.21°** | Al límite y fuera del alcance de la mano. |
| Catálogo e inspector (22 px, escala .002) | 3.42 m | 4.4 cm | **0.74°** | Pequeño y obliga a girar unos 36°. |
| Barra de estado (24 px, escala .0017) | 4.50 m | 4.1 cm | **0.52°** | **Difícil de leer**. La línea de conteos (15 px) queda en 0.32°. |

- **E1 (sev. 3):** la barra de estado, que es el canal principal de retroalimentación, queda a 4.5 m, detrás de la mesa. En modo individual se mantiene sin cambios para no alterar el diseño conocido. **La consola compacta de la sala compartida resuelve el problema (§4.2) y se recomienda para el evento aunque haya una sola persona.**
- **E2 (sev. 2):** la mesa de control queda a 1.7 m, así que solo se usa con el rayo. Con la consola compacta queda a 0.7 m, al alcance del toque directo.
- **E3 (sev. 2):** un rayo de 2 mm sobre botones de 8 cm a 2 m es sensible al temblor de la mano. Se corrigió con **asistencia de puntería** (§4.5).

### 2.3 Preparación para 4 personas (misma sala)

| # | Hallazgo | Sev. |
|---|---|---|
| M1 | La interfaz suponía una sola persona al frente. Catálogo, inspector, estado y ATLAS se reparten en un arco de unos 4 m. Con 4 personas, los paneles de cada una flotarían **frente a la cara de su vecino**. | 4 |
| M2 | No existía el concepto de **estación**: nadie sabía dónde pararse ni hacia dónde mirar, y no había forma de alinear varios visores en el mismo espacio físico. | 4 |
| M3 | El **giro con el stick** rompe la coincidencia entre el espacio físico y el virtual, así que dos personas que se ven en el mismo lugar acabarían en lugares distintos. | 3 |
| M4 | El inspector "contextual" **volaba hasta el objeto** seleccionado, que podía estar en el sector de otra persona. | 3 |
| M5 | No había **presencia** (quién está, dónde mira, qué señala) ni **propiedad de objetos**, así que dos personas podían mover o editar el mismo recurso a la vez. | 4 |
| M6 | El logotipo del evento solo se leía de frente: desde el lado opuesto aparecía al revés. | 2 |
| M7 | No había aviso si alguien salía de su zona; en una sala compartida eso es riesgo de choque. | 3 |
| M8 | Los colores de usuario podían confundirse con los semánticos (verde = éxito, rojo = error). | 2 |

### 2.4 Visual e interacción (general)

- **V1 (sev. 1):** la jerarquía visual del rediseño anterior se sostiene: una fila primaria iluminada, acciones secundarias "fantasma" y el anillo contextual en el objeto.
- **V2 (sev. 2):** faltaba retroalimentación para las acciones bloqueadas. Ahora el intento de editar un objeto ajeno da un mensaje claro y el sonido de error.
- **V3 (sev. 2):** con 4 personas hablando, ATLAS por voz abierta captará conversaciones cruzadas. Se recomienda presionar para hablar en la sala compartida (pendiente, §7).

## 3. Diseño de la sala compartida

```
                     Estación 3 (violeta)
                            ●
                            │
                    ╭───────────────╮
  Estación 2 (ámbar) ●──┤  MESA  Ø3.9 m  ├──● Estación 4 (lima)
                    ╰───────────────╯
                            │
                            ●
                     Estación 1 (cian)
     Centro de la mesa (0, 2.65) · estaciones a 2.6 m · zona personal Ø1.5 m
```

- **Estaciones:** 4 círculos numerados en el piso, separados 90° y a 2.6 m del centro, con un chevrón que indica hacia dónde mirar. Cada uno tiene color **y número**, para que el color no sea la única señal. Los colores son cian, ámbar, violeta y lima, distintos del verde de éxito y del rojo de error.
- **Huella física:** con la mesa actual (radio de 1.93 m), el área útil es un círculo de unos 6.7 m. Para un stand más pequeño, ver §7.
- **Espacio compartido vs. personal:** la mesa, los hologramas, las estaciones, el logotipo y los compañeros son compartidos. Cada persona tiene una **consola personal**: catálogo, mesa de control, inspector, estado, ajustes, ATLAS, código, guía y diagnóstico. Esa consola gira a su estación y nadie más la ve.

## 4. Implementado en 0.21.0

### 4.1 Estaciones y alineación (`SharedSpace`)
- En *Ajustes → Sala compartida* se encuentran: el interruptor de la sala, los botones **Estación 1–4**, **Alinear a mi estación** y **Simular 4 usuarios**.
- **Alinear** hace coincidir la pose física de la cabeza con la estación elegida, mirando al centro de la mesa. Así se alinean los visores en la misma sala sin anclas de red: todos se paran en su círculo, miran al centro y pulsan el botón.
- En sala compartida **se desactiva el giro con stick**. En escritorio, la cámara se coloca 1 m detrás de la estación.
- **Guardia de zona:** si la cabeza sale del círculo (más de 0.75 m del centro de la estación) durante más de 0.6 s, aparece un aviso frente a la vista, "FUERA DE TU ESTACIÓN · VUELVE AL CÍRCULO n", y el anillo se pone rojo.

### 4.2 Consola personal compacta
- Todos los paneles personales viven bajo `Personal console`, que rota alrededor del centro de la mesa según la estación.
- En sala compartida, mesa de control, estado, catálogo e inspector tienen poses ajustadas a mano. El resto conserva su dirección y su **tamaño angular** (escala proporcional a la distancia), limitado a **±40°** y entre **0.75 y 1.35 m**.
- Las posiciones guardadas por la persona se guardan **por distribución**: la individual y la compartida no se pisan, y *Restaurar paneles* vuelve a la de cada modo.

| Superficie (consola compacta) | Distancia | Ángulo de letra | Mejora |
|---|---|---|---|
| Mesa de control (escala .001) | 0.91 m | **1.39°** | ×1.15, al alcance de la mano |
| Catálogo e inspector (escala .00105) | 1.07 m | **1.24°** | ×1.7, a ±51° |
| Barra de estado | 0.88 m | **1.75°** | ×3.4 (conteos 1.09°) |

- El inspector ya no vuela al objeto en sala compartida (M4). El anillo contextual sigue en el objeto.
- El logotipo del evento flota sobre el centro de la mesa y **gira hacia cada espectador** (M6).

### 4.3 Presencia (`PeerAvatars`)
- Cada compañero aparece como un visor de grafito con los **ojos de GuateGeeks**, cuyas pupilas miran hacia donde mira esa persona. Lleva un halo y un cuerpo tenue en su color, una etiqueta con número y nombre que gira hacia ti, y una línea de actividad (OBSERVA / SEÑALA / EDITA).
- Al señalar, dibuja un **haz en su color** con un punto en el objetivo.
- Una **franja de equipo** en la mesa de control muestra "EQUIPO ● 1 TÚ ● 2 ANA · EDITA … · RED SIMULADA".

### 4.4 Propiedad de objetos (bloqueos)
- Seleccionar o tomar un objeto lo **reclama** para editarlo en exclusiva.
- Un objeto reclamado por otra persona muestra un anillo giratorio en su color y la etiqueta "EN USO · NOMBRE". **No se puede seleccionar para editar ni mover**, y aparece un mensaje claro.
- Sí puede usarse como origen o destino de una **conexión**, porque conectarlo no lo modifica.

### 4.5 Interacción más precisa
- **Asistencia de puntería:** si el rayo falla por poco, una esfera de 2.5 cm a lo largo del rayo encuentra el botón o puerto disponible más cercano. Un fallo claro sigue siendo un fallo.

### 4.6 Contrato para la red (`ICollabSession`)
- Toda la interfaz (avatares, franja, bloqueos) habla con una sola interfaz: pares, versión, `LockOwner`, `Claim`, `SetLocalStation` y `Tick`.
- Hoy existen dos implementaciones. `LocalCollabSession` es el valor por defecto, con un solo visor. `SimulatedCollabSession` crea 3 compañeros deterministas para ensayar y probar; **nunca modifican el diseño**.

## 5. Arquitectura de red propuesta (siguiente fase)

Para un evento de AWS, la opción coherente es mostrar AWS en acción:

| Pieza | Propuesta |
|---|---|
| Transporte | **API Gateway WebSocket API** (`wss://`) con rutas `$connect` (código de sala y estación), `$disconnect`, `presence`, `claim`, `release` y `op`. |
| Lógica | **Lambda** por ruta. La difusión a los ≤ 4 miembros de la sala se hace con `PostToConnection`. |
| Estado | **DynamoDB**. Tabla `Rooms`: conexiones, estación y nombre, con TTL. Tabla `Locks`: `PK = sala#objeto`, escritura condicional `attribute_not_exists(owner) OR owner = :yo OR expiresAt < :ahora`, con un latido cada 5 s y TTL de 15 s, para que un visor que se apaga no deje bloqueos huérfanos. Tabla `Ops`: operaciones de diseño con número de secuencia (concurrencia optimista sobre la revisión). |
| Presencia | Cabeza y manos cuantizadas, a 10–15 Hz, interpoladas en el cliente con un búfer de unos 100 ms. No pasan por DynamoDB, solo se reenvían. |
| Interfaz | El `Claim` es optimista: se muestra "reclamando…" hasta el acuse, y se revierte si el servidor lo rechaza. |
| Despliegue | Un bloqueo de sala `deploy`: solo una persona confirma el despliegue y las demás ven el progreso. |
| Deshacer | Cada persona deshace **sus propias** operaciones, no las de otros. |
| Co-ubicación | Hoy la alineación es manual por estación; el error esperado es del orden de 10–20 cm y unos 5°. Siguiente paso: anclas espaciales compartidas o *colocation discovery* de Meta, por verificar con el paquete OpenXR de Meta que usa el proyecto. |
| Alternativa | Red LAN local (por ejemplo Netcode for GameObjects) si el Wi-Fi del stand no garantiza salida a internet. `ICollabSession` admite ambas. |

## 6. Validación

- **Editor (Unity 6000.6.3f1, 5 de octubre de 2026):** EditMode **74/74** y PlayMode **56/56**; los resultados están en `Validation/design-0.21.0-*-results.xml`. Pruebas nuevas:
  - `SharedRoomCompactsAndTurnsThePersonalConsoleToEachStation`: sector de ±55°, alcance de 1.4 m, que ningún panel quede más cerca de un vecino, rotación a la estación 3, logotipo hacia el espectador, guardia de zona y restauración exacta del modo individual.
  - `SimulatedTeammatesShowPresenceAndTheirLocksBlockEditing`: 3 compañeros en las otras estaciones, franja de equipo, reclamo al seleccionar, bloqueo de seleccionar y mover, y que una conexión siga permitida.
  - `AimAssistForgivesNearMissesAndBeamsDoNotAllocate`: un fallo de 1.5 cm acierta, uno de 12 cm sigue fallando y la caché de haces asigna 0 bytes.
- **En el visor (pendiente de medir):**
  1. Con OVR Metrics Tool, el tiempo de GPU y CPU por cuadro en la vista más cargada (plantilla de 6 o más recursos y vista previa de flujo), con y sin foveado.
  2. Legibilidad real de la consola compacta a 0.9–1.1 m.
  3. Ensayo con 4 personas: alinear las cuatro estaciones y comprobar la deriva tras 10 minutos.

## 7. Pendiente y riesgos

1. **Red real:** implementar `ICollabSession` sobre WebSocket (§5). Hoy cada visor está solo o con compañeros simulados.
2. **Tamaño del stand:** la huella de unos 6.7 m depende del radio de la mesa (1.93 m). Para stands de 4–5 m se propone una opción de *mesa compacta* (escala de mesa y espacio de trabajo de 0.7, con estaciones a 1.9 m).
3. **ATLAS en sala compartida:** presionar para hablar, un indicador de quién habla y voz espacial desde el avatar.
4. **Modo individual:** adoptar la consola compacta como opción por defecto en la siguiente versión, después de validarla en el visor (E1).
5. **Accesibilidad:** probar la combinación de colores con simulación de daltonismo. El número ya acompaña al color siempre.

## 8. Revisión del plan tras evaluar la interfaz 0.21.0 (5 de octubre de 2026)

Esta revisión sustituye la recomendación inicial de comenzar con 4–8 usuarios y de exigir un servidor dedicado desde el primer piloto. Se revisaron el código actual, las capturas 45 y 46 y los reportes XML existentes. No se ejecutaron pruebas nuevas ni una sesión real con varios visores. Los reportes guardados registran 74/74 EditMode y 56/56 PlayMode; no prueban sincronización, latencia, deriva o rendimiento sostenido en Quest.

### 8.1 Evaluación y alcance

- Conservar las cuatro estaciones, la consola personal, la franja de equipo y los avatares. Ya ofrecen una base útil para colaboración. El primer piloto será de **cuatro usuarios por sala**, acorde con `SharedSpace.MaxStations`.
- La colaboración actual es local o simulada. Los compañeros simulados no alteran el grafo; activar «Sala compartida» no conecta visores. La interfaz debe distinguir claramente «Distribución para 4», «Ensayo simulado», «Conectando» y «Sala conectada».
- La consola compacta acerca la información y organiza los controles. En la captura 46 se superponen nombres, puertos y acciones de varios objetos vistos desde otra estación. Antes del piloto, verificar los cuatro puntos de vista y priorizar etiquetas del objeto enfocado; usar desplazamientos de etiquetas y ocultar controles personales no necesarios. La densidad de líneas y transparencias requiere medición en Quest, no una conclusión de rendimiento basada en capturas.
- Hay dos escenarios: usuarios físicamente juntos y usuarios remotos en el mismo mundo. Solo el primero necesita coincidencia de coordenadas físicas. Las estaciones virtuales no establecen por sí solas una alineación física común. Confirmar dimensiones del stand, marcar estaciones reales y medir el error; el rango de 10–20 cm mencionado antes es una hipótesis, no una medición.

### 8.2 Cambios necesarios en el contrato y las reglas

1. **Inspeccionar sin bloquear.** `ClaimSelection()` reclama al seleccionar y `Select()` rechaza objetos ajenos. La selección debe ser privada y permitir lectura; adquirir un bloqueo únicamente al comenzar a mover o editar. Liberarlo al terminar, cancelar o perder la conexión.
2. **Reclamo asíncrono.** `ICollabSession.Claim()` devuelve hoy un `bool` inmediato. La red necesita estados pendiente/aceptado/rechazado, un identificador de solicitud y una concesión con vencimiento. No confirmar una modificación hasta recibir autorización. Cambiar estación también debe requerir reserva exclusiva del servidor.
3. **Separar presencia de operaciones.** El contrato actual expone pares y bloqueos, pero no envío de poses, comandos de diseño, instantáneas, revisiones, reconexión ni resultados. Incorporar esos contratos y adaptar los puntos que hoy mutan `Graph` directamente; sustituir únicamente la implementación de `ICollabSession` no basta.
4. **Una autoridad para todas las escrituras.** UI, agarres, conexiones, ATLAS, plantillas, carga, limpieza y ordenado deben usar comandos validados con identidad, sala, revisión y clave de idempotencia. Validar también conexiones a objetos bloqueados: una conexión modifica el grafo y puede alterar las restricciones de sus extremos aunque no cambie sus propiedades.
5. **Deshacer por operación.** El historial actual restaura instantáneas completas. En sala conectada, usar operaciones inversas del usuario con comprobación de conflictos; nunca restaurar una instantánea que borre cambios posteriores de otro participante. Cargar, limpiar o aplicar una propuesta completa debe ser una operación de sala autorizada y revisada.
6. **Bloqueos con vencimiento lógico.** Comparar `expiresAt` con la hora del servidor en lecturas y escrituras condicionales. TTL sirve para limpiar registros: DynamoDB puede tardar días en eliminarlos. Añadir un token de concesión para rechazar operaciones o liberaciones de antiguos propietarios.
7. **Identidad y permisos.** El código de sala no sustituye autenticación. Asociar conexión a usuario verificado y comprobar membresía/rol en cada comando. En API Gateway WebSocket, el authorizer de Lambda opera en `$connect`; la autorización de operaciones posteriores debe implementarse en el backend.

Fuentes: [TTL de DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/TTL.html), [authorizers WebSocket](https://docs.aws.amazon.com/apigateway/latest/developerguide/apigateway-websocket-api-lambda-auth.html).

### 8.3 ATLAS privado en un mundo común

- Una sesión Realtime por usuario, con credencial temporal individual. Audio, subtítulos, historial, selección y borradores permanecen privados; compartir solo operaciones aceptadas y, si se habilita, un indicador de actividad sin transcripciones.
- En co-ubicación, usar presionar para hablar como opción inicial y auriculares; ensayar que la voz de un vecino no dispare acciones. Mantener conversación abierta como opción personal después de validarla.
- `AnimateAssistantCore()` controla actualmente el reactor mediante el estado local de ATLAS. Mantener la señal privada en la consola personal y reservar el reactor central para estado compartido de sala/despliegue, o señalar explícitamente que su representación es personal. No transmitir al reactor común audio ni actividad privada por defecto.
- Las previsualizaciones son privadas. Antes de aplicar, volver a comprobar revisión, permisos y concesiones de todos los objetos afectados. Las operaciones AWS conservan revisión explícita y confirmación del usuario autorizado.

### 8.4 Despliegue propuesto y orden de entrega

**Piloto AWS de cuatro personas:** evaluar API Gateway WebSocket + Lambda + DynamoDB, aprovechando la propuesta de la sección 5. Es una opción para un taller pequeño con cambios discretos, condicionada a medir fluidez de presencia. Comenzar poses a 10 Hz, interpolar localmente y no persistir poses en DynamoDB. Limitar tasa y tamaño de mensajes, evitar logs de cada pose y prever caída/reconexión. Si no cumple los objetivos medidos, usar Netcode/Relay o una autoridad persistente para presencia; no instalar ambos transportes antes de ese ensayo. Una alternativa LAN mantiene la colaboración local, pero ATLAS sigue necesitando internet.

| Fase | Entrega y criterio de aceptación |
|---|---|
| 1. Contratos y UX | Inspección libre, edición con bloqueo asíncrono, estados honestos de conexión, selección/propuestas privadas, reserva de estación y botones de sala según rol. |
| 2. Dos visores reales | Identidad y sala comunes, instantánea inicial, poses, comando aceptado visible en ambos, orden por revisión y recuperación tras desconexión. |
| 3. Cuatro participantes | Competencia por el mismo objeto y estación, bloqueo vencido, comando duplicado, propuesta obsoleta, deshacer con cambios ajenos y acciones globales. |
| 4. ATLAS simultáneo | Conversaciones independientes, ausencia de audio/subtítulos cruzados, dos solicitudes de edición concurrentes y consumo por usuario. |
| 5. Validación del evento | Lectura desde las cuatro estaciones, perfil CPU/GPU frente al objetivo de 72 Hz (13.9 ms), alineación y deriva medidas durante 10 minutos, reconexión y despliegue único de una revisión aprobada. |

Desconectarse no debe convertir silenciosamente un visor en editor de la sala compartida. Permitir inspección y, opcionalmente, una copia local explícita; al reconectar recibir estado autoritativo y resolver borradores pendientes.

### 8.5 Ajuste del presupuesto

El servidor dedicado de USD 30–60/mes sigue siendo una alternativa; no es un requisito del piloto serverless. Para cuatro usuarios, una pose por usuario a 10 Hz reenviada individualmente a los otros tres implica 40 entradas + 120 salidas por segundo: **576,000 mensajes por hora de sala**. A 15 Hz son 864,000. Este cálculo asume mensajes inferiores a 32 KB, difusión individual y ninguna agregación; no incluye operaciones, latidos ni reintentos.

API Gateway cobra mensajes enviados y recibidos y minutos de conexión; también se deben presupuestar Lambda, lecturas/escrituras de estado, transferencia y logs. Por ello, el costo debe estimarse por **horas de sala y frecuencia**, no solo por participantes. No asumir créditos o capa gratuita de la cuenta existente. Fuente: [precios de API Gateway](https://aws.amazon.com/api-gateway/pricing/).

El consumo de ATLAS continúa separado y depende de habla, respuestas, contexto y herramientas. La provisión anterior de USD 1–2 por participante para una experiencia de 30 minutos con cinco minutos de habla de cada lado sigue siendo una reserva orientativa, no una tarifa o límite garantizado. Antes de fijar presupuesto, medir una sesión de cuatro usuarios y confirmar horas mensuales, tamaño del stand y máximo de salas simultáneas.

## 9. Implementación del piloto de cuatro usuarios (5 de octubre de 2026)

El proyecto incorpora el cliente `NetworkCollabSession`, integración de sala en `ArchitectureLab.Network`, rutas REST de entrada y tickets, y autoridad WebSocket en `backend/src/collaboration.py`. Se conserva la UI de estaciones y consola personal existente. La implementación requiere desplegar la plantilla actualizada y reconstruir el cliente; activar solo la distribución local no conecta visores.

### Contrato implementado

- «Crear sala real» entrega un código de ocho caracteres. «Unirme con código» asigna a cada visor identidad, token y estación exclusivos. Hay cuatro plazas; el creador es facilitador y los invitados son editores. La credencial del evento permite entrar, y el token opaco individual permite operar dentro de la sala. Son identidades temporales para un taller, sin cuentas personales duraderas.
- El servidor conserva el grafo y revisión en DynamoDB, con escrituras condicionales y relectura ante competencia. Cada operación tiene identificador y revisión base. Los clientes restauran el estado confirmado mientras esperan; una respuesta incierta provoca lectura y reconexión, sin reenvío automático de la edición.
- La selección es privada y no bloquea. El agarre espera una concesión del servidor y solo permite mover un objeto a la vez por visor. Las concesiones y membresías duran 20 segundos y se renuevan mediante latidos cada cinco segundos. Una concesión recibida después de soltar el agarre se libera si permanece sin uso. El servidor comprueba propietario, vencimiento y token de concesión, incluidos extremos de enlaces modificados.
- Deshacer revierte la última operación propia conservada, con comprobación de cambios posteriores en los objetos y enlaces afectados. La ventana es de ocho operaciones de sala y 64 recibos de idempotencia; no es historial ilimitado. Cargar plantillas, sustituir el diseño y limpiar la mesa son acciones globales de facilitador. El servidor también impide borrar múltiples objetos a un editor aunque omita el indicador global.
- Los tickets de conexión son de un uso y vencen en 60 segundos. Las salas y tokens duran dos horas. Reconectar conserva identidad mientras siga vigente y obtiene otra instantánea; una conexión anterior queda excluida, también durante reintentos de escritura. Perder confirmaciones durante 12 segundos convierte el visor en lectura. «Salir de la sala» conserva una copia local explícita.
- Se transmiten cabeza y una mano a 10 Hz con interpolación local. No se transmite selección privada ni puntero por defecto. Las poses, voz, transcripciones y secretos de ATLAS no se persisten en la sala. Cada participante obtiene su propia credencial Realtime y conversación; la cuota del broker añade un máximo de tres credenciales por usuario/hora, además de la cuota global existente. Estas cuotas no limitan por sí solas el gasto total en tokens o minutos de una sesión.
- En sala real, el micrófono se abre con A en el mando derecho o Espacio en escritorio. El botón «Hablar» permite alternarlo para manos; «Silenciar mic» lo cierra. Al cerrar la entrada se descartan muestras pendientes y se envía silencio para cerrar el turno VAD. Validar acústica real y usar auriculares durante el piloto.
- El backend limita despliegue, eliminación y publicación/restauración de código AWS al facilitador. El slot se reserva para la sala. El despliegue exige la definición de la revisión compartida y objetos liberados; bloquea ediciones mientras está en curso. El estado se consulta desde AWS y se distribuye por instantáneas; otros visores pueden seguir consultándolo si el facilitador se desconecta. Cambiar solo posiciones o escalas mantiene la asociación con la definición desplegada.

### Activación y comprobación del piloto

1. Regenerar `backend/template.json` con `python backend/tools/build_template.py`, ejecutar la suite Python, y ejecutar `sam build --template-file template.json` desde `backend`.
2. Revisar y desplegar el cambio SAM existente en la misma cuenta/región/prefijo. Añade tabla DynamoDB con TTL, API Gateway WebSocket, Lambda de salas, permisos restringidos y logs de siete días. La configuración inicial limita la API a 60 solicitudes/segundo y la Lambda a diez ejecuciones concurrentes: validar una sala activa de cuatro personas antes de aumentar salas simultáneas. La salida `CollabWebSocketUrl` se entrega al cliente automáticamente durante entrada; no requiere copiar otra URL al visor.
3. Mantener `AllowedCidr` restringido al egreso IPv4 del taller. Para usuarios remotos, configurar explícitamente su red permitida o ampliar el mecanismo de autenticación; el piloto no elimina la restricción IP existente. Conservar el secreto de OpenAI en Secrets Manager y el modelo ya validado en la configuración del despliegue.
4. Construir el APK Quest e instalar la misma versión en dos visores. Conectar ambos al mismo backend del evento; crear sala en uno, ingresar el código en el otro y alinear cada estación. Comprobar edición, competencia por objeto, propuesta vencida, deshacer, pérdida/reconexión de red y estado AWS.
5. Repetir con cuatro visores y ATLAS simultáneo. Medir latencia, fluidez, costo real, 72 Hz y alineación física antes de aprobar uso público. Las pruebas automáticas de transición y cliente no sustituyen esta validación.

No se ha realizado un despliegue de estos recursos AWS ni una prueba de sala con varios visores durante esta implementación. La arquitectura serverless del apartado 8.5 sigue siendo el presupuesto aplicable; no requiere alquilar un servidor dedicado para este piloto.

### Validación automática realizada

- Backend: 102 pruebas aprobadas. Incluye transición de grafo, escrituras condicionales con relectura, aislamiento de identidad, tickets de un uso, vencimiento, competencia por objetos, revisión obsoleta, idempotencia, deshacer con cambios ajenos y permisos de despliegue/slots. La prueba de estado AWS comprueba que otra definición no se reporte como despliegue exitoso de la sala.
- Unity EditMode: 81 pruebas aprobadas, incluidas concesiones asíncronas, recepción fuera de orden, pérdida de conexión, privacidad de presencia y muestras de micrófono en silencio cuando se cierra la entrada.
- Unity PlayMode: 57 pruebas aprobadas. La prueba nueva integra `ArchitectureLab` con instantáneas simuladas del transporte y verifica restauración de candidatos pendientes, aceptación de recibo y bloqueo de cambios sin conexión. No abre WebSocket real ni conecta visores.
- `sam validate --lint`: plantilla válida. `sam build`: empaquetado exitoso con Python 3.13 existente. Los módulos Python de las tres funciones empaquetadas fueron actualizados desde el código final y comparados por SHA-256.
- Revisión visual de la captura 44: crear, ingresar código, reconectar y salir caben en la consola existente. La legibilidad física, audio simultáneo, transporte real y rendimiento de cuatro Quest siguen pendientes del piloto.

- APK final 0.22.0 (versionCode 24), ARM64: compilación Android exitosa, cero errores y diez avisos de compilación. No se instaló en el visor durante esta implementación. Evidencia: `Validation/collaboration-validation.txt`, `Validation/collaboration-source-hashes.json` y `Validation/quest3-build.txt`.
