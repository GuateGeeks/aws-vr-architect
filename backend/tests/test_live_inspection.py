import contextlib
import io
import json
import os
import unittest
from unittest.mock import MagicMock, patch
import test_inspection as baseline
import app
import workload


class LiveInspectionTests(unittest.TestCase):
    setUp = baseline.InspectionTests.setUp
    inspect = baseline.InspectionTests.inspect
    def test_resume_overlaps_and_is_bound_to_filter_and_resource(self):
        self.definition['Resources']['Node']['Type'] = 'AWS::Lambda::Function'
        self.service.filter_log_events.return_value = {'events': []}
        with patch('inspection.time.time', return_value=2000):
            first = self.inspect('logs', dict(self.query, q='pedido'))
        self.assertTrue(first['resume']); self.assertFalse(first['incremental'])
        with patch('inspection.time.time', return_value=2010):
            following = self.inspect('logs', dict(self.query, q='pedido', resume=first['resume']))
        self.assertEqual(1970000, self.service.filter_log_events.call_args.kwargs['startTime'])
        self.assertTrue(following['incremental'])
        with self.assertRaises(app.ApiError): self.inspect('logs', dict(self.query, resume=first['resume']))
        with self.assertRaises(app.ApiError): self.inspect('logs', dict(self.query, q='pedido', cursor=first['resume']))

    def test_distinct_identical_messages_keep_cloudwatch_ids(self):
        self.definition['Resources']['Node']['Type'] = 'AWS::Lambda::Function'
        text = json.dumps(dict(eventId='test-event', level='ERROR', stage='failed', nodeId='node1'))
        self.service.filter_log_events.return_value = {'events': [dict(eventId=i,timestamp=100,message=text) for i in ['a','b']]}
        page = self.inspect('logs',dict(self.query,eventId='test-event',level='ERROR'))
        self.assertEqual(['a','b'],[e['id'] for e in page['entries']])
        self.assertEqual('test-event',page['entries'][0]['eventId'])
        self.assertEqual([],self.inspect('logs',dict(self.query,level='INFO'))['entries'])

    def test_event_lookup_uses_exact_key_and_stable_item_identity(self):
        self.service.get_item.return_value = {'Item': {'id': {'S':'event-1'}, 'value': {'N':'10000000000000000001'}}}
        first=self.inspect(query=dict(self.query,eventId='event-1'))
        self.service.get_item.assert_called_once_with(TableName=self.name+'-table',Key={'id':{'S':'event-1'}},ConsistentRead=True)
        self.service.scan.assert_not_called()
        self.service.get_item.return_value['Item']['value']['N']='200'
        second=self.inspect(query=dict(self.query,eventId='event-1'))
        self.assertEqual(first['entries'][0]['id'],second['entries'][0]['id'])
        self.assertNotEqual(first['entries'][0]['text'],second['entries'][0]['text'])

    def test_filter_validation_and_empty_exact_lookup(self):
        for fields in [dict(q='x'*81),dict(level='FATAL'),dict(eventId='../../bad')]:
            with self.assertRaises(app.ApiError): self.inspect(query=dict(self.query,**fields))
        self.service.get_item.return_value={}
        self.assertEqual([],self.inspect(query=dict(self.query,eventId='missing'))['entries'])

    def test_delivery_is_reported_only_after_success(self):
        service=MagicMock(); output=io.StringIO()
        env=dict(NODE_ID='node1',TARGETS='[{"kind":2,"name":"demo-table","nodeId":"table"}]')
        with patch.dict(os.environ,env),patch.object(workload.boto3,'client',return_value=service),contextlib.redirect_stdout(output):
            workload.handler({'id':'event-1'},None)
        logs=[json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual(['received','delivered','processed'],[r['stage'] for r in logs])
        self.assertEqual('table',logs[1]['targetNodeId'])
        service.put_item.side_effect=RuntimeError('failed'); output=io.StringIO()
        with patch.dict(os.environ,env),patch.object(workload.boto3,'client',return_value=service),contextlib.redirect_stdout(output):
            with self.assertRaises(RuntimeError): workload.handler({'id':'event-1'},None)
        self.assertNotIn('delivered',output.getvalue())
        failure=json.loads(output.getvalue().splitlines()[-1])
        self.assertEqual(('failed','ERROR','event-1'),(failure['stage'],failure['level'],failure['eventId']))


if __name__=='__main__': unittest.main()
