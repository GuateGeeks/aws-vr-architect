import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'examples'
root.mkdir(exist_ok=True)
for name, kinds in [('api-serverless', [0, 1, 2]), ('eventos-cola', [5, 4, 1, 2]), ('procesar-archivos', [3, 1, 2])]:
    graph = {'schemaVersion': 1, 'region': 'us-east-1', 'nodes': [{'id': 'node' + str(i), 'kind': kind, 'name': ['API Gateway', 'Lambda', 'DynamoDB', 'S3', 'SQS', 'EventBridge', 'CloudWatch'][kind], 'setting': 0, 'position': {'x': i, 'y': 1.52, 'z': 2.65}, 'state': 0} for i, kind in enumerate(kinds)], 'links': [{'from': 'node' + str(i), 'to': 'node' + str(i + 1)} for i in range(len(kinds)-1)]}
    (root / (name + '.json')).write_text(json.dumps(graph, indent=2) + '\n', encoding='utf-8')
