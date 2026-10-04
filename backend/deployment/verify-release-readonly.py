"""Read-only release verification; never creates, invokes or deletes a workload."""
import base64
import hashlib
import io
import json
from datetime import datetime, timezone
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request
import zipfile
import boto3

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
cfn = session.client('cloudformation')
control = cfn.describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
assert control['StackStatus'] == 'UPDATE_COMPLETE'
outputs = {entry['OutputKey']: entry['OutputValue'] for entry in control['Outputs']}
function = cfn.describe_stack_resource(StackName=control['StackId'], LogicalResourceId='ControlFunction')['StackResourceDetail']['PhysicalResourceId']
deployed = session.client('lambda').get_function(FunctionName=function)
with urllib.request.urlopen(deployed['Code']['Location'], timeout=45) as response:
    package = response.read()
assert base64.b64encode(hashlib.sha256(package).digest()).decode() == deployed['Configuration']['CodeSha256']
with zipfile.ZipFile(io.BytesIO(package)) as archive:
    for name in ('app.py', 'assistant.py', 'authoring.py', 'code_runner.py', 'inspection.py', 'compiler.py', 'workload.py'):
        assert archive.read(name) == Path('src', name).read_bytes(), 'Deployed source mismatch: ' + name
print('PASS deployed assistant/authoring/inspection/compiler/workload source matches release', flush=True)
secret = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
authorization = 'Basic ' + base64.b64encode((secret['username'] + ':' + secret['password']).encode()).decode()
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None
opener = urllib.request.build_opener(NoRedirect())
def get(path):
    request = urllib.request.Request(outputs['ApiUrl'].rstrip('/') + path, headers={'Authorization': authorization})
    try:
        with opener.open(request, timeout=40) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read())

report = dict(timestamp=datetime.now(timezone.utc).isoformat(), stackStatus=control['StackStatus'], deployedSourceMatches=True, slots=[], inspectionChecks=[])
assert get('/v1/session')[0] == 200
for slot in ('1', '2', '3'):
    path = '/v1/deployments/' + slot
    code, state = get(path)
    assert code in (200, 404)
    report['slots'].append(dict(slot=slot, status=state.get('status', 'ABSENT')))
    if code != 200 or state.get('status') != 'CREATE_COMPLETE':
        continue
    for node in state.get('nodes', []):
        if node.get('kind') not in (1, 2):
            continue
        mode = 'logs' if node['kind'] == 1 else 'items'
        query = dict(stackId=state['stackId'], resourceId=node['resourceId'])
        if mode == 'items':
            query['eventId'] = 'release-readonly-missing-event'
        code, page = get(path + '/' + mode + '?' + urllib.parse.urlencode(query))
        assert code == 200 and page['inspectionVersion'] == 2
        assert isinstance(page['entries'], list)
        if mode == 'logs' and page.get('resume'):
            query['resume'] = page['resume']
            code, following = get(path + '/' + mode + '?' + urllib.parse.urlencode(query))
            assert code == 200 and following['incremental']
        report['inspectionChecks'].append(dict(slot=slot, mode=mode, version=2))
Path('deployment/release-0.13.0-verification.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
print('No workload was created, invoked, changed or deleted.')
