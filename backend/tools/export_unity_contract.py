"""Generate offline Unity fixtures from the real Lambda routes with AWS clients mocked."""
import argparse
import base64
import json
import os
from pathlib import Path
import sys
import time
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
from botocore.exceptions import ClientError
import app
from graph import validate, digest, logical
from compiler import compile_graph


def export():
    graph = json.loads((ROOT / 'examples/api-serverless.json').read_text(encoding='utf-8'))
    graph['region'] = 'us-east-1'
    for index, node in enumerate(graph['nodes']):
        node['position'] = {'x': (index - (len(graph['nodes']) - 1) * .5) * .88, 'y': 1.52, 'z': 2.65}
    fingerprint = digest(validate(graph, 'us-east-1'))
    cfn = MagicMock()
    cfn.create_stack.return_value = {'StackId': 'contract-stack-1'}
    cfn.describe_stacks.side_effect = ClientError({'Error': {'Code': 'ValidationError', 'Message': 'Stack does not exist'}}, 'DescribeStacks')
    outputs = {'graph': graph}
    env = dict(AWS_REGION='us-east-1', ACCOUNT_ID='123456789012', DEMO_PREFIX='ggawsday',
        WORKLOAD_ROLE_ARN='arn:aws:iam::123456789012:role/workload', PROVISIONER_ROLE_ARN='arn:aws:iam::123456789012:role/provisioner', ALLOWED_CIDR='192.0.2.1/32')
    def call(name, method, path, body=None):
        response = app.handler({'httpMethod': method, 'path': path,
            'requestContext': {'identity': {'sourceIp': '192.0.2.1'}},
            'headers': {'Authorization': 'Basic ' + base64.b64encode(b'contract:Test42').decode()},
            'body': json.dumps(body) if body is not None else None}, SimpleNamespace(aws_request_id='offline-contract'))
        outputs[name] = {'Code': response['statusCode'], 'Json': response['body']}
        assert response['statusCode'] in (200, 202), (name, response)
    with patch.dict(os.environ, env), patch.object(app, '_secret', (time.monotonic() + 60, {'username': 'contract', 'password': 'Test42'})), patch.object(app, 'client', return_value=cfn):
        call('session', 'GET', '/v1/session')
        call('catalog', 'GET', '/v1/catalog')
        call('validation', 'POST', '/v1/architectures/validate', graph)
        call('created', 'POST', '/v1/deployments', {'deploymentId': '1', 'architecture': graph})
        cfn.describe_stacks.side_effect = None
        stack = {'StackId': 'contract-stack-1', 'StackStatus': 'CREATE_IN_PROGRESS', 'Tags': [{'Key': 'GraphHash', 'Value': fingerprint}]}
        cfn.describe_stacks.return_value = {'Stacks': [stack]}
        cfn.get_template.return_value = {'TemplateBody': compile_graph(validate(graph, 'us-east-1'), 'ggawsday-demo-1', env['ACCOUNT_ID'], env['WORKLOAD_ROLE_ARN'])}
        with patch.object(app, 'resources', return_value=[]):
            call('pending', 'GET', '/v1/deployments/1')
        stack['StackStatus'] = 'CREATE_COMPLETE'
        resources = [{'LogicalResourceId': logical(n['id']), 'ResourceStatus': 'CREATE_COMPLETE', 'PhysicalResourceId': 'offline-resource-' + n['id']} for n in graph['nodes']]
        with patch.object(app, 'resources', return_value=resources):
            call('ready', 'GET', '/v1/deployments/1')
            call('accepted', 'POST', '/v1/deployments/1/events', {'resourceId': next(n['id'] for n in graph['nodes'] if n['kind'] == 1)})
        # Event IDs are normally random; deterministic fixtures stay reviewable.
        accepted = json.loads(outputs['accepted']['Json']); accepted['eventId'] = 'offline-event-1'
        outputs['accepted']['Json'] = json.dumps(accepted, ensure_ascii=False)
        stack['StackStatus'] = 'ROLLBACK_COMPLETE'
        with patch.object(app, 'resources', return_value=[]):
            call('rollback', 'GET', '/v1/deployments/1')
    return outputs


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(export(), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Wrote offline Lambda route contract fixtures; no AWS calls.')
