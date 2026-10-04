"""Read a key from stdin, validate model access, then store only in Secrets Manager."""
import json
import sys
import urllib.request
import boto3
from botocore.exceptions import ClientError

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
if session.client('sts').get_caller_identity()['Account'] != '590183968738':
    raise SystemExit('Unexpected AWS account.')
key = sys.stdin.read().strip()
if not key.startswith('sk-') or len(key) > 1024:
    raise SystemExit('Invalid key input.')
request = urllib.request.Request('https://api.openai.com/v1/models', headers={'Authorization': 'Bearer ' + key})
try:
    with urllib.request.urlopen(request, timeout=20) as response:
        models = {entry['id'] for entry in json.loads(response.read())['data']}
except Exception:
    raise SystemExit('OpenAI authentication/model lookup failed; no secret was stored.') from None
available = [name for name in ('gpt-realtime-2.1', 'gpt-realtime-2.1-mini', 'gpt-realtime', 'gpt-realtime-mini') if name in models]
if not available:
    raise SystemExit('No supported Realtime model is visible to this project; no secret was stored.')
secrets = session.client('secretsmanager')
name = 'ggawsday-openai-realtime'
value = json.dumps({'OPENAI_API_KEY': key})
try:
    result = secrets.create_secret(Name=name, SecretString=value, Description='OpenAI key for GuateGeeks VR assistant; server access only')
except ClientError as error:
    if error.response['Error']['Code'] != 'ResourceExistsException':
        raise SystemExit('Secret creation failed.') from None
    result = secrets.put_secret_value(SecretId=name, SecretString=value)
print(json.dumps({'secretArn': result['ARN'], 'availableRealtimeModels': available}))
