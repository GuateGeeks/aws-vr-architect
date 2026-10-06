# Validación del backend · 1 de octubre de 2026

**Actualización del 5 de octubre de 2026:** se retiraron las restricciones por IP de API Gateway y Lambda por solicitud y confirmación del usuario. La suite actual tiene 104 pruebas aprobadas; la comprobación desplegada devuelve 200 con credenciales y 401 sin ellas. Los ensayos de restricción/restauración de IP de la tabla siguiente son históricos. Véase [la verificación actual](../deployment/public-ip-access-verification.json).

Pruebas ejecutadas en Ubuntu WSL 2, con Python 3.13.13, AWS CLI 2.37.7, SAM CLI 1.166.2, boto3 1.40.45 y cfn-lint 1.40.2. Se creó infraestructura temporal real en us-east-1 bajo el prefijo `ggtdadccc23`, usando la sesión AWS existente mediante `awsday-test`. Esa identidad era root; no se crearon access keys permanentes.

**Ejecución completada y recursos temporales eliminados.** No quedó un backend permanente desplegado. El estado local registra `cleanupComplete: true`.

## Pruebas locales

- **35 pruebas unitarias aprobadas:** grafos, presets Unity, límites, ciclos, duplicados, compilación, conexiones, autenticación, caché, errores sin secretos, reintentos, conflictos, rollback y borrado S3 versionado/reanudable.
- Tres pruebas adicionales cubren IP no permitida con cabeceras falsificadas, contexto de origen ausente/inválido/IPv6 y CIDR ausente/inválido/abierto. Se verifica rechazo antes de autenticar o llamar a AWS.
- **11 plantillas con cero hallazgos de cfn-lint:** control plane, tres presets y siete variantes. La mayor ocupa 40.100 bytes, dentro del límite de 51.200 bytes del TemplateBody utilizado.
- Generación reproducible de template.json, SAM validate y SAM build nativo Python 3.13 aprobados. Docker no fue necesario.
- Comprobación final en WSL: 23 bloques Bash con sintaxis válida, fuentes Python válidas y seis rutas OpenAPI legibles. La revisión de sintaxis no ejecutó los comandos de las guías.

## Pruebas con AWS real

| Prueba | Resultado |
|---|---|
| Control plane SAM | Creación y actualizaciones correctas |
| Basic Auth | Ausente/incorrecta: 401; válida: sesión y catálogo de siete servicios |
| Grafo inválido | 400 |
| API → Lambda → DynamoDB | Despliegue, evento y lectura consistente por eventId correctos |
| EventBridge → SQS → Lambda → DynamoDB | Evento entregado hasta DynamoDB |
| S3 → Lambda → DynamoDB | Evento de archivo entregado hasta DynamoDB |
| Reintento y conflicto | Mismo diseño conserva stackId; otro diseño en slot ocupado devuelve 409 |
| Siete servicios juntos | Lambda 1024 MB, DynamoDB aprovisionado, S3 suspendido, SQS FIFO, EventBridge y dashboard CloudWatch |
| Fan-out | Escrituras reales DynamoDB/S3, mensaje correcto en FIFO y PutEvents sin fallo |
| Observabilidad | Dashboard con seis métricas configuradas; no se afirmó disponibilidad inmediata de puntos de datos |
| API de workload | Solicitud sin firma IAM rechazada con 403 |
| Limpieza de demos | Tres presets y arquitectura de siete servicios borrados mediante API, incluidos objetos S3 versionados |
| Restricción IP | Cambio de AllowedCidr devuelve 403 desde Lambda; X-Forwarded-For y X-Real-IP falsificadas no evitan el rechazo |
| Restauración de IP | Volver al CIDR original restaura la sesión autenticada |
| Rotación de contraseña | Tras 65 segundos, la credencial anterior devuelve 401 y la nueva permite la sesión |
| Limpieza final | Control plane y bucket dedicado eliminados; comprobación independiente sin stacks, Lambdas, roles, buckets, tablas, colas, buses, logs, dashboards, alarmas ni APIs con el prefijo de prueba |

## Defectos encontrados y cambios

1. Faltaba apigateway:TagResource para crear un stage HTTP API etiquetado. Se añadieron permisos explícitos de etiquetado al rol de aprovisionamiento y se verificó el despliegue real.
2. La nueva IP figuraba en la política REST y existía un deployment nuevo, pero el gateway seguía aceptando solicitudes desde la IP excluida durante la ventana probada. AlwaysDeploy y la propiedad nativa Policy no resolvieron por sí solos ese comportamiento. Se conserva la política, pero la restricción efectiva también se aplica dentro de Lambda mediante requestContext.identity.sourceIp, antes de Basic Auth. Nunca se confía en cabeceras de origen. **La prueba observó el rechazo en Lambda, no en el gateway.** No se determinó la causa del comportamiento del gateway; clientes bloqueados pueden alcanzar Lambda y generar invocaciones.
3. El perfil existente y copiar dependencias a /mnt/c impedían o ralentizaban herramientas WSL. Un perfil separado con credential process y región explícitos, Python 3.13 y artefactos en el sistema de archivos Linux resolvieron esos problemas.

## Repetir pruebas

Consulta [wsl-verification.md](wsl-verification.md) para los comandos locales. Estas fases crean y eliminan recursos reales; el operador debe elegir cuenta y perfil con permisos adecuados:

```bash
export PATH="$HOME/.local/bin:$PATH"
source "$HOME/.venvs/guategeeks-aws2026/bin/activate"
cd /mnt/c/Users/adawolfs/Unity/GuateGeeksAWS2026
read -rp 'AWS profile: ' TEST_PROFILE
read -rp 'AWS account ID: ' TEST_ACCOUNT
sam build --template-file template.json --build-dir "$HOME/.cache/guategeeks-aws2026/build"
python tools/live_test.py prepare --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT" \
  --build-template "$HOME/.cache/guategeeks-aws2026/build/template.yaml"
python tools/live_test.py exercise --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT"
python tools/live_test.py matrix --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT"
python tools/live_test.py network --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT"
python tools/live_test.py rotation --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT"
# Ejecutar también si falla alguna fase anterior:
python tools/live_test.py cleanup --profile "$TEST_PROFILE" --account "$TEST_ACCOUNT"
```

El estado y los resultados se guardan en test-results/live-state.json, excluido del control de versiones y sin contraseñas. Cada corrida completada se archiva al preparar la siguiente. diagnostics consulta fallos y logs; cleanup-demos permite retirar slots antes de reintentar.

## Límites de cobertura

No se probó Unity/Quest ni la red física del evento. No se hizo prueba de carga, auditoría exhaustiva de IAM, medición de costos, validación de todas las combinaciones posibles ni prueba del presupuesto opcional por correo. La cuota observada de Lambda era 10 ejecuciones concurrentes compartidas por control y workloads. Los recorridos reales seleccionados complementan las pruebas unitarias y de plantillas; no garantizan capacidad para un número de visores determinado.
