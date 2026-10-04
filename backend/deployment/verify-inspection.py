"""Live acceptance test: reserve empty slot 3, exercise reads, then delete only its exact stack."""
import base64
import json
import time
import uuid
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
import boto3

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
control = session.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
assert control['StackStatus'] == 'UPDATE_COMPLETE'
outputs = {o['OutputKey']: o['OutputValue'] for o in control['Outputs']}
secret = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
auth = 'Basic ' + base64.b64encode((secret['username'] + ':' + secret['password']).encode()).decode()

def request(method, path, body=None):
    req = urllib.request.Request(outputs['ApiUrl'] + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={'Authorization': auth, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=40) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read())

path = '/v1/deployments/3'
assert request('GET', path)[0] == 404, 'Slot 3 is occupied; no changes made.'
graph = json.loads(Path('examples/api-serverless.json').read_text())
graph['nodes'] = [n for n in graph['nodes'] if n['kind'] in (1, 2)]
ids = {n['id'] for n in graph['nodes']}
graph['links'] = [l for l in graph['links'] if l['from'] in ids and l['to'] in ids]
for node in graph['nodes']:
    node['name'] = 'Inspection validation ' + uuid.uuid4().hex[:10]
code, result = request('POST', '/v1/deployments', {'deploymentId': '3', 'architecture': graph})
assert code == 202, (code, result)
identity = result['stackId']
Path('deployment/inspection-test-stack.json').write_text(json.dumps({'slot': '3', 'stackId': identity}))
report = {'stackId': identity, 'slot': '3', 'checks': [], 'cleaned': False}
print('Temporary validation stack created in empty slot 3.', flush=True)
try:
    for _ in range(160):
        code, state = request('GET', path)
        assert code == 200 and state['stackId'] == identity
        if state['finished']:
            assert state['success'], state['status']
            break
        time.sleep(3)
    else:
        raise TimeoutError('Create still in progress')
    print('Validation architecture ready; inspecting real data.', flush=True)
    fn = next(n['id'] for n in graph['nodes'] if n['kind'] == 1)
    table = next(n['id'] for n in graph['nodes'] if n['kind'] == 2)
    code, event = request('POST', path + '/events', {'resourceId': fn})
    assert code == 202
    for mode, node in [('items', table), ('logs', fn)]:
        query = urllib.parse.urlencode({'stackId': identity, 'resourceId': node})
        for _ in range(24):
            code, page = request('GET', path + '/' + mode + '?' + query)
            assert code == 200, (code, page)
            if any(event['eventId'] in e['text'] for e in page['entries']):
                break
            time.sleep(5)
        else:
            raise AssertionError('Event was not visible in ' + mode)
        report['checks'].append({'kind': mode, 'httpStatus': code, 'entries': len(page['entries']), 'eventFound': True})
        print(mode + ': real event verified through deployed API.', flush=True)
    code, _ = request('DELETE', path + '?purge=true&stackId=wrong-stack')
    assert code == 409
    report['checks'].append({'kind': 'replacement-protection', 'httpStatus': code})
finally:
    code, current = request('GET', path)
    if code == 200 and current['stackId'] == identity:
        # Wait for any initial creation/rollback to finish before cleanup.
        for _ in range(180):
            if not current['status'].endswith('_IN_PROGRESS'):
                break
            time.sleep(3); code, current = request('GET', path)
            assert code == 200 and current['stackId'] == identity
        code, deleted = request('DELETE', path + '?' + urllib.parse.urlencode({'purge': 'true', 'stackId': identity}))
        assert code in (200, 202), (code, deleted)
        for _ in range(180):
            code, current = request('GET', path)
            if code == 404:
                report['cleaned'] = True; break
            assert code == 200 and current['stackId'] == identity
            time.sleep(3)
    report['finishedUtc'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
    Path('deployment/inspection-acceptance.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)
    assert report['cleaned'], 'Inspect validation stack cleanup.'
