"""Verify deployment and expected-output assertions without modifying workload slots."""
import json
import os
import runpy
import urllib.request
from pathlib import Path

os.environ['RELEASE_VERIFY_REPORT'] = 'deployment/release-0.15.0-verification.json'
verified = runpy.run_path('deployment/verify-release-readonly.py')
opener, authorization, outputs = (verified[k] for k in ('opener', 'authorization', 'outputs'))
source = 'def handler(event, context):\n    return {"total": event.get("amount", 0) * 2}\n'
checks = []
for expected, passed in [('{"total":42}', True), ('{"total":43}', False)]:
    request = urllib.request.Request(outputs['ApiUrl'].rstrip('/') + '/v1/code/test', method='POST',
        headers={'Authorization': authorization, 'Content-Type': 'application/json'},
        data=json.dumps({'source': source, 'eventJson': '{"amount":21}', 'expectedOutput': expected}).encode())
    with opener.open(request, timeout=45) as response:
        result = json.loads(response.read())
    assert result['valid'] and result['expectedChecked'] and result['passed'] is passed
    checks.append({'expectedOutput': expected, 'passed': result['passed'], 'expectedChecked': True})
    print('PASS isolated assertion ' + ('match' if passed else 'mismatch rejection'), flush=True)
Path('deployment/expected-output-0.15.0-verification.json').write_text(json.dumps(checks, indent=2))
print('No workload slot was created, modified, invoked or deleted.', flush=True)
