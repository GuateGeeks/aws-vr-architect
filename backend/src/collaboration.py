"""Four-person event rooms. One DynamoDB CAS owns membership, leases and graph edits.

Bootstrap uses the existing event credential; each device receives an independent,
opaque guest identity. This is event access, not durable personal account login.
No voice, transcript, AI secret or pose is persisted in the room.
"""
import copy
import hashlib
import json
import math
import os
import re
import secrets
import time
from botocore.exceptions import ClientError
from graph import EDGES, SETTINGS

LEASE_SECONDS = 20
SESSION_SECONDS = 7200


def encode(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'), sort_keys=True)


def require(ok, message):
    if not ok:
        raise ValueError(message)


def identifier(value):
    return isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9_-]{1,64}', value)


def normalize_graph(value, region):
    require(isinstance(value, dict) and type(value.get('schemaVersion')) is int and value.get('schemaVersion') == 1, 'Diseño inválido.')
    require(value.get('region') == region, 'La región de sala no puede cambiar.')
    nodes, links = value.get('nodes'), value.get('links')
    require(isinstance(nodes, list) and len(nodes) <= 12 and isinstance(links, list) and len(links) <= 36, 'Límite de diseño excedido.')
    result = []
    for node in nodes:
        require(isinstance(node, dict) and identifier(node.get('id')), 'ID inválido.')
        kind, setting = node.get('kind'), node.get('setting', 0)
        require(type(kind) is int and 0 <= kind < 7 and type(setting) is int and 0 <= setting < SETTINGS[kind], 'Configuración inválida.')
        name = node.get('name')
        require(isinstance(name, str) and 1 <= len(name) <= 80 and '<' not in name and '>' not in name, 'Nombre inválido.')
        position = node.get('position')
        require(isinstance(position, dict), 'Posición inválida.')
        for axis, low, high in [('x', -1.6, 1.6), ('y', 1.02, 2.1), ('z', 1.65, 3.65)]:
            n = position.get(axis)
            require(type(n) in (int, float) and math.isfinite(n) and low <= n <= high, 'Posición fuera de la mesa.')
        scale = node.get('viewScale', 0)
        require(type(scale) in (int, float) and (scale == 0 or .5 <= scale <= 1.25), 'Escala inválida.')
        result.append(dict(id=node['id'], kind=kind, setting=setting, name=name, position=position, viewScale=scale, state=0))
    by_id = {n['id']: n for n in result}
    require(len(by_id) == len(result), 'IDs duplicados.')
    pairs = set()
    for link in links:
        require(isinstance(link, dict), 'Enlace inválido.')
        a, b = link.get('from'), link.get('to')
        require(isinstance(a, str) and isinstance(b, str) and a in by_id and b in by_id and a != b and (a, b) not in pairs, 'Enlace inválido.')
        ka, kb = by_id[a]['kind'], by_id[b]['kind']
        require(kb in EDGES[ka] or (kb == 6 and ka != 6), 'Enlace incompatible.')
        require(not (ka == 3 and kb == 4 and by_id[b]['setting'] == 1), 'S3 no conecta a FIFO.')
        pairs.add((a, b))
    adjacency = {k: [] for k in by_id}
    for a, b in pairs:
        adjacency[a].append(b)
    for n in result:
        kinds = [by_id[b]['kind'] for b in adjacency[n['id']]]
        require(n['kind'] not in (0, 4) or kinds.count(1) <= 1, 'Un único consumidor Lambda.')
        require(n['kind'] != 3 or sum(k in (1, 4) for k in kinds) <= 1, 'Un único destino S3.')
    visited, visiting = set(), set()
    def visit(key):
        require(key not in visiting, 'Ciclo inválido.')
        if key in visited:
            return
        visiting.add(key)
        for target in adjacency[key]:
            visit(target)
        visiting.remove(key)
        visited.add(key)
    for key in by_id:
        visit(key)
    return dict(schemaVersion=1, region=region, nodes=sorted(result, key=lambda n: n['id']), links=[{'from': a, 'to': b} for a, b in sorted(pairs)])


def changed_ids(before, after):
    old, new = ({n['id']: n for n in g['nodes']} for g in (before, after))
    changed = {key for key in old.keys() | new.keys() if old.get(key) != new.get(key)}
    a, b = ({(e['from'], e['to']) for e in g['links']} for g in (before, after))
    for x, y in a ^ b:
        changed.update((x, y))
    return changed


def inverse(current, operation):
    before, after = operation['before'], operation['after']
    old, new, now = ({n['id']: n for n in g['nodes']} for g in (before, after, current))
    touched = {k for k in old.keys() | new.keys() if old.get(k) != new.get(k)}
    require(all(now.get(k) == new.get(k) for k in touched), 'Otro usuario cambió el objeto; no se puede deshacer.')
    for key in touched:
        if key in old:
            now[key] = old[key]
        else:
            now.pop(key, None)
    a, b, edges = ({(e['from'], e['to']) for e in g['links']} for g in (before, after, current))
    require((b - a) <= edges and not ((a - b) & edges), 'Otro usuario cambió los enlaces.')
    edges = (edges - (b - a)) | (a - b)
    result = copy.deepcopy(current)
    result['nodes'] = list(now.values())
    result['links'] = [{'from': x, 'to': y} for x, y in sorted(edges)]
    return normalize_graph(result, current['region'])


def apply_command(room, user, data, now):
    """Pure transition; the caller persists this whole result with a conditional version."""
    require(user in room['members'] and room['members'][user]['expiresAt'] > now, 'La membresía venció.')
    action = data.get('action')
    member = room['members'][user]
    member['expiresAt'] = now + LEASE_SECONDS
    if action == 'heartbeat':
        for lock in room['locks'].values():
            if lock['owner'] == user and lock['expiresAt'] > now:
                lock['expiresAt'] = now + LEASE_SECONDS
        return
    if action == 'claim':
        key = data.get('objectId')
        require(key in {n['id'] for n in room['graph']['nodes']}, 'Objeto inexistente.')
        lock = room['locks'].get(key)
        require(not lock or lock['expiresAt'] <= now or lock['owner'] == user, 'Objeto en uso.')
        room['locks'][key] = dict(owner=user, token=secrets.token_hex(12), expiresAt=now + LEASE_SECONDS)
        return
    if action == 'release':
        key = data.get('objectId')
        lock = room['locks'].get(key)
        require(lock and lock['owner'] == user and lock['token'] == data.get('leaseToken'), 'Concesión vencida.')
        del room['locks'][key]
        return
    require(action in ('op', 'undo'), 'Acción desconocida.')
    request_id = data.get('requestId')
    require(identifier(request_id), 'ID de operación inválido.')
    receipt = user + ':' + request_id
    if receipt in room['receipts']:
        return
    deployment = room.get('deployment', {})
    require(deployment.get('expiresAt', 0) <= now or deployment.get('finished', True), 'Espera a que termine el despliegue de la sala.')
    require(type(data.get('baseRevision')) is int and data['baseRevision'] == room['revision'], 'Revisión vencida; actualiza el contexto.')
    require(member['role'] in ('facilitator', 'editor'), 'Sin permiso de edición.')
    previous = room['graph']
    undo_target = None
    if action == 'undo':
        undo_target = next((op for op in reversed(room['operations']) if op['userId'] == user and not op.get('undone') and not op.get('isUndo')), None)
        require(undo_target is not None, 'No hay operaciones propias para deshacer.')
        next_graph = inverse(previous, undo_target)
    else:
        next_graph = normalize_graph(data.get('graph'), previous['region'])
    touched = changed_ids(previous, next_graph)
    require(bool(touched), 'No hay cambios.')
    # Replacements/global actions are a facilitator privilege; granular edits remain open.
    removed = {n['id'] for n in previous['nodes']} - {n['id'] for n in next_graph['nodes']}
    if data.get('global') or len(removed) > 1:
        require(member['role'] == 'facilitator', 'Solo el facilitador cambia el diseño completo.')
    supplied_leases = data.get('leases') or []
    require(isinstance(supplied_leases, list), 'Concesiones inválidas.')
    lease_tokens = {v.get('objectId'): v.get('token') for v in supplied_leases if isinstance(v, dict)}
    for key in touched:
        lock = room['locks'].get(key)
        require(not lock or lock['expiresAt'] <= now or lock['owner'] == user, 'Objeto en uso por otro usuario.')
        if lock and lock['owner'] == user:
            require(lock['expiresAt'] > now and lease_tokens.get(key) == lock['token'], 'Concesión vencida; solicita el objeto nuevamente.')
    room['revision'] += 1
    room['graph'] = next_graph
    # Moving/scaling objects leaves the deployed AWS definition unchanged.
    def definition(graph):
        return dict(nodes=[{k: n[k] for k in ('id', 'kind', 'setting', 'name')} for n in graph['nodes']], links=graph['links'])
    if deployment.get('success') and definition(previous) == definition(next_graph):
        deployment['revision'] = room['revision']
    if undo_target:
        undo_target['undone'] = True
    room['operations'].append(dict(userId=user, requestId=request_id, before=previous, after=next_graph, isUndo=action == 'undo'))
    room['operations'] = room['operations'][-8:]
    room['receipts'].append(receipt)
    room['receipts'] = room['receipts'][-64:]


class Store:
    def __init__(self, api):
        self.db = api.client('dynamodb')
        self.table = os.environ['COLLAB_TABLE']

    def get(self, key):
        item = self.db.get_item(TableName=self.table, Key={'id': {'S': key}}, ConsistentRead=True).get('Item')
        return json.loads(item['data']['S']) if item else None

    def put(self, key, value, previous=None):
        require(len(encode(value).encode()) < 300000, 'Sala demasiado grande.')
        version = 1 if previous is None else previous + 1
        value['version'] = version
        args = dict(TableName=self.table, Item={'id': {'S': key}, 'data': {'S': encode(value)}, 'version': {'N': str(version)}, 'expiresAt': {'N': str(value['expiresAt'])}})
        if previous is None:
            args['ConditionExpression'] = 'attribute_not_exists(id)'
        else:
            args.update(ConditionExpression='#v = :v', ExpressionAttributeNames={'#v': 'version'}, ExpressionAttributeValues={':v': {'N': str(previous)}})
        self.db.put_item(**args)

    def mutate(self, key, transition):
        for _ in range(5):
            value = self.get(key)
            require(value is not None and value['expiresAt'] > time.time(), 'Sala o sesión vencida.')
            previous = value['version']
            transition(value)
            try:
                self.put(key, value, previous)
                return value
            except ClientError as error:
                if error.response['Error']['Code'] != 'ConditionalCheckFailedException':
                    raise
        raise ValueError('Sala ocupada. Intenta nuevamente con contexto actualizado.')


def token_key(token):
    require(isinstance(token, str) and 20 <= len(token) <= 200, 'Sesión inválida.')
    return 'token:' + hashlib.sha256(token.encode()).hexdigest()


def grant(store, token):
    value = store.get(token_key(token))
    require(value and value['expiresAt'] > time.time(), 'Sesión vencida.')
    return value


def snapshot(room, request_id='', accepted=True, message=''):
    now = time.time()
    return dict(type='snapshot', roomId=room['roomId'], revision=room['revision'], roomVersion=room.get('version', 0), hostId=room['hostId'], graph=room['graph'],
                members=[dict(userId=k, name=v['name'], station=v['station'], role=v['role']) for k, v in room['members'].items() if v['expiresAt'] > now],
                locks=[dict(objectId=k, **v) for k, v in room['locks'].items() if v['expiresAt'] > now], deployment=room.get('deployment'), requestId=request_id, accepted=accepted, message=message)


def authorize_http(event, token, api):
    store, now = Store(api), int(time.time())
    session = grant(store, token)
    room = store.get('room:' + session['roomId'])
    require(room and room['expiresAt'] > now, 'Sala vencida.')
    member = room['members'].get(session['userId'])
    require(member and member['expiresAt'] > now, 'Reconecta a la sala antes de usar AWS.')
    method, path = event.get('httpMethod'), event.get('path', '').rstrip('/')
    writes = method == 'DELETE' or path == '/v1/deployments' or path.endswith(('/publish', '/rollback'))
    require(not writes or room['hostId'] == session['userId'], 'Solo el facilitador confirma cambios en AWS.')
    require(not path.startswith('/v1/collab/rooms'), 'Sal de la sala antes de crear otra.')
    slot_match = re.match(r'/v1/(?:deployments|code)/([123])(?:/|$)', path)
    if slot_match:
        binding = store.get('slot:' + slot_match.group(1))
        require(binding and binding['roomId'] == session['roomId'], 'El slot no pertenece a esta sala.')
    if method == 'POST' and path == '/v1/deployments':
        from graph import validate, digest
        data = api.body(event)
        slot = str(data.get('deploymentId', ''))
        require(slot in ('1', '2', '3'), 'Slot inválido.')
        binding = store.get('slot:' + slot)
        require(not binding or binding['roomId'] == session['roomId'] or binding['expiresAt'] <= now, 'Otra sala reservó este slot.')
        expected = digest(validate(room['graph'], room['graph']['region']))
        require(digest(validate(data.get('architecture'), room['graph']['region'])) == expected, 'Despliega la revisión compartida actual.')
        slot_record = dict(roomId=session['roomId'], expiresAt=room['expiresAt'])
        # A slot reservation is conditional too; a racing room cannot overwrite it.
        store.put('slot:' + slot, slot_record, binding['version'] if binding else None)
        def reserve(value):
            require(value['revision'] == room['revision'], 'El diseño cambió durante la revisión.')
            require(all(lock['expiresAt'] <= now for lock in value['locks'].values()), 'Suelta los objetos antes de desplegar.')
            current = value.get('deployment', {})
            require(current.get('finished', True) or current.get('expiresAt', 0) <= now, 'Despliegue en curso.')
            value['deployment'] = dict(deploymentId=data.get('deploymentId'), status='REQUESTED', finished=False, success=False,
                                       fingerprint=expected, revision=value['revision'], expiresAt=now + 60)
        store.mutate('room:' + session['roomId'], reserve)
    return session


def authorize_event_write(event, api):
    """The shared event credential cannot bypass room roles on a reserved slot."""
    if not os.environ.get('COLLAB_TABLE'):
        return
    method, path = event.get('httpMethod'), event.get('path', '').rstrip('/')
    slot = None
    if method == 'POST' and path == '/v1/deployments':
        slot = str(api.body(event).get('deploymentId', ''))
    elif method == 'DELETE' or method == 'POST' and path.endswith(('/publish', '/rollback', '/events')):
        match = re.match(r'/v1/deployments/([123])(?:/|$)', path)
        slot = match.group(1) if match else None
    if slot in ('1', '2', '3'):
        binding = Store(api).get('slot:' + slot)
        require(not binding or binding['expiresAt'] <= time.time(), 'El slot está reservado: usa la identidad de sala.')


def record_deployment(token, result, api):
    if not token:
        return
    store = Store(api)
    session = grant(store, token)
    def record(room):
        current = room.get('deployment', {})
        if current.get('deploymentId') != result.get('deploymentId'):
            return
        if result.get('status') == 'NOT_FOUND' and not current.get('status', '').startswith('DELETE') and current.get('expiresAt', 0) > time.time():
            return
        if result.get('success') and result.get('graphHash') != current.get('fingerprint'):
            result.update(success=False, finished=True, status='DEFINITION_MISMATCH')
        # Status is observed from AWS by the control plane, never supplied by a headset.
        if result.get('status') != 'REQUESTED':
            current['expiresAt'] = int(time.time()) + 1800
        for field in ('stackId', 'status', 'finished', 'success'):
            if field in result:
                current[field] = result[field]
    store.mutate('room:' + session['roomId'], record)


def bootstrap(data, api):
    store, now = Store(api), int(time.time())
    name = data.get('name', '')
    require(isinstance(name, str) and 1 <= len(name) <= 24 and '<' not in name and '>' not in name, 'Nombre inválido.')
    user, token = secrets.token_hex(16), secrets.token_urlsafe(32)
    room_id = data.get('roomId', '').upper()
    if not room_id:
        room_id = secrets.token_hex(4).upper()
        graph = normalize_graph(data.get('graph'), os.environ['AWS_REGION'])
        room = dict(roomId=room_id, hostId=user, graph=graph, revision=0, members={}, locks={}, receipts=[], operations=[], expiresAt=now + SESSION_SECONDS)
        store.put('room:' + room_id, room)
        role = 'facilitator'
    else:
        require(re.fullmatch('[A-F0-9]{8}', room_id), 'Código de sala inválido.')
        role = 'editor'
    session = dict(userId=user, roomId=room_id, name=name, role=role, expiresAt=now + SESSION_SECONDS)
    def join(room):
        occupied = {m['station'] for m in room['members'].values() if m['expiresAt'] > now}
        station = next((s for s in range(4) if s not in occupied), None)
        require(station is not None, 'Sala llena: máximo cuatro personas.')
        session['station'] = station
        room['members'][user] = dict(name=name, role=role, station=station, expiresAt=now + LEASE_SECONDS, connectionId='')
    room = store.mutate('room:' + room_id, join)
    store.put(token_key(token), session)
    return dict(token=token, userId=user, roomId=room_id, station=session['station'], role=role, expiresAt=session['expiresAt'], websocketUrl=os.environ['COLLAB_WS_URL'])


def connection_ticket(token, api):
    store = Store(api)
    session = grant(store, token)
    ticket = secrets.token_urlsafe(32)
    value = dict(session, expiresAt=int(time.time()) + 60, sessionExpiresAt=session['expiresAt'], consumed=False)
    store.put('ticket:' + hashlib.sha256(ticket.encode()).hexdigest(), value)
    return dict(ticket=ticket, websocketUrl=os.environ['COLLAB_WS_URL'])


def management(event, api):
    context = event['requestContext']
    return api.client('apigatewaymanagementapi') if 'domainName' not in context else __import__('boto3').client('apigatewaymanagementapi', endpoint_url='https://' + context['domainName'] + '/' + context['stage'], config=api.CONFIG)


def send(ws, connection, data):
    if not connection:
        return
    try:
        ws.post_to_connection(ConnectionId=connection, Data=encode(data).encode())
    except ClientError as error:
        if error.response['Error']['Code'] != 'GoneException':
            raise


def broadcast(ws, room, data, exclude=''):
    for member in room['members'].values():
        if member['expiresAt'] > time.time() and member['connectionId'] != exclude:
            send(ws, member['connectionId'], data)


def websocket(event, api):
    store, now = Store(api), int(time.time())
    context = event['requestContext']
    connection, route = context['connectionId'], context['routeKey']
    if route == '$connect':
        ticket = (event.get('queryStringParameters') or {}).get('ticket', '')
        require(isinstance(ticket, str) and 20 <= len(ticket) <= 200, 'Ticket inválido.')
        key = 'ticket:' + hashlib.sha256(ticket.encode()).hexdigest()
        def consume(value):
            require(not value['consumed'], 'Ticket usado.')
            value['consumed'] = True
        session = store.mutate(key, consume)
        session['expiresAt'] = session['sessionExpiresAt']
        store.put('connection:' + connection, session)
        def connect(room):
            member = room['members'].get(session['userId'])
            require(member is not None, 'Membresía inválida.')
            # An expired station can have been reassigned; choose a new free one.
            used = {m['station'] for u, m in room['members'].items() if u != session['userId'] and m['expiresAt'] > now}
            if member['station'] in used:
                free = next((s for s in range(4) if s not in used), None)
                require(free is not None, 'Sala llena.')
                member['station'] = free
            member.update(connectionId=connection, expiresAt=now + LEASE_SECONDS)
        store.mutate('room:' + session['roomId'], connect)
        return
    session = store.get('connection:' + connection)
    require(session and session['expiresAt'] > now, 'Sesión vencida.')
    room_key, user = 'room:' + session['roomId'], session['userId']
    room = store.get(room_key)
    require(room and room['expiresAt'] > now and user in room['members'] and room['members'][user]['connectionId'] == connection, 'Conexión reemplazada o sala vencida.')
    ws = management(event, api)
    if route == '$disconnect':
        def disconnect(value):
            if value['members'][user]['connectionId'] == connection:
                value['members'][user].update(connectionId='', expiresAt=now)
                value['locks'] = {k: v for k, v in value['locks'].items() if v['owner'] != user}
        room = store.mutate(room_key, disconnect)
        broadcast(ws, room, snapshot(room))
        return
    raw = event.get('body') or '{}'
    require(len(raw.encode()) <= 24000, 'Mensaje demasiado grande.')
    data = json.loads(raw, parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Número inválido.')))
    require(isinstance(data, dict), 'Mensaje inválido.')
    action = data.get('action')
    if action == 'sync':
        send(ws, connection, snapshot(room))
        return
    require(room['members'][user]['expiresAt'] > now, 'Membresía vencida: reconecta.')
    if action == 'presence':
        # No poses in DynamoDB; the counter prevents one participant flooding the room.
        def limit(value):
            if value.get('second') != now:
                value.update(second=now, count=0)
            require(value.get('count', 0) < 16, 'Presencia demasiado frecuente.')
            value['count'] = value.get('count', 0) + 1
        store.mutate('connection:' + connection, limit)
        pose = data.get('pose')
        require(isinstance(pose, dict), 'Pose inválida.')
        for field, axes, limit_value in [('head', 'xyz', 20), ('hand', 'xyz', 20), ('pointAt', 'xyz', 20), ('rotation', 'xyzw', 1)]:
            v = pose.get(field)
            require(isinstance(v, dict) and all(type(v.get(a)) in (int, float) and math.isfinite(v[a]) and abs(v[a]) <= limit_value for a in axes), 'Pose inválida.')
        focus = pose.get('focusId', '')
        require(not focus or focus in {n['id'] for n in room['graph']['nodes']}, 'Foco inválido.')
        broadcast(ws, room, dict(type='presence', userId=user, pose={k: pose[k] for k in ('head', 'hand', 'pointAt', 'rotation')}, focusId=focus, pointing=pose.get('pointing') is True), exclude=connection)
        return
    request_id = data.get('requestId', '')
    try:
        def transition(value):
            require(value['members'][user]['connectionId'] == connection, 'Conexión reemplazada.')
            apply_command(value, user, data, now)
        room = store.mutate(room_key, transition)
        if action == 'heartbeat':
            send(ws, connection, snapshot(room))
        else:
            broadcast(ws, room, snapshot(room, request_id))
    except ValueError as error:
        room = store.get(room_key)
        send(ws, connection, snapshot(room, request_id, False, str(error)))


def handler(event, context):
    import app
    try:
        websocket(event, app)
        return {'statusCode': 200, 'body': ''}
    except (ValueError, app.ApiError):
        return {'statusCode': 403, 'body': 'Sesión o mensaje inválido.'}
    except Exception as error:
        # Never log query tickets, tokens, event bodies or connection payloads.
        app.LOG.error('collaboration_error:%s', type(error).__name__)
        return {'statusCode': 502, 'body': 'Servicio no disponible.'}
