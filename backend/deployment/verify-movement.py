"""Verify transient drags and concurrent drops through four live WebSockets.

Uses a new temporary room; does not deploy AWS workloads or open voice sessions.
"""
import base64
import copy
import importlib.util
import json
import pathlib
import time
import urllib.parse
import urllib.request
from websockets.sync.client import connect

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('movement_deploy', ROOT / 'deployment/deploy-movement-fix.py')
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


def receive(socket, predicate):
    until = time.monotonic() + 15
    while time.monotonic() < until:
        message = json.loads(socket.recv(timeout=max(.1, until - time.monotonic())))
        if predicate(message):
            return message
    raise AssertionError('Expected room message not received')


def main():
    session = deploy.session()
    stack = session.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
    outputs = {o['OutputKey']: o['OutputValue'] for o in stack['Outputs']}
    secret = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
    auth = 'Basic ' + base64.b64encode((secret['username'] + ':' + secret['password']).encode()).decode()

    def http(path, body):
        request = urllib.request.Request(outputs['ApiUrl'] + path, data=json.dumps(body).encode(),
            headers={'Content-Type': 'application/json', 'Authorization': auth})
        with urllib.request.urlopen(request, timeout=20) as response:
            return json.loads(response.read())

    graph = json.loads((ROOT / 'examples/api-serverless.json').read_text())
    for i, node in enumerate(graph['nodes']):
        node['position']['x'] = (i - 1) * .7
        node['viewScale'] = 0
    sockets, grants = [], []
    report = {}
    try:
        for i in range(4):
            grant = http('/v1/collab/rooms', dict(name='Movement QA ' + str(i), graph=graph, roomId=grants[0]['roomId'] if grants else ''))
            grants.append(grant)
            ticket = http('/v1/collab/ticket', {'token': grant['token']})
            socket = connect(ticket['websocketUrl'] + '?ticket=' + urllib.parse.quote(ticket['ticket']), open_timeout=15, close_timeout=3, max_queue=128, proxy=None)
            sockets.append(socket)
            for current in sockets:
                current.send(json.dumps({'action': 'heartbeat'}))
            receive(socket, lambda m: m.get('type') == 'snapshot')
        sockets[0].send(json.dumps({'action': 'sync'}))
        snapshot = receive(sockets[0], lambda m: m.get('type') == 'snapshot' and len(m['members']) == 4)
        graph = snapshot['graph']
        nodes = [n['id'] for n in graph['nodes'][:2]]
        leases = []
        for i, key in enumerate(nodes):
            request = 'qa-claim-' + str(i)
            sockets[i].send(json.dumps(dict(action='claim', objectId=key, requestId=request)))
            snapshot = receive(sockets[i], lambda m: m.get('requestId') == request)
            assert snapshot['accepted']
            leases.append(next(l for l in snapshot['locks'] if l['objectId'] == key))
        sockets[0].send(json.dumps(dict(action='claim', objectId=nodes[0], requestId='qa-repeat')))
        repeated = receive(sockets[0], lambda m: m.get('requestId') == 'qa-repeat')
        assert next(l for l in repeated['locks'] if l['objectId'] == nodes[0])['token'] == leases[0]['token']
        report['duplicateClaimKeepsToken'] = True
        positions = [dict(x=-.25, y=1.45, z=2.4), dict(x=.75, y=1.55, z=2.9)]
        pose = dict(head=dict(x=0,y=1.6,z=0), hand=dict(x=0,y=1,z=0), pointAt=dict(x=0,y=1,z=2), rotation=dict(x=0,y=0,z=0,w=1),
                    objectId=nodes[0], objectPosition=positions[0], leaseToken=leases[0]['token'], revision=0, moveSequence=1)
        sockets[0].send(json.dumps(dict(action='presence', pose=pose)))
        for socket in sockets[1:]:
            preview = receive(socket, lambda m: m.get('type') == 'presence')
            assert preview['pose']['objectPosition'] == positions[0]
        sockets[0].send(json.dumps({'action': 'sync'}))
        snapshot = receive(sockets[0], lambda m: m.get('type') == 'snapshot' and not m.get('requestId'))
        assert snapshot['graph'] == graph and snapshot['revision'] == 0
        report['previewReachesThreePeersWithoutCommit'] = True
        # Both clients drop using revision 0. The server must merge both objects.
        for i in range(2):
            sockets[i].send(json.dumps(dict(action='move', requestId='qa-drop-' + str(i), baseRevision=0,
                objectId=nodes[i], position=positions[i], leaseToken=leases[i]['token'])))
        for socket in sockets:
            seen = set()
            latest = None
            while len(seen) < 2:
                message = receive(socket, lambda m: m.get('requestId', '').startswith('qa-drop-'))
                assert message['accepted']
                seen.add(message['requestId'])
                if latest is None or message['revision'] > latest['revision']:
                    latest = message
            assert latest['revision'] == 2
            for i in range(2):
                assert next(n for n in latest['graph']['nodes'] if n['id'] == nodes[i])['position'] == positions[i]
            assert latest['graph']['links'] == graph['links']
        report['concurrentDropsConvergeOnFourClients'] = True
        sockets[0].send(json.dumps(dict(action='release', requestId='qa-release', objectId=nodes[0], leaseToken=leases[0]['token'])))
        released = receive(sockets[0], lambda m: m.get('requestId') == 'qa-release')
        assert released['accepted']
        sockets[2].send(json.dumps(dict(action='claim', requestId='qa-handoff', objectId=nodes[0])))
        handoff = receive(sockets[2], lambda m: m.get('requestId') == 'qa-handoff')
        assert handoff['accepted']
        sockets[0].send(json.dumps(dict(action='move', requestId='qa-stale', baseRevision=2, objectId=nodes[0], position=dict(x=0,y=1.4,z=2.5), leaseToken=leases[0]['token'])))
        stale = receive(sockets[0], lambda m: m.get('requestId') == 'qa-stale')
        assert not stale['accepted'] and stale['revision'] == 2
        report['releasedObjectCanBeGrabbedAndOldOwnerIsFenced'] = True
        report['participants'] = 4
    finally:
        for socket in sockets:
            socket.close()
    report['verifiedAtUtc'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
    (ROOT / 'deployment/movement-live-verification.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
