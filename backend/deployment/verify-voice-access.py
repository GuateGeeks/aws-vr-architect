"""Verify release sources, preserved parameters and the broker; do not write workload data."""
import json
import os
import runpy
import time
import urllib.request
from pathlib import Path

os.environ['RELEASE_VERIFY_REPORT'] = 'deployment/release-0.16.0-verification.json'
verified = runpy.run_path('deployment/verify-release-readonly.py')
control = verified['control']
previous = json.loads(Path('deployment/parameters-0.15.0.json').read_text())
parameters = lambda values: {v['ParameterKey']: v['ParameterValue'] for v in values}
assert parameters(previous) == parameters(control['Parameters']), 'Existing stack parameters changed'
print('PASS all existing stack parameters preserved', flush=True)

request = urllib.request.Request(verified['outputs']['ApiUrl'].rstrip('/') + '/v1/assistant/session',
    data=b'{}', method='POST', headers={'Authorization': verified['authorization'], 'Content-Type': 'application/json'})
with verified['opener'].open(request, timeout=45) as response:
    ticket = json.loads(response.read())
assert ticket['clientSecret'].startswith('ek_')
assert time.time() < ticket['expiresAt'] <= time.time() + 35
assert ticket['maxSessionSeconds'] == 3300
print('PASS authenticated voice broker; model=' + ticket['model'], flush=True)
report_path = Path(os.environ['RELEASE_VERIFY_REPORT'])
report = json.loads(report_path.read_text())
report.update(parametersPreserved=True, voiceBrokerReady=True, voiceModel=ticket['model'],
              newTools=['read_component', 'slot_action', 'send_event'])
ticket.clear()
report_path.write_text(json.dumps(report, indent=2))
