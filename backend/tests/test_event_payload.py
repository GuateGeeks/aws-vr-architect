import json
import unittest
from unittest.mock import MagicMock, patch
import test_backend as baseline
import app


class EventPayloadTests(unittest.TestCase):
    def invoke(self, data):
        cfn = MagicMock()
        template = {'Resources': {'Queue': {'Metadata': {'NodeId': 'q', 'Kind': 4}}}}
        service = MagicMock()
        with patch.object(app, 'get_stack', return_value={'StackId': 'original', 'StackStatus': 'CREATE_COMPLETE'}), \
             patch.object(app, 'template', return_value=template), \
             patch.object(app, 'resources', return_value=[{'LogicalResourceId': 'Queue', 'PhysicalResourceId': 'https://queue'}]), \
             patch.object(app, 'client', return_value=service):
            result = app.invoke(cfn, 'demo', dict(resourceId='q', **data))
        return result, service

    def test_custom_fields_preserved_and_correlation_owned_by_server(self):
        (status, result), service = self.invoke({'stackId': 'original', 'eventJson': '{"message":"hola", "amount":42, "id":"spoof"}'})
        payload = json.loads(service.send_message.call_args.kwargs['MessageBody'])
        self.assertEqual(202, status)
        self.assertEqual('hola', payload['message'])
        self.assertEqual(42, payload['amount'])
        self.assertEqual(result['eventId'], payload['id'])
        self.assertNotEqual('spoof', payload['id'])

    def test_invalid_payloads_and_replaced_stack_never_send(self):
        for data in ({'eventJson': '[]'}, {'eventJson': '{'}, {'eventJson': '{"x":NaN}'}, {'eventJson': 'x' * 4097}, {'eventJson': False}, {'eventJson': None}, {'stackId': 'replacement'}):
            with self.subTest(data=str(data)[:80]), self.assertRaises(app.ApiError):
                self.invoke(data)

    def test_empty_payload_keeps_standard_message(self):
        (_, result), service = self.invoke({'eventJson': ''})
        self.assertEqual({'message': 'AWS Day test', 'id': result['eventId']}, json.loads(service.send_message.call_args.kwargs['MessageBody']))
