"""Read-only AWS verification and one ephemeral ticket for an explicit Unity live test.
Never prints credentials. The editor consumes and deletes the 30-second ticket.
"""
import base64
import io
import json
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
import boto3

validation = Path('../GuateGeeksAWSVR/Validation')
deadline = time.time() + 180
while not (validation / 'assistant-smoke-ready.txt').exists():
    if time.time() > deadline:
        raise SystemExit('Unity live test did not become ready.')
    time.sleep(1)
session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
cfn = session.client('cloudformation')
stack = cfn.describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
assert stack['StackStatus'] == 'UPDATE_COMPLETE'
outputs = {item['OutputKey']: item['OutputValue'] for item in stack['Outputs']}
function = cfn.describe_stack_resource(StackName=stack['StackId'], LogicalResourceId='ControlFunction')['StackResourceDetail']['PhysicalResourceId']
deployed = session.client('lambda').get_function(FunctionName=function)
with urllib.request.urlopen(deployed['Code']['Location'], timeout=30) as response:
    with zipfile.ZipFile(io.BytesIO(response.read())) as package:
        for name in ('app.py', 'assistant.py', 'authoring.py'):
            assert package.read(name) == Path('src', name).read_bytes(), 'Deployed source mismatch'
secret = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
auth = base64.b64encode((secret['username'] + ':' + secret['password']).encode()).decode()
request = urllib.request.Request(outputs['ApiUrl'] + '/v1/assistant/session', data=b'{}', method='POST',
    headers={'Authorization': 'Basic ' + auth, 'Content-Type': 'application/json'})
try:
    with urllib.request.urlopen(request, timeout=40) as response:
        ticket = json.loads(response.read())
except urllib.error.HTTPError as error:
    # Backend errors are sanitized, but do not print any body in a credential test.
    raise SystemExit('Broker returned HTTP ' + str(error.code)) from None
assert ticket['clientSecret'].startswith('ek_')
assert time.time() < ticket['expiresAt'] <= time.time() + 35
assert ticket['maxSessionSeconds'] == 3300
pending = validation / 'assistant-smoke-ticket.pending'
pending.write_text(json.dumps(ticket))
pending.replace(validation / 'assistant-smoke-ticket.json')
print('PASS deployed broker source; authenticated ephemeral session created; model=' + ticket['model'], flush=True)
