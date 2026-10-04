import json
import os
import sys
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
import app
import inspection


class InspectionTests(unittest.TestCase):
    def setUp(self):
        self.cfn, self.service = MagicMock(), MagicMock()
        self.name = 'ggawsday-demo-1'
        self.stack = {'StackId': 'confirmed-stack', 'StackStatus': 'CREATE_COMPLETE'}
        self.query = {'stackId': 'confirmed-stack', 'resourceId': 'node1'}
        self.definition = {'Resources': {'Node': {'Type': 'AWS::DynamoDB::Table', 'Metadata': {'NodeId': 'node1'}}}}
        for target, value in [('get_stack', self.stack), ('template', self.definition),
                              ('resources', [{'LogicalResourceId': 'Node', 'PhysicalResourceId': self.name + '-table'}]),
                              ('client', self.service)]:
            patcher = patch.object(app, target, return_value=value); patcher.start(); self.addCleanup(patcher.stop)
        patcher = patch.object(app, '_secret', (float('inf'), {'password': 'Test42'})); patcher.start(); self.addCleanup(patcher.stop)

    def inspect(self, mode='items', query=None):
        return inspection.inspect(self.cfn, self.name, mode, query or self.query, app)

    def test_items_are_bounded_typed_read_only_and_cursor_bound(self):
        self.service.scan.return_value = {'Items': [{'id': {'S': 'one'}, 'value': {'N': '1234567890123456789'}}], 'LastEvaluatedKey': {'id': {'S': 'one'}}}
        page = self.inspect()
        args = self.service.scan.call_args.kwargs
        self.assertEqual(10, args['Limit']); self.assertFalse(args['ConsistentRead'])
        self.assertEqual(self.name + '-table', args['TableName'])
        self.assertIn('1234567890123456789', page['entries'][0]['text'])
        self.inspect(query=dict(self.query, cursor=page['cursor']))
        self.assertEqual({'id': {'S': 'one'}}, self.service.scan.call_args.kwargs['ExclusiveStartKey'])
        with self.assertRaises(app.ApiError): self.inspect(query=dict(self.query, cursor=page['cursor'] + 'x'))
        self.stack['StackId'] = 'replacement'
        with self.assertRaises(app.ApiError): self.inspect(query=dict(self.query, stackId='replacement', cursor=page['cursor']))

    def test_resource_and_stack_ownership_checked_before_data_read(self):
        for query in ({}, {'stackId': 'different', 'resourceId': 'node1'}, dict(self.query, resourceId='foreign-table')):
            with self.assertRaises(app.ApiError): inspection.inspect(self.cfn, self.name, 'items', query, app)
        with self.assertRaises(app.ApiError): self.inspect('logs')
        self.service.scan.assert_not_called(); self.service.filter_log_events.assert_not_called()

    def test_logs_window_pagination_and_explicit_truncation(self):
        self.definition['Resources']['Node']['Type'] = 'AWS::Lambda::Function'
        self.service.filter_log_events.return_value = {'events': [{'timestamp': 100000, 'message': 'x' * 5000}], 'nextToken': 'next'}
        page = self.inspect('logs'); args = self.service.filter_log_events.call_args.kwargs
        self.assertEqual(900000, args['endTime'] - args['startTime']); self.assertEqual(10, args['limit'])
        self.assertEqual('/aws/lambda/' + self.name + '-table', args['logGroupName'])
        self.assertTrue(page['entries'][0]['truncated']); self.assertEqual(4096, len(page['entries'][0]['text']))
        self.inspect('logs', dict(self.query, cursor=page['cursor']))
        self.assertEqual(args['endTime'], self.service.filter_log_events.call_args.kwargs['endTime'])
        with patch.object(inspection.time, 'time', return_value=args['endTime'] / 1000 + 901):
            with self.assertRaises(app.ApiError): self.inspect('logs', dict(self.query, cursor=page['cursor']))

    def test_empty_page_can_have_more_results(self):
        self.service.scan.return_value = {'Items': [], 'LastEvaluatedKey': {'id': {'S': 'one'}}}
        page = self.inspect(); self.assertEqual([], page['entries']); self.assertTrue(page['cursor'])

    def test_delete_replacement_rejected_before_purge(self):
        with patch.object(app, 'purge_buckets') as purge:
            with self.assertRaises(app.ApiError) as error:
                app.delete(self.cfn, self.name, '1', True, MagicMock(), 'older-stack')
            self.assertEqual(409, error.exception.status); purge.assert_not_called(); self.cfn.delete_stack.assert_not_called()

    def test_delete_uses_immutable_id_and_purges_only_confirmed_stack(self):
        with patch.object(app, 'purge_buckets', return_value=True) as purge, patch.dict(os.environ, {'PROVISIONER_ROLE_ARN': 'role'}):
            code, result = app.delete(self.cfn, self.name, '1', True, None, 'confirmed-stack')
            self.assertEqual(202, code)
            purge.assert_called_once_with(self.cfn, self.name, None, 'confirmed-stack')
            self.cfn.delete_stack.assert_called_once_with(StackName='confirmed-stack', RoleARN='role')

    def test_routes_require_existing_auth_and_source_controls(self):
        event = {'httpMethod': 'GET', 'path': '/v1/deployments/1/items', 'queryStringParameters': self.query}
        self.service.scan.return_value = {'Items': []}
        with patch.object(app, 'authorize_source') as source, patch.object(app, 'authenticate') as auth:
            code, page = app.route(event, None)
            source.assert_called_once(); auth.assert_called_once(); self.assertEqual(200, code)
            self.assertEqual('confirmed-stack', page['stackId'])


if __name__ == '__main__': unittest.main()
