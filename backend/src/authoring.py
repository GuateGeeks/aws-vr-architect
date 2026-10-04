"""Reviewed, revision-bound code operations on Lambda nodes owned by a demo stack."""
import ast
import hashlib
import io
import json
import os
import re
import urllib.parse
import urllib.request
import zipfile
from botocore.exceptions import ClientError

MAX_SOURCE = 8192


def validate_source(source, api):
    if not isinstance(source, str) or not source.strip() or len(source.encode('utf-8')) > MAX_SOURCE:
        raise api.ApiError(400, 'invalid_code', 'Código Python requerido, máximo 8 KiB.')
    try:
        tree = ast.parse(source, filename='index.py')
        compile(tree, 'index.py', 'exec')  # Compile only: never execute draft code in the control plane.
    except (SyntaxError, ValueError, RecursionError) as error:
        line = getattr(error, 'lineno', 0) or 0
        raise api.ApiError(400, 'invalid_code', 'Python inválido, línea ' + str(line)) from None
    handlers = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'handler']
    if len(handlers) != 1 or len(handlers[0].args.args) != 2 or handlers[0].decorator_list:
        raise api.ApiError(400, 'invalid_handler', 'Define handler(event, context) sin decoradores.')
    return {'valid': True, 'sourceHash': hashlib.sha256(source.encode()).hexdigest(),
            'message': 'Sintaxis válida. La validación no ejecuta ni demuestra el comportamiento del código.'}


def test_draft(data, api):
    validation = validate_source(data.get('source'), api)
    try:
        raw = data.get('eventJson', '{}')
        if not isinstance(raw, str) or len(raw.encode()) > 4096:
            raise ValueError()
        event = json.loads(raw, parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
        if not isinstance(event, dict):
            raise ValueError()
    except (ValueError, TypeError):
        raise api.ApiError(400, 'invalid_test', 'El evento de prueba debe ser un objeto JSON de hasta 4 KiB.') from None
    result = api.client('lambda').invoke(FunctionName=os.environ['CODE_TEST_FUNCTION'], InvocationType='RequestResponse',
        Payload=json.dumps({'source': data['source'], 'event': event}).encode())
    with result['Payload'] as stream:
        raw_result = stream.read(16001)
    if result.get('FunctionError') or len(raw_result) > 16000:
        return dict(validation, passed=False, output='', logs='', message='La prueba aislada falló o excedió sus límites. No se cambió la Lambda desplegada.')
    report = json.loads(raw_result)
    return dict(validation, passed=bool(report.get('passed')), output=str(report.get('output', ''))[:6000],
                logs=str(report.get('logs', ''))[:2000], message=str(report.get('message', ''))[:300])


def resolve(cfn, slot, data, api):
    name = api.stack_name(slot)
    stack = api.get_stack(cfn, name)
    if not stack:
        raise api.ApiError(404, 'not_found', 'No existe el despliegue.')
    identity = data.get('stackId')
    if identity != stack['StackId']:
        raise api.ApiError(409, 'stack_changed', 'El slot cambió. Actualiza la sesión.')
    if stack['StackStatus'] != 'CREATE_COMPLETE':
        raise api.ApiError(409, 'not_ready', 'Espera a que el despliegue esté listo.')
    definition = api.template(cfn, identity)['Resources']
    matches = [(key, value) for key, value in definition.items()
               if value.get('Metadata', {}).get('NodeId') == data.get('resourceId')]
    if len(matches) != 1 or matches[0][1].get('Type') != 'AWS::Lambda::Function':
        raise api.ApiError(400, 'invalid_resource', 'Selecciona una Lambda de este diseño desplegado.')
    physical = next((r.get('PhysicalResourceId') for r in api.resources(cfn, identity)
                     if r['LogicalResourceId'] == matches[0][0]), None)
    if not physical or not physical.startswith(name + '-'):
        raise api.ApiError(400, 'invalid_resource', 'La función no pertenece a este slot.')
    return physical


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def source_from_function(function, api):
    location = function.get('Code', {}).get('Location', '')
    url = urllib.parse.urlsplit(location)
    if url.scheme != 'https' or not (url.hostname or '').endswith('.amazonaws.com'):
        raise api.ApiError(502, 'code_unavailable', 'AWS no devolvió un paquete compatible.')
    # Only the AWS-provided signed URL is used; it is never returned or logged.
    with urllib.request.build_opener(NoRedirect()).open(location, timeout=5) as response:
        package = response.read(1048577)
    if len(package) > 1048576:
        raise api.ApiError(400, 'package_too_large', 'El editor admite paquetes Python de un solo archivo de hasta 1 MiB.')
    try:
        with zipfile.ZipFile(io.BytesIO(package)) as archive:
            files = [n for n in archive.namelist() if not n.endswith('/')]
            if files != ['index.py'] or archive.getinfo('index.py').file_size > MAX_SOURCE:
                raise ValueError()
            source = archive.read('index.py').decode('utf-8')
    except (ValueError, KeyError, UnicodeError, zipfile.BadZipFile):
        raise api.ApiError(400, 'unsupported_package', 'El editor requiere un único index.py de hasta 8 KiB.') from None
    return source


def describe(function):
    value = function.get('Configuration', function)
    return {'revisionId': value.get('RevisionId', ''), 'codeSha256': value.get('CodeSha256', ''),
            'version': value.get('Version', '$LATEST'), 'updateStatus': value.get('LastUpdateStatus', 'Successful')}


def published_versions(client, physical, api):
    versions, marker = [], None
    for _ in range(10):
        page = client.list_versions_by_function(FunctionName=physical, MaxItems=50, **({'Marker': marker} if marker else {}))
        versions.extend(v for v in page.get('Versions', []) if str(v.get('Version', '')).isdigit())
        marker = page.get('NextMarker')
        if not marker:
            return sorted(versions, key=lambda v: int(v['Version']))
    raise api.ApiError(409, 'version_limit', 'Hay demasiadas versiones para una revisión acotada. Administra versiones en AWS antes de continuar.')


def read(cfn, slot, query, api):
    physical = resolve(cfn, slot, query, api)
    client = api.client('lambda')
    function = client.get_function(FunctionName=physical)
    versions = published_versions(client, physical, api)
    return dict(describe(function), source=source_from_function(function, api),
        versions=[{'version': v['Version'], 'description': v.get('Description', '')[:100]}
                  for v in versions][-10:],
        truncated=len(versions)>10,
        message='Publicar cambia el código activo de esta función. Los destinos e IAM conservan su configuración.')


def publish(cfn, slot, data, api, rollback=False):
    if data.get('confirmed') is not True:
        raise api.ApiError(400, 'confirmation_required', 'Confirma el cambio de código en el panel de revisión.')
    physical = resolve(cfn, slot, data, api)
    client = api.client('lambda')
    current = client.get_function_configuration(FunctionName=physical)
    revision = data.get('revisionId')
    if not revision or revision != current.get('RevisionId'):
        raise api.ApiError(409, 'code_changed', 'El código cambió. Carga la versión actual antes de publicar.')
    if current.get('LastUpdateStatus', 'Successful') != 'Successful':
        raise api.ApiError(409, 'update_pending', 'Espera a que termine la actualización anterior.')
    if rollback:
        version = data.get('version', '')
        if not isinstance(version, str) or not re.fullmatch(r'[1-9][0-9]{0,8}', version):
            raise api.ApiError(400, 'invalid_version', 'Elige una versión publicada de esta función.')
        source = source_from_function(client.get_function(FunctionName=physical, Qualifier=version), api)
    else:
        source = data.get('source')
    validation = validate_source(source, api)
    package = io.BytesIO()
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as archive:
        info = zipfile.ZipInfo('index.py', date_time=(2026, 1, 1, 0, 0, 0))
        archive.writestr(info, source.encode())
    try:
        # Preserve an immutable checkpoint before replacing $LATEST. Revision checks prevent
        # overwriting an external edit even if it races this checkpoint operation.
        versions = published_versions(client, physical, api)
        checkpoints = [v for v in versions if str(v.get('Version', '')).isdigit() and v.get('CodeSha256') == current['CodeSha256']]
        if checkpoints:
            previous = max(checkpoints, key=lambda v: int(v['Version']))
        else:
            previous = client.publish_version(FunctionName=physical, RevisionId=revision,
                CodeSha256=current['CodeSha256'], Description='ATLAS checkpoint before reviewed code update')
            # Publishing a checkpoint can advance $LATEST's revision. Re-read it, but
            # accept the handoff only if code and every stable configuration field are
            # still identical. The subsequent update guards this new revision as well.
            refreshed = client.get_function_configuration(FunctionName=physical)
            volatile = {'RevisionId', 'LastModified', 'LastUpdateStatus', 'LastUpdateStatusReason',
                        'LastUpdateStatusReasonCode', 'State', 'StateReason', 'StateReasonCode', 'RuntimeVersionConfig'}
            stable = lambda value: {k: v for k, v in value.items() if k not in volatile and k != 'ResponseMetadata'}
            if stable(current) != stable(refreshed) or refreshed.get('LastUpdateStatus', 'Successful') != 'Successful':
                raise api.ApiError(409, 'code_changed', 'La función cambió durante el checkpoint. Carga su estado antes de reintentar.')
            revision = refreshed['RevisionId']
        result = client.update_function_code(FunctionName=physical, RevisionId=revision, ZipFile=package.getvalue(), Publish=True)
    except ClientError as error:
        if error.response['Error']['Code'] in ('PreconditionFailedException', 'ResourceConflictException'):
            raise api.ApiError(409, 'code_changed', 'La función cambió o está ocupada. Carga su estado antes de reintentar.') from None
        raise
    return dict(describe(result), sourceHash=validation['sourceHash'], rollbackVersion=previous['Version'],
                message='AWS aceptó el código. Verifica el estado de actualización antes de usarlo.')
