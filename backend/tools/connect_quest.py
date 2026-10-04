"""Read the deployed API configuration and pass a one-time session to Quest over USB.

Read-only AWS operations. Never deploys a stack or sends a workload event.
No password in arguments, console output, local files, APK, or PlayerPrefs.
The USB bootstrap is briefly plaintext on the development headset, consumed/deleted
by Unity and removed by this tool after at most 60 seconds if not consumed.
"""
import argparse
import json
from pathlib import Path
import subprocess
import time
from urllib.parse import urlsplit
import boto3

SESSION_PATH = '/sdcard/Android/data/com.guategeeks.awsarchitectlab/files/cloud-session.json'


def connection(values, credentials, slot, now):
    url = values['ApiUrl'].rstrip('/')
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError('The stack output must be an HTTPS API URL without credentials or query parameters.')
    username, password = credentials['username'], credentials['password']
    if not username or ':' in username or not password or len((username + ':' + password).encode()) > 1500:
        raise ValueError('Invalid service credentials.')
    if slot not in ('1', '2', '3'):
        raise ValueError('Invalid deployment slot.')
    return {'endpoint': url, 'username': username, 'password': password, 'deploymentId': slot, 'expiresAt': int(now) + 60}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', required=True)
    parser.add_argument('--region', default='us-east-1')
    parser.add_argument('--stack', default='guategeeks-aws2026')
    parser.add_argument('--slot', choices=['1', '2', '3'], default='1')
    parser.add_argument('--serial', required=True)
    parser.add_argument('--adb', default='adb')
    args = parser.parse_args()
    def adb(*command, data=None, check=True):
        return subprocess.run([args.adb, '-s', args.serial, *command], input=data, capture_output=True, check=check, timeout=15)

    if adb('shell', 'getprop', 'ro.product.model').stdout.strip() != b'Quest 3':
        raise RuntimeError('Connect and authorize the selected Meta Quest 3.')
    adb('shell', 'test -d ' + str(Path(SESSION_PATH).parent).replace('\\', '/'))
    session = boto3.Session(profile_name=args.profile, region_name=args.region)
    outputs = session.client('cloudformation').describe_stacks(StackName=args.stack)['Stacks'][0]['Outputs']
    values = {o['OutputKey']: o['OutputValue'] for o in outputs}
    credentials = json.loads(session.client('secretsmanager').get_secret_value(SecretId=values['AuthSecretArn'])['SecretString'])
    settings = connection(values, credentials, args.slot, time.time())
    try:
        # Atomic rename prevents Unity from seeing partial JSON. Command contains no secret.
        adb('shell', f'umask 077; cat > {SESSION_PATH}.pending && mv {SESSION_PATH}.pending {SESSION_PATH}',
            data=json.dumps(settings).encode())
        settings.clear(); credentials.clear()
        print('In Quest, select CONEXIÓN AWS > Conectar AWS (USB) within 60 seconds.')
        print('Only session/catalog requests are sent. Confirm resource creation separately in VR.')
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            result = adb('shell', 'test -f ' + SESSION_PATH, check=False)
            if result.returncode == 1:
                print('Unity consumed the bootstrap. Check the headset for connection success or an API error.')
                return
            if result.returncode != 0:
                raise RuntimeError('USB connection was interrupted.')
            time.sleep(1)
        raise TimeoutError('Bootstrap expired. Run the command again when ready in the headset.')
    finally:
        settings.clear(); credentials.clear()
        result = adb('shell', f'rm -f {SESSION_PATH} {SESSION_PATH}.pending', check=False)
        if result.returncode != 0:
            print('USB cleanup could not be confirmed. Reconnect USB and rerun this tool; the expired bootstrap cannot connect.')


if __name__ == '__main__':
    try:
        main()
    except (Exception, KeyboardInterrupt) as error:
        # Avoid printing credential-bearing payloads from arbitrary SDK/process exceptions.
        print('Session setup stopped (' + type(error).__name__ + '). Verify login, stack, USB authorization and the running app; no resources were created.')
        raise SystemExit(1) from None
