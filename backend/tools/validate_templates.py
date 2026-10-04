"""Offline CloudFormation schema/IAM checks for the control plane and compiled graphs."""
import json
import os
from pathlib import Path
import sys
import cfnlint

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
from compiler import compile_graph
from graph import validate


def main():
    templates = [('control', json.loads((ROOT / 'template.json').read_text()))]
    graphs = [(p.stem, json.loads(p.read_text())) for p in (ROOT / 'examples').glob('*.json')]
    for name, kinds, settings in [('s3-sqs-fifo-lambda', [5, 4, 1, 2], [1, 1, 3, 1]), ('s3-sqs-standard', [3, 4, 1, 2], [1, 0, 0, 0]), ('event-lambda-s3', [5, 1, 3], [0, 0, 0]), ('lambda-event-queue', [1, 5, 4], [0, 0, 0]), ('lambda-s3', [1, 3], [0, 1])]:
        graphs.append((name, {'schemaVersion': 1, 'region': 'us-east-1', 'nodes': [{'id': f'n{i}', 'kind': kind, 'setting': settings[i]} for i, kind in enumerate(kinds)], 'links': [{'from': f'n{i}', 'to': f'n{i+1}'} for i in range(len(kinds)-1)]}))
    # All seven service families reporting into CloudWatch, including Fn::Sub API IDs.
    graphs.append(('observability', {'schemaVersion': 1, 'region': 'us-east-1', 'nodes': [{'id': f'n{i}', 'kind': i} for i in range(7)], 'links': [{'from': 'n0', 'to': 'n1'}] + [{'from': f'n{i}', 'to': 'n6'} for i in range(6)]}))
    graphs.append(('max-lambdas', {'schemaVersion': 1, 'region': 'us-east-1', 'nodes': [{'id': f'n{i}', 'kind': 1 if i < 11 else 6} for i in range(12)], 'links': [{'from': f'n{i}', 'to': 'n11'} for i in range(11)]}))
    for name, graph in graphs:
        templates.append((name, compile_graph(validate(graph, 'us-east-1'), 'ggawsday-demo-1', '123456789012', 'arn:aws:iam::123456789012:role/workload')))
    failures = []
    for name, template in templates:
        text = json.dumps(template, separators=(',', ':'))
        if name != 'control' and len(text.encode()) > 51200:
            failures.append(name + ': template exceeds 51200 bytes')
        errors = cfnlint.lint(text, regions=['us-east-1'])
        for error in errors:
            failures.append(f'{name}: {error.rule.id} {error.message} ({error.path})')
        print(f'{name}: {len(template["Resources"])} resources, {len(text.encode())} bytes, {len(errors)} findings')
    if failures:
        raise SystemExit('\n'.join(failures))


if __name__ == '__main__':
    main()
