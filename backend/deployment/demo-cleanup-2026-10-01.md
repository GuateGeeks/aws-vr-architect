# Revisión y limpieza de la demo — 1 de octubre de 2026

Limpieza solicitada por el usuario, completada aproximadamente a las **20:07, hora de Guatemala**, en la cuenta `590183968738`, región `us-east-1`.

## Encontrado

El stack `ggawsday-demo-1` estaba en `CREATE_COMPLETE`, etiquetado `Project=GuateGeeksAWS2026`, sin eventos de fallo. Contenía una arquitectura HTTP API → Lambda → DynamoDB y ocho recursos de CloudFormation:

- HTTP API `k2k4l7rask`, integración Lambda, ruta `POST /demo` y stage `$default`.
- Función `ggawsday-demo-1-nd316b65e313247df41a4` y permiso de invocación de API Gateway.
- Tabla `ggawsday-demo-1-nefc5928933c589a92b39`.
- Grupo de logs de esa función.

Los slots 2 y 3 ya estaban vacíos. No había buckets S3 en esta arquitectura. No se leyeron registros de la tabla ni payloads de los eventos.

## Acción y comprobación

Se eliminó el slot 1 mediante `tools/demo.py delete`, usando la API del backend y esperando la finalización. La eliminación incluye los datos de la tabla y los logs de prueba.

La consulta posterior confirmó ausencia de los tres stacks de demo y de grupos de logs con su prefijo. Las consultas directas a API Gateway V2, Lambda y DynamoDB también devolvieron recurso inexistente para los identificadores anteriores. Las rutas, integración, stage y permiso subordinados se eliminaron con sus recursos de CloudFormation.

Evidencia:

- [Inventario anterior](demo-inventory-before-cleanup-2026-10-01.json).
- [Inventario posterior](demo-inventory-latest.json).
- [Verificación directa](demo-cleanup-verification.json).

## Conservado

El backend `guategeeks-aws2026` continúa en `CREATE_COMPLETE`: API de control, Lambda, roles IAM compartidos, secreto de autenticación, logs y alarmas de control. También se conserva su bucket privado de artefactos. Por tanto, esta limpieza libera las arquitecturas de la demo; **no elimina todos los recursos AWS del proyecto**.

El visor puede conservar el dibujo o un estado visual anterior hasta consultar de nuevo la API. Los datos locales del diseño no se borraron y se pueden reutilizar para un nuevo despliegue.

Se preparó por separado el [plan para conectar sin USB](../../GuateGeeksAWSVR/Documentation/Plan-Conexion-Sin-USB.md). El plan no se implementó ni se modificó la aplicación instalada durante esta limpieza.
