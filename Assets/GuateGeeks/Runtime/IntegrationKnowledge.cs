using System;
using System.Linq;

namespace GuateGeeks.AwsVr
{
    // Local retrieval grounded in the compiler/workload contract, refreshed for every context.
    public static class IntegrationKnowledge
    {
        [Serializable] public sealed class Entry
        {
            public string source="backend/src/compiler.py + workload.py",fromId,toId,operation,eventJson,example,limitations;
        }
        public static Entry[] Retrieve(Architecture graph,string selectedId)
        {
            return graph.links.OrderByDescending(l=>l.from==selectedId || l.to==selectedId)
                .ThenBy(l=>l.from,StringComparer.Ordinal).ThenBy(l=>l.to,StringComparer.Ordinal).Take(8)
                .Select(l=>{
                    var from=graph.Find(l.from);var to=graph.Find(l.to);
                    var entry=new Entry{fromId=l.from,toId=l.to,operation=DesignSemantics.Operation(from.kind,to.kind),limitations="Local graph configuration; deployment requires manual review. Isolated code tests cannot access workload resources."};
                    if(to.kind==ServiceKind.CloudWatch){entry.example="Observation association only. Keep JSON logs with eventId, nodeId, stage and requestId.";entry.eventJson="{}";return entry;}
                    entry.eventJson=from.kind==ServiceKind.ApiGateway?"{\"body\":\"{\\\"id\\\":\\\"example-1\\\",\\\"amount\\\":21}\"}":from.kind==ServiceKind.SQS?"{\"Records\":[{\"eventSource\":\"aws:sqs\",\"body\":\"{\\\"id\\\":\\\"example-1\\\",\\\"amount\\\":21}\"}]}":from.kind==ServiceKind.EventBridge?"{\"detail\":{\"id\":\"example-1\",\"amount\":21}}":from.kind==ServiceKind.S3?"{\"Records\":[{\"eventSource\":\"aws:s3\",\"s3\":{\"bucket\":{\"name\":\"REPLACE_WITH_ACTUAL_BUCKET\"},\"object\":{\"key\":\"demo/example.json\"}}}]}":"{\"id\":\"example-1\",\"amount\":21}";
                    entry.example=from.kind==ServiceKind.Lambda?"Read destinations from json.loads(os.environ.get('TARGETS', '[]')); entries contain kind, name and nodeId. Physical AWS names come from TARGETS, never visible labels. "+(to.kind==ServiceKind.DynamoDB?"put_item(TableName=target['name'], Item={'id': {'S': event_id}, 'message': {'S': body}})":to.kind==ServiceKind.S3?"put_object(Bucket=target['name'], Key='demo/'+event_id+'.json', Body=body, ContentType='application/json')":to.kind==ServiceKind.SQS?"get_queue_url(QueueName=target['name']), then send_message; FIFO requires MessageGroupId.":"put_events with EventBusName=target['name'], Source='guategeeks.demo', DetailType='DemoEvent'; check FailedEntryCount."):
                        "Preserve workload.payloads normalization: HTTP body JSON, SQS Records/body, EventBridge detail, S3 Records with URL-decoded key and a bounded 64 KiB get_object. Default handler returns statusCode=200 and a JSON body with processed count.";
                    return entry;
                }).ToArray();
        }
    }
}
