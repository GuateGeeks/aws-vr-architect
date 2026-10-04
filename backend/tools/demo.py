"""Exercise the deployed API without putting its Basic password in shell history."""
import argparse
import base64
import json
from pathlib import Path
import time
import urllib.error
import urllib.request
import boto3


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['session', 'smoke', 'delete'])
    parser.add_argument('--profile', required=True)
    parser.add_argument('--region', default='us-east-1')
    parser.add_argument('--stack', default='guategeeks-aws2026')
    parser.add_argument('--slot', choices=['1', '2', '3'], default='1')
    parser.add_argument('--architecture', type=Path, default=Path(__file__).resolve().parents[1] / 'examples' / 'api-serverless.json')
    args = parser.parse_args()
    session = boto3.Session(profile_name=args.profile, region_name=args.region)
    outputs = session.client('cloudformation').describe_stacks(StackName=args.stack)['Stacks'][0]['Outputs']
    values = {o['OutputKey']: o['OutputValue'] for o in outputs}
    credentials = json.loads(session.client('secretsmanager').get_secret_value(SecretId=values['AuthSecretArn'])['SecretString'])
    authorization = 'Basic ' + base64.b64encode((credentials['username'] + ':' + credentials['password']).encode()).decode()
    def request(method, path, data=None):
        req = urllib.request.Request(values['ApiUrl'] + path, data=json.dumps(data).encode() if data is not None else None,
            headers={'Authorization': authorization, 'Content-Type': 'application/json'}, method=method)
        try:
            with urllib.request.urlopen(req, timeout=40) as result:
                payload = json.loads(result.read())
                print(json.dumps(payload, ensure_ascii=False))
                return payload
        except urllib.error.HTTPError as error:
            if error.code == 404 and method == 'GET' and path.startswith('/v1/deployments/'):
                return {'status': 'DELETE_COMPLETE', 'finished': True}
            raise RuntimeError(f'HTTP {error.code}: {error.read().decode()}') from None
    request('GET', '/v1/session')
    path = '/v1/deployments/' + args.slot
    if args.action == 'session':
        return
    if args.action == 'delete':
        result = request('DELETE', path + '?purge=true')
        deadline = time.monotonic() + 1200
        while not result.get('finished') and time.monotonic() < deadline:
            time.sleep(3)
            result = request('DELETE', path + '?purge=true') if result.get('retryDelete') else request('GET', path)
        if result.get('status') != 'DELETE_COMPLETE':
            raise RuntimeError('El borrado no terminó correctamente; revisa CloudFormation.')
        return
    graph = json.loads(args.architecture.read_text(encoding='utf-8'))
    graph['region'] = args.region
    request('POST', '/v1/architectures/validate', graph)
    request('POST', '/v1/deployments', {'deploymentId': args.slot, 'architecture': graph})
    deadline = time.monotonic() + 1200
    while time.monotonic() < deadline:
        result = request('GET', path)
        if result.get('finished'):
            if not result.get('success'):
                raise RuntimeError('El despliegue terminó con error.')
            # Presets use their first node as entry; custom files must follow this convention.
            request('POST', path + '/events', {'resourceId': graph['nodes'][0]['id']})
            return
        time.sleep(3)
    raise TimeoutError('Consulta CloudFormation: venció la espera local; la operación AWS continúa.')


if __name__ == '__main__':
    main()
