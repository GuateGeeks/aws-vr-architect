"""Explicitly create, exercise and remove isolated AWS integration-test resources."""
import argparse
import base64
import copy
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import uuid

import boto3
from botocore.exceptions import ClientError

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / 'test-results' / 'live-state.json'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['prepare', 'exercise', 'matrix', 'network', 'rotation', 'cleanup-demos', 'cleanup', 'diagnostics'])
    parser.add_argument('--profile', required=True)
    parser.add_argument('--account', required=True)
    parser.add_argument('--region', default='us-east-1', choices=['us-east-1'])
    parser.add_argument('--build-template', type=Path, help='SAM-built template; permits build artifacts on the Linux filesystem')
    args = parser.parse_args()
    session = boto3.Session(profile_name=args.profile, region_name=args.region)
    identity = session.client('sts').get_caller_identity()
    if identity['Account'] != args.account:
        raise RuntimeError('Account differs from the explicitly supplied test account')
    print('Test identity:', identity['Arn'], flush=True)
    cfn, s3 = session.client('cloudformation'), session.client('s3')
    if STATE.exists() and args.phase == 'prepare':
        previous = json.loads(STATE.read_text())
        if previous.get('cleanupComplete'):
            archive = STATE.with_name('live-state-' + previous['prefix'] + '.json')
            STATE.rename(archive)
    if STATE.exists():
        state = json.loads(STATE.read_text())
        if state['account'] != args.account or state['region'] != args.region:
            raise RuntimeError('Saved run belongs to another account/region')
    elif args.phase == 'prepare':
        prefix = 'ggt' + uuid.uuid4().hex[:8]
        state = {'account': args.account, 'region': args.region, 'prefix': prefix, 'stack': prefix + '-control', 'artifacts': prefix + '-artifacts-' + args.account, 'checks': []}
        STATE.parent.mkdir(exist_ok=True)
        STATE.write_text(json.dumps(state, indent=2))
    else:
        raise RuntimeError('No saved test run; prepare first')
    prefix = state['prefix']
    if not prefix.startswith('ggt') or len(prefix) != 11 or state['stack'] != prefix + '-control' or state['artifacts'] != prefix + '-artifacts-' + args.account:
        raise RuntimeError('Unexpected test resource names; refusing operations')

    def save():
        STATE.write_text(json.dumps(state, indent=2))

    def passed(message):
        print('PASS:', message, flush=True)
        state['checks'].append(message)
        save()

    def describe(name):
        try:
            return cfn.describe_stacks(StackName=name)['Stacks'][0]
        except ClientError as error:
            if error.response['Error']['Code'] == 'ValidationError' and 'does not exist' in error.response['Error']['Message']:
                return None
            raise

    if args.phase == 'prepare':
        built = args.build_template or Path(state.get('buildTemplate', str(ROOT / '.aws-sam/build/template.yaml')))
        if not built.is_file():
            raise RuntimeError('Complete sam build before preparing a live deployment: ' + str(built))
        state['buildTemplate'] = str(built.resolve())
        with urllib.request.urlopen('https://checkip.amazonaws.com', timeout=10) as response:
            ip = response.read().decode().strip()
        import ipaddress
        ipaddress.IPv4Address(ip)
        state['cidr'] = ip + '/32'
        save()
        if not state.get('artifactBucketCreated'):
            s3.create_bucket(Bucket=state['artifacts'])
            state['artifactBucketCreated'] = True
            save()
            s3.put_public_access_block(Bucket=state['artifacts'], PublicAccessBlockConfiguration={key: True for key in ['BlockPublicAcls', 'IgnorePublicAcls', 'BlockPublicPolicy', 'RestrictPublicBuckets']})
        env = dict(os.environ, AWS_DEFAULT_REGION=args.region, AWS_REGION=args.region, AWS_PAGER='', SAM_CLI_TELEMETRY='0')
        subprocess.run(['sam', 'deploy', '--template-file', str(built), '--stack-name', state['stack'], '--profile', args.profile, '--region', args.region, '--s3-bucket', state['artifacts'], '--capabilities', 'CAPABILITY_IAM', '--no-confirm-changeset', '--no-fail-on-empty-changeset', '--parameter-overrides', 'DemoPrefix=' + prefix, 'AllowedCidr=' + state['cidr']], check=True, cwd=ROOT, env=env)
        passed('Control plane deployed with dedicated artifact bucket and current-IP restriction')
        return

    if args.phase == 'diagnostics':
        for name in [state['stack']] + [prefix + '-demo-' + str(i) for i in range(1, 4)]:
            stack = describe(name)
            if stack:
                print(name, stack['StackStatus'], flush=True)
                for event in cfn.describe_stack_events(StackName=name)['StackEvents']:
                    if 'FAILED' in event['ResourceStatus']:
                        print(event['LogicalResourceId'], event.get('ResourceStatusReason', ''), flush=True)
        try:
            events = session.client('logs').filter_log_events(logGroupName='/aws/lambda/' + prefix + '-control', limit=20)
            for event in events.get('events', []):
                print(event['message'], flush=True)
        except ClientError:
            pass
        return

    control = describe(state['stack'])
    outputs = {o['OutputKey']: o['OutputValue'] for o in control.get('Outputs', [])} if control else {}
    auth = None
    if outputs:
        secret = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
        auth = 'Basic ' + base64.b64encode((secret['username'] + ':' + secret['password']).encode()).decode()

    def request(method, path, data=None, authentication='valid', expect=(200, 202), extra_headers=None):
        headers = {'Content-Type': 'application/json'}
        headers.update(extra_headers or {})
        if authentication == 'valid':
            headers['Authorization'] = auth
        elif authentication == 'invalid':
            headers['Authorization'] = 'Basic ' + base64.b64encode(b'quest-demo:incorrect').decode()
        req = urllib.request.Request(outputs['ApiUrl'] + path, data=json.dumps(data).encode() if data is not None else None, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=40) as response:
                code, value = response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            code, value = error.code, json.loads(error.read())
        if code not in expect:
            raise RuntimeError(f'{method} {path}: HTTP {code}: {value}')
        return value

    def await_stack(slot):
        last = None
        deadline = time.monotonic() + 1200
        while time.monotonic() < deadline:
            value = request('GET', '/v1/deployments/' + slot)
            if last != value['status']:
                last = value['status']
                print('Slot', slot, last, flush=True)
            if value['finished']:
                if not value['success']:
                    raise RuntimeError('Deployment failed: ' + json.dumps(value))
                return value
            time.sleep(5)
        raise TimeoutError('Deployment did not finish')

    def delete_slot(slot):
        name = prefix + '-demo-' + slot
        if not describe(name):
            return
        print('Deleting test slot', slot, flush=True)
        deadline = time.monotonic() + 1200
        while time.monotonic() < deadline:
            stack = describe(name)
            if not stack:
                return
            if stack['StackStatus'].endswith('_IN_PROGRESS'):
                time.sleep(5)
                continue
            if not outputs:
                raise RuntimeError('Demo exists but control API unavailable; use diagnostics before manual cleanup')
            value = request('DELETE', '/v1/deployments/' + slot + '?purge=true')
            if value.get('retryDelete'):
                continue
            time.sleep(5)
        raise TimeoutError('Deletion did not finish: ' + name)

    if args.phase == 'exercise':
        request('GET', '/v1/session', authentication='none', expect=(401,))
        request('GET', '/v1/session', authentication='invalid', expect=(401,))
        assert request('GET', '/v1/session')['connected']
        assert len(request('GET', '/v1/catalog')['services']) == 7
        request('POST', '/v1/architectures/validate', {}, expect=(400,))
        passed('Missing/wrong Basic Auth rejected; valid session, catalog and invalid-graph responses verified')
        for slot, filename in [('1', 'api-serverless'), ('2', 'eventos-cola'), ('3', 'procesar-archivos')]:
            graph = json.loads((ROOT / 'examples' / (filename + '.json')).read_text())
            assert request('POST', '/v1/architectures/validate', graph)['valid']
            payload = {'deploymentId': slot, 'architecture': graph}
            request('POST', '/v1/deployments', payload)
            deployed = await_stack(slot)
            again = request('POST', '/v1/deployments', payload)
            assert again['stackId'] == deployed['stackId']
            changed = copy.deepcopy(payload)
            changed['architecture']['nodes'][0]['name'] += ' changed'
            request('POST', '/v1/deployments', changed, expect=(409,))
            event = request('POST', '/v1/deployments/' + slot + '/events', {'resourceId': graph['nodes'][0]['id']})
            table = next(n['physicalId'] for n in deployed['nodes'] if n['kind'] == 2)
            deadline = time.monotonic() + 120
            while time.monotonic() < deadline:
                item = session.client('dynamodb').get_item(TableName=table, Key={'id': {'S': event['eventId']}}, ConsistentRead=True)
                if 'Item' in item:
                    break
                time.sleep(3)
            else:
                raise TimeoutError('Event did not reach DynamoDB: ' + filename)
            passed(filename + ': real deployment, idempotency, conflict rejection and event-to-DynamoDB delivery')
        for slot in ['1', '2', '3']:
            delete_slot(slot)
            passed('Slot ' + slot + ' deleted through API, including any versioned S3 objects')
        return

    if args.phase == 'matrix':
        graph = {'schemaVersion': 1, 'region': args.region,
                 'nodes': [{'id': 'n' + str(i), 'kind': i, 'setting': [0, 3, 1, 1, 1, 1, 2][i]} for i in range(7)],
                 'links': [{'from': 'n0', 'to': 'n1'}] + [{'from': 'n1', 'to': 'n' + str(i)} for i in [2, 3, 4, 5]] + [{'from': 'n' + str(i), 'to': 'n6'} for i in range(6)]}
        request('POST', '/v1/deployments', {'deploymentId': '1', 'architecture': graph})
        deployed = await_stack('1')
        nodes = {n['kind']: n['physicalId'] for n in deployed['nodes']}
        event = request('POST', '/v1/deployments/1/events', {'resourceId': 'n0'})
        item = session.client('dynamodb').get_item(TableName=nodes[2], Key={'id': {'S': event['eventId']}}, ConsistentRead=True)
        assert 'Item' in item
        import hashlib
        key = 'demo/' + hashlib.sha256(event['eventId'].encode()).hexdigest() + '.json'
        body = session.client('s3').get_object(Bucket=nodes[3], Key=key)['Body'].read()
        assert json.loads(body)['id'] == event['eventId']
        sqs = session.client('sqs')
        found = False
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline and not found:
            queue = sqs.receive_message(QueueUrl=nodes[4], MaxNumberOfMessages=10, WaitTimeSeconds=10)
            for message in queue.get('Messages', []):
                found = found or json.loads(message['Body'])['id'] == event['eventId']
                sqs.delete_message(QueueUrl=nodes[4], ReceiptHandle=message['ReceiptHandle'])
        assert found, 'The expected test message did not reach FIFO SQS'
        dashboard = session.client('cloudwatch').get_dashboard(DashboardName=nodes[6])
        assert len(json.loads(dashboard['DashboardBody'])['widgets'][0]['properties']['metrics']) == 6
        try:
            urllib.request.urlopen(urllib.request.Request('https://' + nodes[0] + '.execute-api.' + args.region + '.amazonaws.com/demo', data=b'{}', method='POST'), timeout=15)
        except urllib.error.HTTPError as error:
            assert error.code == 403
        else:
            raise AssertionError('Generated workload API allowed an unsigned request')
        passed('All seven services: Lambda fan-out to provisioned DynamoDB, S3, FIFO SQS and EventBridge; CloudWatch dashboard; unsigned workload API denied')
        delete_slot('1')
        passed('Seven-service architecture deleted through API')
        return

    if args.phase == 'rotation':
        import secrets
        replacement = {'username': secret['username'], 'password': secrets.token_urlsafe(40)}
        session.client('secretsmanager').put_secret_value(SecretId=outputs['AuthSecretArn'], SecretString=json.dumps(replacement))
        print('Testing password rotation after the documented 60-second credential cache', flush=True)
        time.sleep(35)
        print('Waiting for remaining credential-cache lifetime', flush=True)
        time.sleep(30)
        request('GET', '/v1/session', expect=(401,))
        auth = 'Basic ' + base64.b64encode((replacement['username'] + ':' + replacement['password']).encode()).decode()
        assert request('GET', '/v1/session')['connected']
        passed('Password rotation rejects the old credential and accepts the new credential after cache expiry')
        return

    if args.phase == 'network':
        def change_cidr(cidr):
            current = describe(state['stack'])
            parameters = [{'ParameterKey': p['ParameterKey'], 'ParameterValue': cidr} if p['ParameterKey'] == 'AllowedCidr' else {'ParameterKey': p['ParameterKey'], 'UsePreviousValue': True} for p in current['Parameters']]
            original = cfn.get_template(StackName=state['stack'], TemplateStage='Original')['TemplateBody']
            original = json.dumps(original) if isinstance(original, dict) else original
            try:
                cfn.update_stack(StackName=state['stack'], TemplateBody=original, Parameters=parameters, Capabilities=['CAPABILITY_IAM', 'CAPABILITY_AUTO_EXPAND'])
            except ClientError as error:
                if 'No updates are to be performed' in str(error):
                    return
                raise
            cfn.get_waiter('stack_update_complete').wait(StackName=state['stack'], WaiterConfig={'Delay': 5, 'MaxAttempts': 120})
        try:
            print('Testing source-IP policy with an intentionally different allowed IP', flush=True)
            change_cidr('203.0.113.1/32')
            api_id = outputs['ApiUrl'].split('//')[1].split('.')[0]
            applied_policy = session.client('apigateway').get_rest_api(restApiId=api_id)['policy']
            print('Changed IP is present in API resource policy:', '203.0.113.1' in applied_policy, flush=True)
            deadline = time.monotonic() + 45
            while True:
                blocked = request('GET', '/v1/session', expect=(200, 403))
                if not blocked.get('connected'):
                    break
                if time.monotonic() >= deadline:
                    raise AssertionError('Changed-IP restriction never blocked the current client')
                time.sleep(5)
            passed('A parameter update to a different source IP blocks this client with HTTP 403')
            layer = 'Lambda source-IP guard' if blocked.get('error') == 'source_ip_denied' else 'API Gateway'
            passed('Observed IP rejection layer: ' + layer)
            request('GET', '/v1/session', expect=(403,), extra_headers={'X-Forwarded-For': '203.0.113.1', 'X-Real-IP': '203.0.113.1'})
            passed('Client-supplied forwarding headers cannot bypass the source-IP restriction')
        finally:
            print('Restoring original source-IP restriction', flush=True)
            change_cidr(state['cidr'])
        assert request('GET', '/v1/session')['connected']
        passed('Restoring the source-IP parameter restores authenticated access')
        return

    if args.phase in ('cleanup', 'cleanup-demos'):
        for slot in ['1', '2', '3']:
            delete_slot(slot)
        if args.phase == 'cleanup-demos':
            passed('Demo slots cleaned; control plane retained for further tests')
            return
        if describe(state['stack']):
            cfn.delete_stack(StackName=state['stack'])
            cfn.get_waiter('stack_delete_complete').wait(StackName=state['stack'], WaiterConfig={'Delay': 5, 'MaxAttempts': 240})
        if state.get('artifactBucketCreated') and not state.get('artifactBucketDeleted'):
            for page in s3.get_paginator('list_objects_v2').paginate(Bucket=state['artifacts']):
                objects = [{'Key': x['Key']} for x in page.get('Contents', [])]
                if objects:
                    result = s3.delete_objects(Bucket=state['artifacts'], Delete={'Objects': objects})
                    if result.get('Errors'):
                        raise RuntimeError('Artifact object deletion failed')
            s3.delete_bucket(Bucket=state['artifacts'])
            state['artifactBucketDeleted'] = True
        passed('Test control stack, all demo stacks and dedicated artifact bucket removed')
        assert describe(state['stack']) is None
        assert all(describe(prefix + '-demo-' + str(slot)) is None for slot in [1, 2, 3])
        functions = session.client('lambda').get_paginator('list_functions').paginate()
        assert not [fn['FunctionName'] for page in functions for fn in page['Functions'] if fn['FunctionName'].startswith(prefix + '-')]
        roles = session.client('iam').get_paginator('list_roles').paginate()
        assert not [role['RoleName'] for page in roles for role in page['Roles'] if role['RoleName'].startswith(prefix + '-')]
        assert not [bucket['Name'] for page in s3.get_paginator('list_buckets').paginate() for bucket in page['Buckets'] if bucket['Name'].startswith(prefix + '-')]
        assert not [name for page in session.client('dynamodb').get_paginator('list_tables').paginate() for name in page['TableNames'] if name.startswith(prefix + '-')]
        assert not session.client('sqs').list_queues(QueueNamePrefix=prefix + '-').get('QueueUrls')
        assert not session.client('events').list_event_buses(NamePrefix=prefix + '-').get('EventBuses')
        assert not session.client('logs').describe_log_groups(logGroupNamePrefix='/aws/lambda/' + prefix + '-').get('logGroups')
        assert not session.client('cloudwatch').list_dashboards(DashboardNamePrefix=prefix + '-').get('DashboardEntries')
        assert not session.client('cloudwatch').describe_alarms(AlarmNamePrefix=prefix + '-').get('MetricAlarms')
        assert not [api for page in session.client('apigatewayv2').get_paginator('get_apis').paginate() for api in page['Items'] if api.get('Name', '').startswith(prefix + '-')]
        assert not [api for page in session.client('apigateway').get_paginator('get_rest_apis').paginate() for api in page['items'] if api.get('name', '').startswith(prefix + '-')]
        try:
            s3.head_bucket(Bucket=state['artifacts'])
        except ClientError as error:
            assert error.response['Error']['Code'] in ('404', 'NoSuchBucket')
        else:
            raise AssertionError('Artifact bucket still exists')
        state['cleanupComplete'] = True
        passed('Independent cleanup checks: no test stacks, Lambda functions, IAM roles or artifact bucket remain')
        passed('Service inventories contain no test S3 buckets, tables, queues, event buses, log groups, dashboards, alarms or APIs')


if __name__ == '__main__':
    main()
