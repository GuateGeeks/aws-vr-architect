"""Bounded, read-only inspection of resources resolved from a confirmed demo stack."""
import base64
import hashlib
from datetime import datetime, timezone
import hmac
import json
import re
import time
from botocore.exceptions import ClientError


def inspect(cfn, name, mode, query, api):
    def reject(message):
        raise api.ApiError(400, 'invalid_inspection', message)
    stack = api.get_stack(cfn, name)
    if not stack:
        raise api.ApiError(404, 'not_found', 'No existe este despliegue.')
    identity = query.get('stackId')
    if not identity or identity != stack['StackId']:
        raise api.ApiError(409, 'stack_changed', 'El slot cambió. Actualiza su estado.')
    if stack['StackStatus'] != 'CREATE_COMPLETE':
        raise api.ApiError(409, 'not_ready', 'Espera a que el despliegue esté CREATE_COMPLETE.')
    node = query.get('resourceId')
    definition = api.template(cfn, identity)['Resources']
    candidates = [(key, value) for key, value in definition.items()
                  if value.get('Metadata', {}).get('NodeId') == node]
    expected_type = 'AWS::Lambda::Function' if mode == 'logs' else 'AWS::DynamoDB::Table'
    if len(candidates) != 1 or candidates[0][1]['Type'] != expected_type:
        reject('Selecciona una Lambda para logs o una tabla DynamoDB para ítems.')
    logical, _ = candidates[0]
    physical = next((r.get('PhysicalResourceId') for r in api.resources(cfn, identity)
                     if r['LogicalResourceId'] == logical), None)
    if not physical or not physical.startswith(name + '-'):
        reject('El recurso no pertenece a este slot.')
    now = int(time.time())
    search, level, event_id = query.get('q', ''), query.get('level', ''), query.get('eventId', '')
    if not all(isinstance(v, str) for v in (search, level, event_id)) or len(search) > 80 or level not in ('', 'INFO', 'WARN', 'ERROR') or (event_id and not re.fullmatch(r'[A-Za-z0-9_-]{1,128}', event_id)):
        reject('Filtro inválido: búsqueda hasta 80 caracteres y eventId alfanumérico.')
    binding = [identity, node, mode, search, level, event_id]
    secret = api._secret[1]['password'].encode()
    state = {'binding': binding, 'expires': now + 900, 'start': (now - 900) * 1000, 'end': now * 1000}
    token = query.get('cursor') or query.get('resume')
    if token:
        try:
            if len(token) > 16000:
                raise ValueError()
            encoded, signature = token.split('.')
            raw = base64.urlsafe_b64decode(encoded.encode())
            if not hmac.compare_digest(hmac.new(secret, raw, hashlib.sha256).hexdigest(), signature):
                raise ValueError()
            prior = json.loads(raw)
            if prior['binding'] != binding or prior['expires'] < now:
                raise ValueError()
            if query.get('cursor'):
                if prior.get('purpose', 'page') != 'page': raise ValueError()
                state = prior
            else:
                if mode != 'logs' or prior.get('purpose') != 'resume': raise ValueError()
                state['start'] = max(state['start'], min(prior['end'], now * 1000) - 30000)
                state['incremental'] = True
        except (ValueError, KeyError, TypeError, AttributeError):
            reject('Página vencida o inválida. Vuelve a actualizar.')
    entries, following = [], None
    if mode == 'logs':
        args = dict(logGroupName='/aws/lambda/' + physical, startTime=state['start'], endTime=state['end'], limit=10)
        if state.get('next'):
            args['nextToken'] = state['next']
        try:
            result = api.client('logs').filter_log_events(**args)
        except ClientError as error:
            if error.response['Error']['Code'] != 'ResourceNotFoundException':
                raise
            result = {}
        for event in result.get('events', []):
            content = event.get('message', '')
            try:
                structured = json.loads(content)
                if not isinstance(structured, dict): structured = {}
            except (ValueError, TypeError): structured = {}
            severity = str(structured.get('level', 'ERROR' if content.startswith(('[ERROR]', 'Traceback')) else 'INFO')).upper()
            if severity == 'WARNING': severity = 'WARN'
            correlation = str(structured.get('eventId', ''))
            if (search and search.casefold() not in content.casefold()) or (level and severity != level) or (event_id and correlation != event_id): continue
            stable = event.get('eventId') or hashlib.sha256(json.dumps([physical, event.get('logStreamName'), event['timestamp'], content]).encode()).hexdigest()
            entries.append(entry(datetime.fromtimestamp(event['timestamp'] / 1000, timezone.utc).strftime('%H:%M:%S UTC'), content,
                                 id=stable, timestamp=event['timestamp'], level=severity, eventId=correlation,
                                 nodeId=str(structured.get('nodeId', '')), targetNodeId=str(structured.get('targetNodeId', '')),
                                 stage=str(structured.get('stage', '')), requestId=str(structured.get('requestId', ''))))
        following = result.get('nextToken')
        note = 'Logs reales · últimos 15 minutos al actualizar · orden cronológico. No es una traza de paquetes.'
    else:
        args = dict(TableName=physical, Limit=10, ConsistentRead=False)
        if state.get('next'):
            args['ExclusiveStartKey'] = state['next']
        if event_id:
            result = api.client('dynamodb').get_item(TableName=physical, Key={'id': {'S': event_id}}, ConsistentRead=True)
            items = [result['Item']] if 'Item' in result else []
        else:
            result = api.client('dynamodb').scan(**args)
            items = result.get('Items', [])
        for item in items:
            # AttributeValue JSON retains exact numbers, sets and binary types.
            content = json.dumps(item, ensure_ascii=False, indent=2,
                                 default=lambda value: base64.b64encode(value).decode())
            if search and search.casefold() not in content.casefold(): continue
            key = item.get('id', {})
            stable = hashlib.sha256(json.dumps([physical, key], sort_keys=True).encode()).hexdigest()
            correlation = key.get('S', '')
            entries.append(entry('Ítem · ' + correlation, content, id=stable, eventId=correlation, nodeId=node, stage='stored', level='INFO'))
        following = result.get('LastEvaluatedKey')
        note = 'Lectura real · muestra de hasta 10 ítems · JSON tipado (S texto, N número). Scan eventual, sin orden garantizado.'
    cursor, resume = '', ''
    def sign(value):
        raw = json.dumps(value, separators=(',', ':')).encode()
        return base64.urlsafe_b64encode(raw).decode() + '.' + hmac.new(secret, raw, hashlib.sha256).hexdigest()
    if following:
        state['next'] = following
        state['purpose'] = 'page'
        cursor = sign(state)
    elif mode == 'logs':
        resume = sign({'binding': binding, 'expires': now + 900, 'end': state['end'], 'purpose': 'resume'})
    return {'stackId': identity, 'resourceId': node, 'entries': entries, 'cursor': cursor, 'resume': resume,
            'incremental': state.get('incremental', False), 'message': note, 'inspectionVersion': 2}


def entry(title, content, **metadata):
    metadata = {key: value[:128] if isinstance(value, str) else value for key, value in metadata.items()}
    return dict(title=title[:160], text=content[:4096], truncated=len(content) > 4096, **metadata)
