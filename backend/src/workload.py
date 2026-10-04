"""Fixed demo workload embedded by the compiler; no uploaded user code."""
import hashlib
import json
import os
from urllib.parse import unquote_plus
import boto3


def payloads(record):
    if record.get('eventSource') == 'aws:sqs':
        yield from payloads(json.loads(record['body']))
    elif record.get('eventSource') == 'aws:s3':
        obj = record['s3']
        response = boto3.client('s3').get_object(Bucket=obj['bucket']['name'], Key=unquote_plus(obj['object']['key']))
        # Demo accepts small JSON objects; avoid loading arbitrary files into memory.
        with response['Body'] as stream:
            raw = stream.read(65537)
        if len(raw) > 65536:
            raise ValueError('Demo object exceeds 64 KiB')
        yield json.loads(raw)
    elif 'Records' in record:
        for child in record['Records']:
            yield from payloads(child)
    elif record.get('Event') == 's3:TestEvent':
        return
    elif isinstance(record.get('detail'), dict):
        yield record['detail']
    elif record.get('body'):
        yield json.loads(record['body'])
    else:
        yield record


def handler(event, context):
    # Event identity propagates through the graph so retries overwrite the same item/object.
    count = 0
    for payload in payloads(event):
        count += 1
        event_id = str(payload.get('id') or hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest())
        def record(stage, level='INFO', **fields):
            print(json.dumps(dict(eventId=event_id, nodeId=os.environ['NODE_ID'], stage=stage, level=level,
                                  requestId=getattr(context, 'aws_request_id', ''), **fields)))
        record('received')
        body = json.dumps({'id': event_id, 'message': 'AWS Day · GuateGeeks', 'source': os.environ['NODE_ID']})
        for target in json.loads(os.environ.get('TARGETS', '[]')):
            kind, name = target['kind'], target['name']
            try:
                if kind == 2:
                    boto3.client('dynamodb').put_item(TableName=name, Item={'id': {'S': event_id}, 'message': {'S': body}})
                elif kind == 3:
                    boto3.client('s3').put_object(Bucket=name, Key='demo/' + hashlib.sha256(event_id.encode()).hexdigest() + '.json', Body=body, ContentType='application/json')
                elif kind == 4:
                    sqs = boto3.client('sqs')
                    url = sqs.get_queue_url(QueueName=name)['QueueUrl']
                    args = {'QueueUrl': url, 'MessageBody': body}
                    if name.endswith('.fifo'):
                        args['MessageGroupId'] = 'aws-day'
                    sqs.send_message(**args)
                elif kind == 5:
                    result = boto3.client('events').put_events(Entries=[{'EventBusName': name, 'Source': 'guategeeks.demo', 'DetailType': 'DemoEvent', 'Detail': body}])
                    if result['FailedEntryCount']:
                        raise RuntimeError('EventBridge rejected the event')
            except Exception as error:
                record('failed', level='ERROR', targetNodeId=target.get('nodeId', ''), errorType=type(error).__name__)
                raise
            if target.get('nodeId'):
                record('delivered', targetNodeId=target['nodeId'], targetKind=kind)
        record('processed', processed=True)
    return {'statusCode': 200, 'body': json.dumps({'processed': count})}
