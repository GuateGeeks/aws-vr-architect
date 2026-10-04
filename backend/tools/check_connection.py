"""Check the deployed VR API without creating architecture resources or exposing credentials."""
import argparse
import base64
import json
from pathlib import Path
import urllib.error
import urllib.request
from urllib.parse import urlsplit
import boto3


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', required=True)
    parser.add_argument('--region', default='us-east-1')
    parser.add_argument('--stack', default='guategeeks-aws2026')
    args = parser.parse_args()
    session = boto3.Session(profile_name=args.profile, region_name=args.region)
    stack = session.client('cloudformation').describe_stacks(StackName=args.stack)['Stacks'][0]
    values = {o['OutputKey']: o['OutputValue'] for o in stack['Outputs']}
    endpoint = values['ApiUrl'].rstrip('/')
    url = urlsplit(endpoint)
    if url.scheme != 'https' or url.username or url.password or url.query or url.fragment:
        raise ValueError('Invalid API URL in stack outputs')
    credentials = json.loads(session.client('secretsmanager').get_secret_value(SecretId=values['AuthSecretArn'])['SecretString'])
    if len(credentials['password']) != 6:
        raise RuntimeError('The deployed demo password must contain exactly six characters')
    print('PASS six-character service password (value not logged)')
    auth = 'Basic ' + base64.b64encode((credentials['username'] + ':' + credentials['password']).encode()).decode()
    opener = urllib.request.build_opener(NoRedirect())
    def request(method, path, data=None, authenticated=True, expected=200):
        headers = {'Content-Type': 'application/json'}
        if authenticated:
            headers['Authorization'] = auth
        req = urllib.request.Request(endpoint + path, data=json.dumps(data).encode() if data is not None else None, headers=headers, method=method)
        try:
            with opener.open(req, timeout=40) as response:
                code, result = response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            code = error.code
            result = json.loads(error.read())
        if code not in (expected if isinstance(expected, tuple) else (expected,)):
            raise RuntimeError(f'{method} {path}: HTTP {code}; expected {expected}')
        return result
    connected = request('GET', '/v1/session')
    if not connected.get('connected') or connected.get('region') != args.region or connected.get('schemaVersion') != 1:
        raise RuntimeError('Incompatible session contract')
    print('PASS authenticated session:', args.region)
    request('GET', '/v1/session', authenticated=False, expected=401)
    print('PASS unauthenticated access rejected')
    catalog = request('GET', '/v1/catalog')
    if catalog.get('deploymentSlots') != ['1', '2', '3'] or catalog.get('maxNodes') != 12:
        raise RuntimeError('Incompatible catalog contract')
    print('PASS catalog and shared slots')
    for slot in catalog['deploymentSlots']:
        state = request('GET', '/v1/deployments/' + slot, expected=(200, 404))
        print('SLOT', slot + ':', 'free' if state.get('error') == 'not_found' else state.get('status', 'unknown'))
    for path in sorted((Path(__file__).resolve().parents[1] / 'examples').glob('*.json')):
        graph = json.loads(path.read_text(encoding='utf-8')); graph['region'] = args.region
        result = request('POST', '/v1/architectures/validate', graph)
        if not result.get('valid') or len(result.get('graphHash', '')) != 64:
            raise RuntimeError('Invalid validation result')
        print('PASS server validation:', path.stem)
    print('READY:', endpoint)
    print('No deployment slots were created, changed or deleted.')


if __name__ == '__main__':
    main()
