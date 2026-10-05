"""Single control-plane Lambda: Basic Auth, validation and asynchronous provisioning."""
import base64
import binascii
import hmac
import json
import logging
import os
import re
import time
import uuid
import sys
import inspection
import assistant
import authoring
import collaboration
import urllib.request
import boto3
from botocore.auth import SigV4Auth
from botocore.awsrequest import AWSRequest
from botocore.config import Config
from botocore.exceptions import ClientError
from compiler import compile_graph
from graph import InvalidGraph, KINDS, digest, validate

LOG = logging.getLogger(__name__)
LOG.setLevel(logging.INFO)
CONFIG = Config(connect_timeout=2, read_timeout=5, retries={'max_attempts': 2, 'mode': 'standard'})
_secret = None


class ApiError(Exception):
    def __init__(self, status, code, message):
        self.status, self.code, self.message = status, code, message


def client(service):
    return boto3.client(service, config=CONFIG)


def authenticate(headers):
    global _secret
    auth = headers.get('authorization', '')
    try:
        scheme, encoded = auth.split(' ', 1)
        if scheme.lower() != 'basic' or len(encoded) > 2048:
            raise ValueError()
        username, password = base64.b64decode(encoded, validate=True).decode('utf-8').split(':', 1)
        if not 1 <= len(password) <= 6:
            raise ValueError()
    except (ValueError, UnicodeError, binascii.Error):
        raise ApiError(401, 'unauthorized', 'Se requiere Basic Auth.')
    if _secret is None or _secret[0] < time.monotonic():
        value = client('secretsmanager').get_secret_value(SecretId=os.environ['AUTH_SECRET_ARN'])
        _secret = (time.monotonic() + 60, json.loads(value['SecretString']))
    credentials = _secret[1]
    user_ok = hmac.compare_digest(username.encode(), credentials['username'].encode())
    password_ok = hmac.compare_digest(password.encode(), credentials['password'].encode())
    if not (user_ok & password_ok):
        raise ApiError(401, 'unauthorized', 'Credenciales inválidas.')


def body(event):
    raw = event.get('body') or '{}'
    if len(raw) > 90000:
        raise ApiError(413, 'payload_too_large', 'El límite es 64 KiB.')
    try:
        if event.get('isBase64Encoded'):
            raw = base64.b64decode(raw, validate=True).decode('utf-8')
        if len(raw.encode('utf-8')) > 65536:
            raise ApiError(413, 'payload_too_large', 'El límite es 64 KiB.')
        data = json.loads(raw, parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))
        if not isinstance(data, dict):
            raise ValueError()
        return data
    except (ValueError, UnicodeError, binascii.Error):
        raise ApiError(400, 'invalid_json', 'Se requiere un objeto JSON válido.')


def stack_name(slot):
    if str(slot) not in ('1', '2', '3'):
        raise ApiError(400, 'invalid_slot', 'deploymentId debe ser 1, 2 o 3.')
    return os.environ['DEMO_PREFIX'] + '-demo-' + str(slot)


def get_stack(cfn, name):
    try:
        return cfn.describe_stacks(StackName=name)['Stacks'][0]
    except ClientError as error:
        if error.response['Error']['Code'] == 'ValidationError' and 'does not exist' in error.response['Error']['Message']:
            return None
        raise


def template(cfn, name):
    data = cfn.get_template(StackName=name, TemplateStage='Original')['TemplateBody']
    return json.loads(data) if isinstance(data, str) else data


def resources(cfn, name):
    result = []
    for page in cfn.get_paginator('list_stack_resources').paginate(StackName=name):
        result.extend(page['StackResourceSummaries'])
    return result


def status(cfn, name, slot):
    stack = get_stack(cfn, name)
    if stack is None:
        raise ApiError(404, 'not_found', 'No existe este despliegue.')
    definition = template(cfn, name)
    actual = resources(cfn, name)
    nodes = []
    for resource in actual:
        metadata = definition['Resources'].get(resource['LogicalResourceId'], {}).get('Metadata', {})
        if 'NodeId' in metadata:
            nodes.append({'resourceId': metadata['NodeId'], 'kind': metadata['Kind'], 'name': metadata.get('Name', metadata['NodeId']), 'status': resource['ResourceStatus'], 'physicalId': resource.get('PhysicalResourceId'), 'message': resource.get('ResourceStatusReason', '')})
    aws_status = stack['StackStatus']
    finished = not aws_status.endswith('_IN_PROGRESS')
    # Readiness is stack-wide; individual CREATE_COMPLETE resources can still roll back.
    graph_hash = next((t['Value'] for t in stack.get('Tags', []) if t['Key'] == 'GraphHash'), None)
    return {'deploymentId': str(slot), 'stackId': stack['StackId'], 'graphHash': graph_hash, 'status': aws_status, 'finished': finished,
            'success': aws_status == 'CREATE_COMPLETE', 'nodes': nodes, 'message': stack.get('StackStatusReason', '')}


def create(cfn, data):
    slot = data.get('deploymentId')
    name = stack_name(slot)
    graph = validate(data.get('architecture'), os.environ['AWS_REGION'])
    fingerprint = digest(graph)
    existing = get_stack(cfn, name)
    if existing:
        tags = {t['Key']: t['Value'] for t in existing.get('Tags', [])}
        if tags.get('GraphHash') != fingerprint or existing['StackStatus'].startswith('DELETE_'):
            raise ApiError(409, 'slot_occupied', 'Elimina el despliegue anterior y espera a que termine antes de reutilizar este slot.')
        return 200, status(cfn, name, slot)
    compiled = compile_graph(graph, name, os.environ['ACCOUNT_ID'], os.environ['WORKLOAD_ROLE_ARN'], os.environ.get('AWS_PARTITION', 'aws'))
    encoded = json.dumps(compiled, separators=(',', ':'))
    if len(encoded.encode()) > 51200:
        raise ApiError(400, 'template_too_large', 'Reduce el número de nodos o enlaces.')
    try:
        result = cfn.create_stack(StackName=name, TemplateBody=encoded, RoleARN=os.environ['PROVISIONER_ROLE_ARN'],
            ClientRequestToken='create-' + fingerprint, OnFailure='ROLLBACK', TimeoutInMinutes=15,
            Tags=[{'Key': 'Project', 'Value': 'GuateGeeksAWS2026'}, {'Key': 'GraphHash', 'Value': fingerprint}])
    except ClientError as error:
        if error.response['Error']['Code'] == 'AlreadyExistsException':
            raise ApiError(409, 'slot_occupied', 'Otra solicitud ocupó el slot. Consulta su estado.')
        raise
    return 202, {'deploymentId': str(slot), 'stackId': result['StackId'], 'graphHash': fingerprint, 'status': 'CREATE_IN_PROGRESS', 'finished': False, 'success': False, 'pollAfterSeconds': 3}


def purge_buckets(cfn, name, context, stack_id=None):
    s3 = client('s3')
    for resource in resources(cfn, stack_id or name):
        if resource['ResourceType'] != 'AWS::S3::Bucket' or not resource.get('PhysicalResourceId'):
            continue
        bucket = resource['PhysicalResourceId']
        if not bucket.startswith(name + '-') or not bucket.endswith('-' + os.environ['ACCOUNT_ID']):
            raise ApiError(409, 'unexpected_bucket', 'El bucket no pertenece al despliegue.')
        # Repeatedly remove the first page; safe to resume and handles all object versions.
        while True:
            if context.get_remaining_time_in_millis() < 8000:
                return False
            try:
                page = s3.list_object_versions(Bucket=bucket, MaxKeys=1000)
            except ClientError as error:
                if error.response['Error']['Code'] == 'NoSuchBucket':
                    break
                raise
            objects = [{'Key': o['Key'], 'VersionId': o['VersionId']} for o in page.get('Versions', []) + page.get('DeleteMarkers', [])]
            if not objects:
                break
            result = s3.delete_objects(Bucket=bucket, Delete={'Objects': objects, 'Quiet': True})
            if result.get('Errors'):
                raise ApiError(409, 'purge_failed', 'No se pudieron borrar todos los objetos. Revisa los permisos del bucket.')
    return True


def delete(cfn, name, slot, purge, context, expected_stack_id=None):
    stack = get_stack(cfn, name)
    if not stack:
        return 200, {'deploymentId': slot, 'status': 'DELETE_COMPLETE', 'finished': True}
    if expected_stack_id and stack['StackId'] != expected_stack_id:
        raise ApiError(409, 'stack_changed', 'El slot cambió. Actualiza y confirma el nuevo despliegue.')
    if stack['StackStatus'] == 'DELETE_IN_PROGRESS':
        return 202, {'deploymentId': slot, 'status': 'DELETE_IN_PROGRESS', 'finished': False}
    if stack['StackStatus'].endswith('_IN_PROGRESS'):
        raise ApiError(409, 'operation_in_progress', 'Espera a que termine la operación actual.')
    if purge and not purge_buckets(cfn, name, context, stack['StackId']):
        return 202, {'deploymentId': slot, 'status': 'PURGING', 'finished': False, 'retryDelete': True}
    cfn.delete_stack(StackName=stack['StackId'], RoleARN=os.environ['PROVISIONER_ROLE_ARN'])
    return 202, {'deploymentId': slot, 'status': 'DELETE_IN_PROGRESS', 'finished': False}


def invoke(cfn, name, data):
    stack = get_stack(cfn, name)
    if not stack:
        raise ApiError(404, 'not_found', 'No existe este despliegue.')
    if stack['StackStatus'] != 'CREATE_COMPLETE':
        raise ApiError(409, 'not_ready', 'El despliegue debe estar CREATE_COMPLETE.')
    if data.get('stackId') and data['stackId'] != stack['StackId']:
        raise ApiError(409, 'stack_changed', 'El slot cambió. Actualiza su estado.')
    event_data = {'message': 'AWS Day test'}
    if 'eventJson' in data and data['eventJson'] != '':
        raw = data['eventJson']
        if not isinstance(raw, str) or len(raw.encode('utf-8')) > 4096:
            raise ApiError(400, 'invalid_event', 'Evento JSON de hasta 4 KiB requerido.')
        try:
            event_data = json.loads(raw, parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))
        except (ValueError, TypeError, RecursionError):
            raise ApiError(400, 'invalid_event', 'Evento JSON inválido.') from None
        if not isinstance(event_data, dict):
            raise ApiError(400, 'invalid_event', 'El evento debe ser un objeto JSON.')
    definition = template(cfn, stack['StackId'])
    candidates = {key: value for key, value in definition['Resources'].items() if value.get('Metadata', {}).get('NodeId') == data.get('resourceId')}
    if len(candidates) != 1:
        raise ApiError(400, 'invalid_resource', 'resourceId no pertenece al despliegue.')
    lid, definition = next(iter(candidates.items()))
    physical = next((r.get('PhysicalResourceId') for r in resources(cfn, stack['StackId']) if r['LogicalResourceId'] == lid), None)
    if not physical:
        raise ApiError(409, 'not_ready', 'Recurso sin identificador físico.')
    kind = definition['Metadata']['Kind']
    event_id = str(uuid.uuid4())
    # Correlation is application-owned; user payloads cannot overwrite another event.
    event_data['id'] = event_id
    payload = json.dumps(event_data, allow_nan=False)
    if kind == 0:
        url = f'https://{physical}.execute-api.{os.environ["AWS_REGION"]}.amazonaws.com/demo'
        session = boto3.Session()
        request = AWSRequest(method='POST', url=url, data=payload, headers={'Content-Type': 'application/json'})
        SigV4Auth(session.get_credentials().get_frozen_credentials(), 'execute-api', os.environ['AWS_REGION']).add_auth(request)
        with urllib.request.urlopen(urllib.request.Request(url, data=payload.encode(), headers=dict(request.headers), method='POST'), timeout=12) as response:
            result = json.loads(response.read())
        return 200, {'eventId': event_id, 'accepted': True, 'result': result}
    if kind == 1:
        client('lambda').invoke(FunctionName=physical, InvocationType='Event', Payload=payload.encode())
    elif kind == 3:
        client('s3').put_object(Bucket=physical, Key='demo/' + event_id + '.json', Body=payload, ContentType='application/json')
    elif kind == 4:
        args = {'QueueUrl': physical, 'MessageBody': payload}
        if physical.endswith('.fifo'):
            args['MessageGroupId'] = 'aws-day'
        client('sqs').send_message(**args)
    elif kind == 5:
        result = client('events').put_events(Entries=[{'EventBusName': physical, 'Source': 'guategeeks.demo', 'DetailType': 'DemoEvent', 'Detail': payload}])
        if result['FailedEntryCount']:
            raise ApiError(502, 'event_rejected', 'EventBridge rechazó el evento.')
    else:
        raise ApiError(400, 'unsupported_source', 'Usa un nodo API Gateway, Lambda, S3, SQS o EventBridge.')
    return 202, {'eventId': event_id, 'accepted': True, 'message': 'Evento aceptado; consulta CloudWatch para confirmar su procesamiento.'}


def route(event, context):
    headers = {k.lower(): v for k, v in (event.get('headers') or {}).items()}
    room_token = None
    if headers.get('authorization', '').startswith('Bearer '):
        room_token = headers['authorization'][7:]
        try:
            collaboration.authorize_http(event, room_token, sys.modules[__name__])
        except ValueError as error:
            raise ApiError(403, 'room_permission', str(error))
    else:
        authenticate(headers)
        try:
            collaboration.authorize_event_write(event, sys.modules[__name__])
        except ValueError as error:
            raise ApiError(403, 'room_permission', str(error))
    method, path = event.get('httpMethod'), event.get('path', '').rstrip('/')
    if method == 'POST' and path in ('/v1/collab/rooms', '/v1/collab/ticket'):
        try:
            data = body(event)
            if path.endswith('/rooms'):
                return 201, collaboration.bootstrap(data, sys.modules[__name__])
            return 200, collaboration.connection_ticket(data.get('token'), sys.modules[__name__])
        except ValueError as error:
            raise ApiError(409, 'collaboration_rejected', str(error))
    if method == 'GET' and path == '/v1/session':
        return 200, {'connected': True, 'region': os.environ['AWS_REGION'], 'schemaVersion': 1}
    if method == 'GET' and path == '/v1/catalog':
        return 200, {'maxNodes': 12, 'deploymentSlots': ['1', '2', '3'], 'services': [{'kind': i, 'name': n} for i, n in enumerate(KINDS)], 'limitations': ['HTTP API only', 'One S3 notification destination', 'One Lambda consumer per queue/API', 'S3 to FIFO unsupported', 'Replace designs by delete then create']}
    if method == 'POST' and path == '/v1/assistant/session':
        identity = collaboration.grant(collaboration.Store(sys.modules[__name__]), room_token)['userId'] if room_token else None
        return 200, assistant.create_session(sys.modules[__name__], identity) if identity else assistant.create_session(sys.modules[__name__])
    if method == 'POST' and path == '/v1/code/validate':
        return 200, authoring.validate_source(body(event).get('source'), sys.modules[__name__])
    if method == 'POST' and path == '/v1/code/test':
        return 200, authoring.test_draft(body(event), sys.modules[__name__])
    if method == 'POST' and path == '/v1/architectures/validate':
        graph = validate(body(event), os.environ['AWS_REGION'])
        return 200, {'valid': True, 'graphHash': digest(graph), 'nodeCount': len(graph['nodes'])}
    cfn = client('cloudformation')
    code_match = re.fullmatch(r'/v1/deployments/([123])/code(?:/(publish|rollback))?', path)
    if code_match:
        slot, operation = code_match.groups()
        if method == 'GET' and not operation:
            return 200, authoring.read(cfn, slot, event.get('queryStringParameters') or {}, sys.modules[__name__])
        if method == 'POST' and operation in ('publish', 'rollback'):
            return 202, authoring.publish(cfn, slot, body(event), sys.modules[__name__], rollback=operation == 'rollback')
    if method == 'POST' and path == '/v1/deployments':
        result = create(cfn, body(event))
        collaboration.record_deployment(room_token, result[1], sys.modules[__name__])
        return result
    match = re.fullmatch(r'/v1/deployments/([123])(/events|/logs|/items)?', path)
    if match:
        slot, suffix = match.groups()
        name = stack_name(slot)
        if method == 'GET' and not suffix:
            try:
                result = status(cfn, name, slot)
            except ApiError as error:
                if error.status == 404:
                    collaboration.record_deployment(room_token, dict(deploymentId=slot, status='NOT_FOUND', finished=True, success=False), sys.modules[__name__])
                raise
            collaboration.record_deployment(room_token, result, sys.modules[__name__])
            return 200, result
        query = event.get('queryStringParameters') or {}
        if method == 'GET' and suffix in ('/logs', '/items'):
            return 200, inspection.inspect(cfn, name, suffix[1:], query, sys.modules[__name__])
        if method == 'DELETE' and not suffix:
            result = delete(cfn, name, slot, query.get('purge') == 'true', context, query.get('stackId'))
            collaboration.record_deployment(room_token, dict(result[1], success=False), sys.modules[__name__])
            return result
        if method == 'POST' and suffix == '/events':
            return invoke(cfn, name, body(event))
    raise ApiError(404, 'not_found', 'Ruta no encontrada.')


def handler(event, context):
    request_id = getattr(context, 'aws_request_id', 'local')
    headers = {'Content-Type': 'application/json', 'Cache-Control': 'no-store', 'X-Request-Id': request_id}
    try:
        code, result = route(event, context)
    except InvalidGraph as error:
        code, result = 400, {'error': 'invalid_architecture', 'message': str(error)}
    except ApiError as error:
        code, result = error.status, {'error': error.code, 'message': error.message}
        if code == 401:
            headers['WWW-Authenticate'] = 'Basic realm="AWS Day", charset="UTF-8"'
    except Exception as error:
        # Do not log events, Authorization, credentials, templates or arbitrary AWS error text.
        LOG.error(json.dumps({'requestId': request_id, 'errorType': type(error).__name__, 'awsErrorCode': error.response['Error']['Code'] if isinstance(error, ClientError) else None}))
        code, result = 502, {'error': 'aws_operation_failed', 'message': 'No se pudo completar la operación. Consulta los logs con requestId.', 'requestId': request_id}
    LOG.info(json.dumps({'requestId': request_id, 'status': code}))
    return {'statusCode': code, 'headers': headers, 'body': json.dumps(result, ensure_ascii=False)}
