import copy
import json
import os
import sys
import time
import unittest
from pathlib import Path
from unittest.mock import patch, MagicMock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
import collaboration as c


def graph():
    return c.normalize_graph(dict(schemaVersion=1, region='us-east-1', nodes=[
        dict(id='api', kind=0, name='API', setting=0, position=dict(x=-.7, y=1.5, z=2.5)),
        dict(id='fn', kind=1, name='Lambda', setting=0, position=dict(x=0, y=1.5, z=2.5)),
        dict(id='db', kind=2, name='DynamoDB', setting=0, position=dict(x=.7, y=1.5, z=2.5))],
        links=[{'from': 'api', 'to': 'fn'}, {'from': 'fn', 'to': 'db'}]), 'us-east-1')


def room():
    return dict(roomId='ABCDEF12', revision=0, version=1, hostId='host', graph=graph(), locks={}, receipts=[], operations=[],
                expiresAt=int(time.time()) + 3600, members={key: dict(name=key, role='facilitator' if key == 'host' else 'editor', station=index,
                    expiresAt=int(time.time()) + 100, connectionId='socket-' + key) for index, key in enumerate(('host', 'editor'))})


def edit(value, node='fn', name='New name', request='op1'):
    result = copy.deepcopy(value['graph'])
    next(n for n in result['nodes'] if n['id'] == node)['name'] = name
    return dict(action='op', requestId=request, baseRevision=value['revision'], graph=result, leases=[])


class Transitions(unittest.TestCase):
    def setUp(self):
        self.value, self.now = room(), int(time.time())

    def apply(self, data, user='editor'):
        c.apply_command(self.value, user, data, self.now)

    def test_editor_commits_with_revision_and_no_client_identity(self):
        self.apply(edit(self.value))
        self.assertEqual(1, self.value['revision'])
        self.assertEqual('editor', self.value['operations'][0]['userId'])

    def test_stale_revision_rejected(self):
        command = edit(self.value)
        self.apply(edit(self.value, 'db', 'Other', 'op2'))
        with self.assertRaisesRegex(ValueError, 'Revisión'):
            self.apply(command)

    def move(self, node, position, request='move1', revision=0):
        return dict(action='move', requestId=request, baseRevision=revision, objectId=node,
                    position=position, leaseToken=self.value['locks'][node]['token'])

    def test_concurrent_drops_merge_positions_without_reverting_other_changes(self):
        self.apply(dict(action='claim', objectId='fn'))
        self.apply(dict(action='claim', objectId='db'), 'host')
        first = self.move('fn', dict(x=.25, y=1.3, z=2.4))
        second = self.move('db', dict(x=1.2, y=1.5, z=3), 'move2')
        self.apply(first)
        self.apply(second, 'host')
        self.assertEqual(2, self.value['revision'])
        self.assertEqual(first['position'], next(n for n in self.value['graph']['nodes'] if n['id'] == 'fn')['position'])
        self.assertEqual(second['position'], next(n for n in self.value['graph']['nodes'] if n['id'] == 'db')['position'])
        self.assertEqual(graph()['links'], self.value['graph']['links'])
        self.apply(second, 'host')
        self.assertEqual(2, self.value['revision'], 'A duplicate drop must not reapply')

    def test_move_requires_live_matching_lease_and_valid_position(self):
        self.apply(dict(action='claim', objectId='fn'))
        command = self.move('fn', dict(x=.25, y=1.3, z=2.4))
        with self.assertRaisesRegex(ValueError, 'Concesión'):
            self.apply(command, 'host')
        command['leaseToken'] = 'outdated'
        with self.assertRaisesRegex(ValueError, 'Concesión'):
            self.apply(command)
        command['leaseToken'] = self.value['locks']['fn']['token']
        command['position']['x'] = float('nan')
        with self.assertRaisesRegex(ValueError, 'Posición'):
            self.apply(command)
        command['position']['x'] = .25
        self.value['locks']['fn']['expiresAt'] = self.now
        with self.assertRaisesRegex(ValueError, 'Concesión'):
            self.apply(command)

    def test_duplicate_claim_keeps_current_token(self):
        self.apply(dict(action='claim', objectId='fn'))
        token = self.value['locks']['fn']['token']
        self.apply(dict(action='claim', objectId='fn'))
        self.assertEqual(token, self.value['locks']['fn']['token'])

    def test_duplicate_does_not_reapply_or_increment_revision(self):
        command = edit(self.value)
        self.apply(command)
        self.apply(command)
        self.assertEqual(1, self.value['revision'])

    def test_same_request_id_is_scoped_to_user(self):
        self.apply(edit(self.value), 'host')
        self.apply(edit(self.value, 'db', 'Other'))
        self.assertEqual(2, self.value['revision'])

    def test_foreign_lock_blocks_node_and_incident_link_edits(self):
        self.apply(dict(action='claim', objectId='fn'), 'host')
        with self.assertRaisesRegex(ValueError, 'otro usuario'):
            self.apply(edit(self.value))
        command = edit(self.value, 'api')
        command['graph']['links'] = []
        with self.assertRaisesRegex(ValueError, 'otro usuario'):
            self.apply(command)

    def test_owned_lease_requires_current_token(self):
        self.apply(dict(action='claim', objectId='fn'))
        with self.assertRaisesRegex(ValueError, 'Concesión'):
            self.apply(edit(self.value))
        command = edit(self.value)
        command['leases'] = [dict(objectId='fn', token=self.value['locks']['fn']['token'])]
        self.apply(command)
        self.assertEqual(1, self.value['revision'])

    def test_old_release_cannot_release_new_owner(self):
        self.apply(dict(action='claim', objectId='fn'))
        old = self.value['locks']['fn']['token']
        self.value['locks']['fn']['expiresAt'] = self.now - 1
        self.apply(dict(action='claim', objectId='fn'), 'host')
        with self.assertRaisesRegex(ValueError, 'Concesión'):
            self.apply(dict(action='release', objectId='fn', leaseToken=old))
        self.assertEqual('host', self.value['locks']['fn']['owner'])

    def test_expired_lock_is_available_without_ttl_deletion(self):
        self.value['locks']['fn'] = dict(owner='host', token='old', expiresAt=self.now - 1)
        self.apply(dict(action='claim', objectId='fn'))
        self.assertEqual('editor', self.value['locks']['fn']['owner'])

    def test_heartbeat_cannot_resurrect_expired_lease(self):
        self.value['locks']['fn'] = dict(owner='editor', token='old', expiresAt=self.now - 1)
        self.apply(dict(action='heartbeat'))
        self.assertLess(self.value['locks']['fn']['expiresAt'], self.now)

    def test_undo_preserves_other_users_unrelated_edit(self):
        self.apply(edit(self.value, 'fn', 'Mine'))
        self.apply(edit(self.value, 'db', 'Theirs', 'op2'), 'host')
        self.apply(dict(action='undo', requestId='undo1', baseRevision=2))
        names = {n['id']: n['name'] for n in self.value['graph']['nodes']}
        self.assertEqual('Lambda', names['fn'])
        self.assertEqual('Theirs', names['db'])

    def test_undo_rejects_changed_target(self):
        self.apply(edit(self.value, 'fn', 'Mine'))
        self.apply(edit(self.value, 'fn', 'Theirs', 'op2'), 'host')
        with self.assertRaisesRegex(ValueError, 'Otro usuario'):
            self.apply(dict(action='undo', requestId='undo1', baseRevision=2))

    def test_bulk_delete_requires_facilitator_even_if_client_omits_flag(self):
        command = edit(self.value)
        command['graph']['nodes'] = []
        command['graph']['links'] = []
        with self.assertRaisesRegex(ValueError, 'facilitador'):
            self.apply(command)

    def test_graph_can_be_incomplete_while_building(self):
        value = graph()
        value['nodes'] = [value['nodes'][0]]
        value['links'] = []
        self.assertEqual(1, len(c.normalize_graph(value, 'us-east-1')['nodes']))

    def test_graph_rejects_duplicate_edges_cycles_and_nonfinite_positions(self):
        value = graph(); value['links'].append(value['links'][0])
        with self.assertRaises(ValueError): c.normalize_graph(value, 'us-east-1')
        value = graph(); value['nodes'][0]['position']['x'] = float('nan')
        with self.assertRaises(ValueError): c.normalize_graph(value, 'us-east-1')
        value = graph(); value['nodes'][0]['kind'] = 3; value['links'].append({'from':'fn','to':'api'})
        with self.assertRaises(ValueError): c.normalize_graph(value, 'us-east-1')

    def test_deployment_blocks_edits(self):
        self.value['deployment'] = dict(finished=False, expiresAt=self.now + 60)
        with self.assertRaisesRegex(ValueError, 'despliegue'):
            self.apply(edit(self.value))

    def test_layout_edit_keeps_deployment_revision_but_definition_edit_invalidates(self):
        self.value['deployment'] = dict(finished=True, success=True, revision=0)
        command = edit(self.value)
        command['graph']['nodes'] = copy.deepcopy(self.value['graph']['nodes'])
        command['graph']['nodes'][0]['position']['x'] += .1
        self.apply(command)
        self.assertEqual(1, self.value['deployment']['revision'])
        self.apply(edit(self.value, request='definition'))
        self.assertEqual(1, self.value['deployment']['revision'])
        self.assertEqual(2, self.value['revision'])

    def test_expired_membership_rejected(self):
        self.value['members']['editor']['expiresAt'] = self.now - 1
        with self.assertRaisesRegex(ValueError, 'membresía'):
            self.apply(edit(self.value))

    def test_facilitator_sets_the_room_table_without_a_design_revision(self):
        self.assertEqual(3, c.snapshot(self.value)['tableSize'], 'Rooms created before table sizes use the full table')
        self.apply(dict(action='table', tableSize=1), 'host')
        self.assertEqual(1, self.value['tableSize'])
        self.assertEqual(1, c.snapshot(self.value)['tableSize'])
        self.assertEqual(0, self.value['revision'])
        self.assertEqual([], self.value['operations'])

    def test_only_the_facilitator_changes_the_table_and_sizes_are_validated(self):
        with self.assertRaisesRegex(ValueError, 'facilitador'):
            self.apply(dict(action='table', tableSize=2))
        for bad in (0, 4, '2', True, None, 2.0):
            with self.assertRaisesRegex(ValueError, 'Tamaño'):
                self.apply(dict(action='table', tableSize=bad), 'host')
        self.assertNotIn('tableSize', self.value)

    def test_snapshot_excludes_history_tokens_and_expired_peers(self):
        self.value['members']['editor']['expiresAt'] = self.now - 1
        self.value['locks']['fn'] = dict(owner='host', token='lease', expiresAt=self.now - 1)
        result = c.snapshot(self.value)
        self.assertEqual(1, len(result['members']))
        self.assertEqual([], result['locks'])
        self.assertNotIn('operations', result)
        self.assertNotIn('connectionId', json.dumps(result))


class MemoryStore:
    values = {}
    def __init__(self, api): pass
    def get(self, key): return copy.deepcopy(self.values.get(key))
    def put(self, key, value, previous=None):
        value['version'] = 1 if previous is None else previous + 1
        self.values[key] = copy.deepcopy(value)
    def mutate(self, key, transition):
        value = self.get(key)
        c.require(value is not None and value['expiresAt'] > time.time(), 'Sala vencida.')
        previous = value['version']; transition(value); self.put(key, value, previous)
        return value


class Lifecycle(unittest.TestCase):
    def setUp(self):
        MemoryStore.values = {}
        self.store_patch = patch.object(c, 'Store', MemoryStore); self.store_patch.start()
        self.env_patch = patch.dict(os.environ, AWS_REGION='us-east-1', COLLAB_WS_URL='wss://rooms.example/rooms'); self.env_patch.start()
        self.api = MagicMock()
    def tearDown(self):
        self.store_patch.stop(); self.env_patch.stop()
    def join(self, code=''):
        return c.bootstrap(dict(roomId=code, name='Test', graph=graph()), self.api)

    def test_four_distinct_identities_and_exclusive_stations(self):
        first = self.join()
        guests = [first] + [self.join(first['roomId']) for _ in range(3)]
        self.assertEqual(4, len({g['userId'] for g in guests}))
        self.assertEqual({0, 1, 2, 3}, {g['station'] for g in guests})
        self.assertEqual(['facilitator', 'editor', 'editor', 'editor'], [g['role'] for g in guests])
        with self.assertRaisesRegex(ValueError, 'llena'): self.join(first['roomId'])

    def test_new_room_starts_with_the_creators_table_and_guests_cannot_resize_it(self):
        host = c.bootstrap(dict(roomId='', name='Host', graph=graph(), tableSize=2), self.api)
        guest = c.bootstrap(dict(roomId=host['roomId'], name='Guest', graph=graph(), tableSize=1), self.api)
        self.assertEqual(2, MemoryStore.values['room:' + host['roomId']]['tableSize'])
        legacy = self.join()
        self.assertEqual(3, MemoryStore.values['room:' + legacy['roomId']]['tableSize'], 'Older apps send no size')
        with self.assertRaisesRegex(ValueError, 'Tamaño'):
            c.bootstrap(dict(roomId='', name='Bad', graph=graph(), tableSize=7), self.api)
        ticket = c.connection_ticket(guest['token'], self.api)['ticket']
        c.websocket(dict(requestContext=dict(routeKey='$connect', connectionId='guest'), queryStringParameters={'ticket': ticket}), self.api)
        with patch.object(c, 'send') as send:
            c.websocket(dict(requestContext=dict(routeKey='$default', connectionId='guest'), body=json.dumps(dict(action='table', tableSize=1, requestId='t1'))), self.api)
            reply = send.call_args.args[2]
        self.assertFalse(reply['accepted']); self.assertIn('facilitador', reply['message']); self.assertEqual(2, reply['tableSize'])

    def test_ticket_consumed_once_and_reconnect_fences_old_socket(self):
        first = self.join()
        ticket = c.connection_ticket(first['token'], self.api)['ticket']
        event = dict(requestContext=dict(routeKey='$connect', connectionId='one'), queryStringParameters={'ticket': ticket})
        c.websocket(event, self.api)
        with self.assertRaisesRegex(ValueError, 'usado'): c.websocket(event, self.api)
        ticket = c.connection_ticket(first['token'], self.api)['ticket']
        c.websocket(dict(requestContext=dict(routeKey='$connect', connectionId='two'), queryStringParameters={'ticket':ticket}), self.api)
        with self.assertRaisesRegex(ValueError, 'reemplazada'):
            c.websocket(dict(requestContext=dict(routeKey='$default', connectionId='one'), body='{"action":"sync"}'), self.api)

    def test_bearer_editor_cannot_deploy_or_delete(self):
        host = self.join(); editor = self.join(host['roomId'])
        for method, path in [('POST', '/v1/deployments'), ('DELETE', '/v1/deployments/1'), ('POST', '/v1/code/1/publish')]:
            with self.assertRaisesRegex(ValueError, 'facilitador'):
                c.authorize_http(dict(httpMethod=method, path=path), editor['token'], self.api)

    def test_aws_status_cannot_claim_success_for_a_different_definition(self):
        host=self.join()
        value=MemoryStore.values['room:'+host['roomId']]
        value['deployment']=dict(deploymentId='1',fingerprint='expected',status='REQUESTED',finished=False,success=False,expiresAt=time.time()+60)
        c.record_deployment(host['token'],dict(deploymentId='1',graphHash='different',stackId='stack',status='CREATE_COMPLETE',finished=True,success=True),self.api)
        result=MemoryStore.values['room:'+host['roomId']]['deployment']
        self.assertFalse(result['success']);self.assertEqual('DEFINITION_MISMATCH',result['status'])

    def test_shared_event_credential_cannot_bypass_reserved_room_slot(self):
        MemoryStore.values['slot:1'] = dict(roomId='ABCDEF12',expiresAt=time.time()+100,version=1)
        self.api.body.return_value={'deploymentId':'1'}
        with patch.dict(os.environ,COLLAB_TABLE='rooms'):
            for method,path in [('POST','/v1/deployments'),('DELETE','/v1/deployments/1'),('POST','/v1/deployments/1/code/publish')]:
                with self.assertRaisesRegex(ValueError,'reservado'):
                    c.authorize_event_write(dict(httpMethod=method,path=path),self.api)
            c.authorize_event_write(dict(httpMethod='GET',path='/v1/deployments/1'),self.api)

    def test_unknown_or_expired_token_rejected(self):
        with self.assertRaises(ValueError): c.connection_ticket('x' * 32, self.api)
        host = self.join(); MemoryStore.values[c.token_key(host['token'])]['expiresAt'] = int(time.time()) - 1
        with self.assertRaises(ValueError): c.connection_ticket(host['token'], self.api)

    def test_presence_does_not_persist_audio_or_pose_and_rate_limit_applies(self):
        host = self.join()
        ticket = c.connection_ticket(host['token'], self.api)['ticket']
        c.websocket(dict(requestContext=dict(routeKey='$connect', connectionId='one'), queryStringParameters={'ticket':ticket}), self.api)
        pose = dict(head=dict(x=0,y=1.6,z=0), hand=dict(x=0,y=1,z=0), pointAt=dict(x=0,y=1,z=2), rotation=dict(x=0,y=0,z=0,w=1), focusId='', pointing=False, transcript='PRIVATE')
        event = dict(requestContext=dict(routeKey='$default', connectionId='one'), body=json.dumps(dict(action='presence', pose=pose)))
        for _ in range(16): c.websocket(event, self.api)
        with self.assertRaisesRegex(ValueError, 'frecuente'): c.websocket(event, self.api)
        self.assertNotIn('PRIVATE', json.dumps(MemoryStore.values))
        self.assertNotIn('pose', MemoryStore.values['connection:one'])

    def test_drag_preview_is_transient_and_fenced_by_lease_and_revision(self):
        host = self.join()
        ticket = c.connection_ticket(host['token'], self.api)['ticket']
        c.websocket(dict(requestContext=dict(routeKey='$connect', connectionId='one'), queryStringParameters={'ticket': ticket}), self.api)
        key = 'room:' + host['roomId']
        c.apply_command(MemoryStore.values[key], host['userId'], dict(action='claim', objectId='fn'), int(time.time()))
        lease = MemoryStore.values[key]['locks']['fn']
        pose = dict(head=dict(x=0,y=1.6,z=0), hand=dict(x=0,y=1,z=0), pointAt=dict(x=0,y=1,z=2), rotation=dict(x=0,y=0,z=0,w=1),
                    objectId='fn', objectPosition=dict(x=.2,y=1.4,z=2.5), leaseToken=lease['token'], revision=0, moveSequence=1)
        def preview():
            c.websocket(dict(requestContext=dict(routeKey='$default', connectionId='one'), body=json.dumps(dict(action='presence', pose=pose))), self.api)
        committed = copy.deepcopy(MemoryStore.values[key]['graph'])
        with patch.object(c, 'broadcast') as broadcast:
            preview()
            self.assertEqual('fn', broadcast.call_args.args[2]['pose']['objectId'])
            self.assertEqual(committed, MemoryStore.values[key]['graph'])
            self.assertNotIn('objectPosition', json.dumps(MemoryStore.values))
            pose['leaseToken'] = 'forged'; preview()
            self.assertNotIn('objectId', broadcast.call_args.args[2]['pose'])
            pose['leaseToken'] = lease['token']; pose['revision'] = -1; preview()
            self.assertNotIn('objectId', broadcast.call_args.args[2]['pose'])
            pose['revision'] = 0; pose['objectPosition']['x'] = 50; preview()
            self.assertNotIn('objectId', broadcast.call_args.args[2]['pose'])


class ConditionalStore(unittest.TestCase):
    def test_racing_write_reloads_state_instead_of_overwriting_newer_revision(self):
        from botocore.exceptions import ClientError
        api=MagicMock(); db=api.client.return_value
        value=room()
        newer=copy.deepcopy(value);newer['version']=2;newer['revision']=1
        newer['graph']['nodes'][2]['name']='Concurrent edit'
        db.get_item.side_effect=[{'Item':{'data':{'S':json.dumps(v)}}} for v in (value,newer)]
        db.put_item.side_effect=[ClientError({'Error':{'Code':'ConditionalCheckFailedException'}},'PutItem'),None]
        with patch.dict(os.environ,COLLAB_TABLE='rooms'):
            store=c.Store(api)
            result=store.mutate('room:ABCDEF12', lambda r: c.apply_command(r,'editor',dict(action='heartbeat'),int(time.time())))
        self.assertEqual(1,result['revision'])
        self.assertEqual('Concurrent edit',result['graph']['nodes'][2]['name'])
        call=db.put_item.call_args.kwargs
        self.assertEqual({'N':'2'},call['ExpressionAttributeValues'][':v'])
        self.assertEqual('#v = :v',call['ConditionExpression'])

if __name__ == '__main__': unittest.main()
