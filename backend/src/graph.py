"""Validate the Unity Architecture DTO. Never accept user-supplied IaC or code."""
import hashlib
import json
import re

KINDS = ['ApiGateway', 'Lambda', 'DynamoDB', 'S3', 'SQS', 'EventBridge', 'CloudWatch']
SETTINGS = [1, 4, 2, 2, 2, 2, 3]
EDGES = {0: {1}, 1: {2, 3, 4, 5}, 2: set(), 3: {1, 4}, 4: {1}, 5: {1, 4}, 6: set()}


class InvalidGraph(ValueError):
    pass


def validate(data, region):
    def require(ok, message):
        if not ok:
            raise InvalidGraph(message)
    require(isinstance(data, dict), 'El diseño debe ser un objeto JSON.')
    require(type(data.get('schemaVersion')) is int and data['schemaVersion'] == 1, 'schemaVersion debe ser 1.')
    require(data.get('region') == region, 'La región debe coincidir con la API: ' + region)
    nodes, links = data.get('nodes'), data.get('links')
    require(isinstance(nodes, list) and 2 <= len(nodes) <= 12, 'Se requieren entre 2 y 12 nodos.')
    require(isinstance(links, list) and len(links) <= 36, 'Se permiten hasta 36 enlaces.')
    normalized = []
    for n in nodes:
        require(isinstance(n, dict), 'Nodo inválido.')
        require(isinstance(n.get('id'), str) and re.fullmatch(r'[a-zA-Z0-9_-]{1,64}', n['id']), 'ID de nodo inválido.')
        kind, setting = n.get('kind'), n.get('setting', 0)
        require(type(kind) is int and 0 <= kind < len(KINDS), 'kind debe ser el enum numérico de Unity (0–6).')
        require(type(setting) is int and 0 <= setting < SETTINGS[kind], 'Configuración no soportada; API Gateway admite HTTP API (setting=0).')
        name = n.get('name', KINDS[kind])
        require(isinstance(name, str) and 1 <= len(name) <= 80, 'Nombre inválido.')
        normalized.append({'id': n['id'], 'kind': kind, 'setting': setting, 'name': name})
    by_id = {n['id']: n for n in normalized}
    require(len(by_id) == len(nodes), 'IDs duplicados.')
    adjacency = {i: [] for i in by_id}
    pairs = set()
    for e in links:
        require(isinstance(e, dict) and isinstance(e.get('from'), str) and isinstance(e.get('to'), str), 'Enlace inválido.')
        a, b = e['from'], e['to']
        require(a in by_id and b in by_id and a != b, 'Enlace sin nodo o hacia sí mismo.')
        require((a, b) not in pairs, 'Enlace duplicado.')
        ka, kb = by_id[a]['kind'], by_id[b]['kind']
        require(kb in EDGES[ka] or (kb == 6 and ka != 6), 'Conexión no soportada.')
        require(not (ka == 3 and kb == 4 and by_id[b]['setting'] == 1), 'S3 no entrega notificaciones directamente a SQS FIFO.')
        pairs.add((a, b))
        adjacency[a].append(b)
    for n in normalized:
        require(any(n['id'] in pair for pair in pairs), 'Todos los nodos deben estar conectados.')
        if n['kind'] in (0, 4):
            targets = [b for a, b in pairs if a == n['id'] and by_id[b]['kind'] == 1]
            require(len(targets) <= 1, 'Una API o cola admite un único consumidor Lambda en esta demo.')
        if n['kind'] == 0:
            require(any(by_id[b]['kind'] == 1 for b in adjacency[n['id']]), 'API Gateway requiere una Lambda conectada.')
        if n['kind'] == 3:
            require(sum(by_id[b]['kind'] in (1, 4) for b in adjacency[n['id']]) <= 1, 'S3 admite un único destino de notificación en esta demo.')
    seen, visiting = set(), set()
    def visit(node):
        require(node not in visiting, 'El diseño contiene un ciclo.')
        if node in seen:
            return
        visiting.add(node)
        for target in adjacency[node]:
            visit(target)
        visiting.remove(node)
        seen.add(node)
    for node in by_id:
        visit(node)
    return {'schemaVersion': 1, 'region': region, 'nodes': sorted(normalized, key=lambda n: n['id']),
            'links': [{'from': a, 'to': b} for a, b in sorted(pairs)]}


def digest(graph):
    return hashlib.sha256(json.dumps(graph, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def logical(node_id):
    return 'N' + hashlib.sha256(node_id.encode()).hexdigest()[:20]
