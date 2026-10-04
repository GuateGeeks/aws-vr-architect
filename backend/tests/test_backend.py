import base64
import copy
import io
import json
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import MagicMock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
sys.path.insert(0, str(ROOT / 'tools'))
os.environ.update(AWS_REGION='us-east-1', AWS_DEFAULT_REGION='us-east-1', AWS_EC2_METADATA_DISABLED='true', DEMO_PREFIX='ggawsday', ACCOUNT_ID='123456789012', AUTH_SECRET_ARN='secret', PROVISIONER_ROLE_ARN='arn:aws:iam::123456789012:role/provisioner', WORKLOAD_ROLE_ARN='arn:aws:iam::123456789012:role/workload')
import app
from graph import InvalidGraph, validate, digest, logical
from compiler import compile_graph
from build_template import build
from botocore.exceptions import ClientError


def graph(kinds=(0, 1, 2)):
    return {'schemaVersion': 1, 'region': 'us-east-1', 'nodes': [{'id': f'n{i}', 'kind': k, 'name': f'Node {i}', 'setting': 0} for i, k in enumerate(kinds)], 'links': [{'from': f'n{i}', 'to': f'n{i+1}'} for i in range(len(kinds)-1)]}


def compile_demo(data):
    return compile_graph(validate(data, 'us-east-1'), 'ggawsday-demo-1', '123456789012', os.environ['WORKLOAD_ROLE_ARN'])


class GraphTests(unittest.TestCase):
    def test_demo_secret_is_six_characters(self):
        secret = build()['Resources']['AuthSecret']['Properties']['GenerateSecretString']
        self.assertEqual(secret['PasswordLength'], 6)
        self.assertTrue(secret['ExcludePunctuation'])

    def test_all_unity_presets(self):
        for kinds in [(0, 1, 2), (5, 4, 1, 2), (3, 1, 2)]:
            self.assertEqual(len(validate(graph(kinds), 'us-east-1')['nodes']), len(kinds))

    def test_duplicate_ids_and_links(self):
        for duplicate_node in (True, False):
            g = graph()
            if duplicate_node:
                g['nodes'][1]['id'] = 'n0'
            else:
                g['links'].append(g['links'][0])
            with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')

    def test_cycle(self):
        g = graph((1, 4))
        g['links'].append({'from': 'n1', 'to': 'n0'})
        with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')

    def test_unknown_region_kind_id_setting_shape(self):
        for mutate in [lambda g: g.update(region='eu-west-1'), lambda g: g['nodes'][0].update(kind=99), lambda g: g['nodes'][0].update(id='../../evil'), lambda g: g['nodes'][0].update(setting=1), lambda g: g['nodes'][0].update(kind=True), lambda g: g.update(links=None), lambda g: g['links'][0].update(to='missing')]:
            g = graph(); mutate(g)
            with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')

    def test_limits_and_disconnected_nodes(self):
        for count in (1, 13):
            with self.assertRaises(InvalidGraph): validate(graph([1] * count), 'us-east-1')
        g = graph(); g['links'].pop()
        with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')

    def test_s3_fifo_and_multiple_notifications_rejected(self):
        g = graph((3, 4)); g['nodes'][1]['setting'] = 1
        with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')
        g = graph((3, 1, 2)); g['nodes'].append({'id': 'extra', 'kind': 4})
        g['links'].append({'from': 'n0', 'to': 'extra'})
        with self.assertRaises(InvalidGraph): validate(g, 'us-east-1')

    def test_hash_ignores_unity_layout_and_state(self):
        a = graph(); b = copy.deepcopy(a)
        b['nodes'].reverse(); b['links'].reverse()
        b['nodes'][0].update(position={'x': 1, 'y': 2, 'z': 3}, state=2)
        self.assertEqual(digest(validate(a, 'us-east-1')), digest(validate(b, 'us-east-1')))


class CompilerTests(unittest.TestCase):
    def test_api_is_iam_protected_and_dynamo_targets_wired(self):
        rs = compile_demo(graph())['Resources']
        self.assertEqual(rs[logical('n0') + 'Route']['Properties']['AuthorizationType'], 'AWS_IAM')
        targets = json.loads(rs[logical('n1')]['Properties']['Environment']['Variables']['TARGETS'])
        self.assertEqual(targets[0]['name'], rs[logical('n2')]['Properties']['TableName'])

    def test_s3_permission_has_no_circular_reference(self):
        rs = compile_demo(graph((3, 1, 2)))['Resources']
        bucket = rs[logical('n0')]
        permission = rs[bucket['DependsOn'][0]]
        self.assertIsInstance(permission['Properties']['SourceArn'], str)
        self.assertIn('LambdaConfigurations', bucket['Properties']['NotificationConfiguration'])
        self.assertTrue(bucket['Properties']['PublicAccessBlockConfiguration']['BlockPublicPolicy'])

    def test_event_queue_consumption_and_policy(self):
        rs = compile_demo(graph((5, 4, 1, 2)))['Resources']
        types = [r['Type'] for r in rs.values()]
        for kind in ['AWS::Events::Rule', 'AWS::SQS::QueuePolicy', 'AWS::Lambda::EventSourceMapping']:
            self.assertIn(kind, types)

    def test_policies_merge_two_producers(self):
        g = graph((5, 4, 1)); g['nodes'].append({'id': 's3', 'kind': 3})
        g['links'].append({'from': 's3', 'to': 'n1'})
        rs = compile_demo(g)['Resources']
        self.assertEqual(len(rs[logical('n1') + 'Policy']['Properties']['PolicyDocument']['Statement']), 2)

    def test_observability_and_settings(self):
        g = graph((1, 6)); g['nodes'][0]['setting'] = 3; g['nodes'][1]['setting'] = 2
        rs = compile_demo(g)['Resources']
        self.assertEqual(rs[logical('n0')]['Properties']['MemorySize'], 1024)
        self.assertEqual(rs[logical('n0') + 'Logs']['Properties']['RetentionInDays'], 30)
        self.assertEqual(rs[logical('n1')]['Type'], 'AWS::CloudWatch::Dashboard')

    def test_templates_have_no_reference_cycles(self):
        for kinds in [(0, 1, 2), (5, 4, 1, 2), (3, 1, 2), (3, 4, 1, 2), (1, 5, 4), (5, 1, 3), (1, 6)]:
            rs = compile_demo(graph(kinds))['Resources']
            def references(value):
                if isinstance(value, list): return set().union(*(references(v) for v in value))
                if not isinstance(value, dict): return set()
                found = set()
                if 'Ref' in value and value['Ref'] in rs: found.add(value['Ref'])
                if 'Fn::GetAtt' in value: found.add(value['Fn::GetAtt'][0])
                for v in value.values(): found |= references(v)
                return found
            deps = {k: references(v) | set(v.get('DependsOn', [])) for k, v in rs.items()}
            def visit(k, path):
                self.assertNotIn(k, path)
                for dep in deps[k]: visit(dep, path | {k})
            for k in rs: visit(k, set())

    def test_control_template_reproducible_and_no_admin(self):
        self.assertEqual(json.loads((ROOT / 'template.json').read_text()), build())
        text = json.dumps(build())
        self.assertNotIn('AdministratorAccess', text)
        self.assertNotIn('iam:CreateRole', text)
        self.assertIn('NotIpAddress', text)


class ApiTests(unittest.TestCase):
    def setUp(self):
        self.environment = patch.dict(os.environ, ALLOWED_CIDR='192.0.2.0/24')
        self.environment.start()
        self.addCleanup(self.environment.stop)
        app._secret = (float('inf'), {'username': 'quest-demo', 'password': 'a:b123'})
        self.context = MagicMock(aws_request_id='test')
        self.context.get_remaining_time_in_millis.return_value = 29000

    def event(self, method, path, data=None):
        return {'httpMethod': method, 'path': path, 'requestContext': {'identity': {'sourceIp': '192.0.2.8'}}, 'headers': {'Authorization': 'Basic ' + base64.b64encode(b'quest-demo:a:b123').decode()}, 'body': json.dumps(data) if data is not None else None}

    def test_auth_before_aws(self):
        with patch.object(app, 'client') as aws:
            event = self.event('GET', '/v1/session'); event['headers'] = {}
            result = app.handler(event, self.context)
            self.assertEqual(result['statusCode'], 401)
            aws.assert_not_called()
            self.assertIn('WWW-Authenticate', result['headers'])

    def test_source_ip_rejected_before_aws_and_auth_even_with_spoofed_headers(self):
        event = self.event('GET', '/v1/session')
        event['requestContext']['identity']['sourceIp'] = '198.51.100.9'
        event['headers'].update({'X-Forwarded-For': '192.0.2.8', 'X-Real-IP': '192.0.2.8'})
        with patch.object(app, 'client') as aws, patch.object(app, 'authenticate') as auth:
            self.assertEqual(app.handler(event, self.context)['statusCode'], 403)
            aws.assert_not_called()
            auth.assert_not_called()

    def test_source_context_missing_invalid_or_ipv6_fails_closed(self):
        for context in [None, {}, {'identity': {}}, {'identity': {'sourceIp': 'invalid'}}, {'identity': {'sourceIp': '::ffff:192.0.2.8'}}]:
            event = self.event('GET', '/v1/session'); event['requestContext'] = context
            self.assertEqual(app.handler(event, self.context)['statusCode'], 403)

    def test_cidr_missing_invalid_or_unrestricted_fails_closed(self):
        for cidr in ['', 'invalid', '0.0.0.0/0', '::/0']:
            with patch.dict(os.environ, ALLOWED_CIDR=cidr):
                self.assertEqual(app.handler(self.event('GET', '/v1/session'), self.context)['statusCode'], 403)
        with patch.dict(os.environ):
            del os.environ['ALLOWED_CIDR']
            self.assertEqual(app.handler(self.event('GET', '/v1/session'), self.context)['statusCode'], 403)

    def test_bad_auth_variants(self):
        for value in ['Basic !!!!', 'Bearer token', 'Basic ' + base64.b64encode(b'user:wrong').decode(), 'Basic ' + base64.b64encode(b'no-colon').decode()]:
            with self.assertRaises(app.ApiError): app.authenticate({'authorization': value})

    def test_colon_password_and_session(self):
        result = app.handler(self.event('GET', '/v1/session'), self.context)
        self.assertEqual(result['statusCode'], 200)

    def test_rotation_cache_refresh(self):
        app._secret = (0, {})
        secrets = MagicMock(); secrets.get_secret_value.return_value = {'SecretString': '{"username":"new","password":"Ab12Cd"}'}
        with patch.object(app, 'client', return_value=secrets):
            app.authenticate({'authorization': 'Basic ' + base64.b64encode(b'new:Ab12Cd').decode()})
        secrets.get_secret_value.assert_called_once_with(SecretId='secret')

    def test_bad_json_and_payload_limit(self):
        for raw, code in [('[]', 400), ('{', 400), ('{"x":NaN}', 400), ('x' * 65537, 413)]:
            event = self.event('POST', '/v1/architectures/validate'); event['body'] = raw
            self.assertEqual(app.handler(event, self.context)['statusCode'], code)

    def test_no_cloud_calls_for_invalid_design(self):
        with patch.object(app, 'client') as aws:
            result = app.handler(self.event('POST', '/v1/architectures/validate', graph((3, 3))), self.context)
            self.assertEqual(result['statusCode'], 400)
            aws.assert_not_called()

    def test_create_and_retry_are_same_stack(self):
        cfn = MagicMock()
        cfn.describe_stacks.side_effect = ClientError({'Error': {'Code': 'ValidationError', 'Message': 'Stack does not exist'}}, 'DescribeStacks')
        cfn.create_stack.return_value = {'StackId': 'stack-id'}
        data = {'deploymentId': '1', 'architecture': graph()}
        code, result = app.create(cfn, data)
        self.assertEqual(code, 202)
        args = cfn.create_stack.call_args.kwargs
        self.assertEqual(args['StackName'], 'ggawsday-demo-1')
        self.assertEqual(args['RoleARN'], os.environ['PROVISIONER_ROLE_ARN'])
        fingerprint = digest(validate(graph(), 'us-east-1'))
        cfn.describe_stacks.side_effect = None
        cfn.describe_stacks.return_value = {'Stacks': [{'StackStatus': 'CREATE_IN_PROGRESS', 'Tags': [{'Key': 'GraphHash', 'Value': fingerprint}]}]}
        with patch.object(app, 'status', return_value={'status': 'CREATE_IN_PROGRESS'}):
            self.assertEqual(app.create(cfn, data)[0], 200)
        cfn.create_stack.assert_called_once()

    def test_slot_collision_and_out_of_bounds(self):
        cfn = MagicMock(); cfn.describe_stacks.return_value = {'Stacks': [{'Tags': [], 'StackStatus': 'CREATE_COMPLETE'}]}
        with self.assertRaises(app.ApiError) as caught: app.create(cfn, {'deploymentId': '1', 'architecture': graph()})
        self.assertEqual(caught.exception.status, 409)
        for slot in ['4', '../stack', True, None]:
            with self.assertRaises(app.ApiError): app.stack_name(slot)

    def test_failed_stack_never_success(self):
        cfn = MagicMock(); cfn.describe_stacks.return_value = {'Stacks': [{'StackId': 'id', 'StackStatus': 'ROLLBACK_COMPLETE'}]}
        cfn.get_template.return_value = {'TemplateBody': compile_demo(graph())}
        with patch.object(app, 'resources', return_value=[]): result = app.status(cfn, 'name', '1')
        self.assertTrue(result['finished']); self.assertFalse(result['success'])

    def test_delete_in_progress_does_not_purge(self):
        cfn = MagicMock(); cfn.describe_stacks.return_value = {'Stacks': [{'StackStatus': 'CREATE_IN_PROGRESS'}]}
        with patch.object(app, 'purge_buckets') as purge:
            with self.assertRaises(app.ApiError): app.delete(cfn, 'name', '1', True, self.context)
            purge.assert_not_called()

    def test_purge_versions_and_delete_markers(self):
        s3 = MagicMock(); s3.list_object_versions.side_effect = [{'Versions': [{'Key': 'x', 'VersionId': '1'}], 'DeleteMarkers': [{'Key': 'x', 'VersionId': '2'}]}, {}]
        s3.delete_objects.return_value = {}
        rs = [{'ResourceType': 'AWS::S3::Bucket', 'PhysicalResourceId': 'ggawsday-demo-1-abc-123456789012'}]
        with patch.object(app, 'resources', return_value=rs), patch.object(app, 'client', return_value=s3):
            self.assertTrue(app.purge_buckets(MagicMock(), 'ggawsday-demo-1', self.context))
        self.assertEqual(len(s3.delete_objects.call_args.kwargs['Delete']['Objects']), 2)

    def test_purge_is_resumable(self):
        self.context.get_remaining_time_in_millis.return_value = 7000
        rs = [{'ResourceType': 'AWS::S3::Bucket', 'PhysicalResourceId': 'ggawsday-demo-1-abc-123456789012'}]
        with patch.object(app, 'resources', return_value=rs), patch.object(app, 'client'):
            self.assertFalse(app.purge_buckets(MagicMock(), 'ggawsday-demo-1', self.context))

    def test_secrets_not_returned_in_errors(self):
        with patch.object(app, 'route', side_effect=RuntimeError('secret password private detail')):
            result = app.handler({}, self.context)
        self.assertEqual(result['statusCode'], 502)
        self.assertNotIn('private detail', result['body'])


class WorkloadTests(unittest.TestCase):
    def test_s3_via_sqs_preserves_event_id(self):
        import workload
        aws = MagicMock()
        stream = MagicMock()
        stream.__enter__.return_value.read.return_value = b'{"id":"original-event"}'
        aws.get_object.return_value = {'Body': stream}
        envelope = {'Records': [{'eventSource': 'aws:s3', 's3': {'bucket': {'name': 'demo-bucket'}, 'object': {'key': 'demo%2Fevent.json'}}}]}
        event = {'Records': [{'eventSource': 'aws:sqs', 'body': json.dumps(envelope)}]}
        with patch.dict(os.environ, NODE_ID='lambda1', TARGETS='[{"kind":2,"name":"demo-table"}]'), patch.object(workload.boto3, 'client', return_value=aws):
            workload.handler(event, None)
        self.assertEqual(aws.put_item.call_args.kwargs['Item']['id']['S'], 'original-event')
        aws.get_object.assert_called_once_with(Bucket='demo-bucket', Key='demo/event.json')

    def test_eventbridge_via_sqs_preserves_event_id(self):
        import workload
        event = {'Records': [{'eventSource': 'aws:sqs', 'body': json.dumps({'detail': {'id': 'original-event'}})}]}
        self.assertEqual(list(workload.payloads(event)), [{'id': 'original-event'}])

    def test_s3_configuration_test_event_is_ignored(self):
        import workload
        self.assertEqual(list(workload.payloads({'Event': 's3:TestEvent'})), [])

    def test_queue_message_writes_connected_table(self):
        import workload
        ddb = MagicMock()
        with patch.dict(os.environ, NODE_ID='lambda1', TARGETS='[{"kind":2,"name":"demo-table"}]'), patch.object(workload.boto3, 'client', return_value=ddb):
            result = workload.handler({'Records': [{'eventSource': 'aws:sqs', 'body': '{"id":"test-id"}'}]}, None)
        self.assertEqual(result['statusCode'], 200)
        self.assertEqual(ddb.put_item.call_args.kwargs['Item']['id']['S'], 'test-id')

    def test_eventbridge_partial_failure_raises(self):
        import workload
        events = MagicMock(); events.put_events.return_value = {'FailedEntryCount': 1}
        with patch.dict(os.environ, NODE_ID='lambda1', TARGETS='[{"kind":5,"name":"demo-bus"}]'), patch.object(workload.boto3, 'client', return_value=events):
            with self.assertRaises(RuntimeError): workload.handler({'id': 'test-id'}, None)


if __name__ == '__main__': unittest.main()
