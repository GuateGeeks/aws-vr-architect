import io
import json
import os
import zipfile
import unittest
from unittest.mock import MagicMock, patch
import test_backend as baseline
import app
import authoring
import code_runner


SOURCE = 'def handler(event, context):\n    return {"total": event.get("amount", 0) * 2}\n'


class AuthoringTests(unittest.TestCase):
    setUp = baseline.ApiTests.setUp
    event = baseline.ApiTests.event
    def test_routes_require_authentication_and_source_ip(self):
        for path in ('/v1/code/test', '/v1/code/validate', '/v1/deployments/1/code/publish'):
            event = self.event('POST', path, {'source': SOURCE})
            event['headers'] = {}
            with patch.object(app, 'client') as client:
                self.assertEqual(401, app.handler(event, self.context)['statusCode'])
                client.assert_not_called()

    def test_rollback_reads_only_chosen_immutable_version_and_checkpoints_current(self):
        client=MagicMock(); client.get_function_configuration.return_value={'RevisionId':'r1','CodeSha256':'oldsha'}
        client.list_versions_by_function.return_value={'Versions':[{'Version':'2','CodeSha256':'oldsha'}]}
        client.update_function_code.return_value={'Version':'4','RevisionId':'r2'}
        with patch.object(authoring,'resolve',return_value='owned'),patch.object(app,'client',return_value=client),patch.object(authoring,'source_from_function',return_value=SOURCE):
            result=authoring.publish(None,'1',{'confirmed':True,'revisionId':'r1','version':'1'},app,rollback=True)
        client.get_function.assert_called_once_with(FunctionName='owned',Qualifier='1')
        with zipfile.ZipFile(io.BytesIO(client.update_function_code.call_args.kwargs['ZipFile'])) as package:
            self.assertEqual(SOURCE,package.read('index.py').decode())
        self.assertEqual('2',result['rollbackVersion'])

    def test_versions_paginate_and_checkpoint_latest_matching_code(self):
        client=MagicMock();client.list_versions_by_function.side_effect=[{'Versions':[{'Version':'1'}],'NextMarker':'next'},{'Versions':[{'Version':'9'},{'Version':'3'}]}]
        self.assertEqual(['1','3','9'],[v['Version'] for v in authoring.published_versions(client,'owned',app)])
        self.assertEqual('next',client.list_versions_by_function.call_args.kwargs['Marker'])

    def test_package_rejects_non_aws_url_and_multiple_files(self):
        with self.assertRaises(app.ApiError):authoring.source_from_function({'Code':{'Location':'https://example.com/steal'}},app)
        package=io.BytesIO()
        with zipfile.ZipFile(package,'w') as archive:
            archive.writestr('index.py',SOURCE);archive.writestr('other.py','extra')
        opener=MagicMock();opener.open.return_value.__enter__.return_value.read.return_value=package.getvalue()
        with patch('authoring.urllib.request.build_opener',return_value=opener):
            with self.assertRaises(app.ApiError):authoring.source_from_function({'Code':{'Location':'https://code.s3.amazonaws.com/package'}},app)

    def test_test_timeout_does_not_claim_success_or_retry_workload(self):
        client=MagicMock();client.invoke.return_value={'FunctionError':'Unhandled','Payload':io.BytesIO(b'private error')}
        with patch.dict(os.environ,CODE_TEST_FUNCTION='isolated-test'),patch.object(app,'client',return_value=client):
            report=authoring.test_draft({'source':SOURCE,'eventJson':'{}'},app)
        self.assertFalse(report['passed']);self.assertEqual('',report['output']);client.invoke.assert_called_once()

    def test_validate_never_executes_draft_and_rejects_invalid_handler(self):
        with patch('builtins.exec', side_effect=AssertionError('must not execute')):
            self.assertTrue(authoring.validate_source(SOURCE, app)['valid'])
        for source in ('def broken(', 'print("no handler")', 'def handler(event): return event', 'x'*9000):
            with self.assertRaises(app.ApiError): authoring.validate_source(source, app)

    def test_isolated_runner_reports_output_and_errors(self):
        result = code_runner.handler({'source': SOURCE, 'event': {'amount': 21}}, self.context)
        self.assertTrue(result['passed']); self.assertEqual({'total': 42}, json.loads(result['output']))
        result = code_runner.handler({'source': 'def handler(event, context):\n    raise ValueError("private detail")', 'event': {}}, self.context)
        self.assertFalse(result['passed']); self.assertNotIn('private detail', result['message'])

    def test_test_endpoint_invokes_only_the_dedicated_runner(self):
        client = MagicMock(); client.invoke.return_value = {'Payload': io.BytesIO(b'{"passed":true,"output":"42"}')}
        with patch.dict(os.environ, CODE_TEST_FUNCTION='isolated-test'), patch.object(app, 'client', return_value=client):
            result = authoring.test_draft({'source': SOURCE, 'eventJson': '{"amount":21}'}, app)
        self.assertTrue(result['passed']); self.assertEqual('isolated-test', client.invoke.call_args.kwargs['FunctionName'])
        with self.assertRaises(app.ApiError): authoring.test_draft({'source': SOURCE, 'eventJson': '[]'}, app)

    def test_publish_requires_review_and_matching_revision(self):
        with self.assertRaises(app.ApiError): authoring.publish(None, '1', {}, app)
        client = MagicMock(); client.get_function_configuration.return_value = {'RevisionId': 'new', 'CodeSha256': 'sha'}
        with patch.object(authoring, 'resolve', return_value='demo-owned'), patch.object(app, 'client', return_value=client):
            with self.assertRaises(app.ApiError): authoring.publish(None, '1', {'confirmed': True, 'revisionId': 'old', 'source': SOURCE}, app)
        client.update_function_code.assert_not_called(); client.publish_version.assert_not_called()

    def test_publish_reuses_existing_checkpoint_and_optimistic_revision(self):
        client = MagicMock(); client.get_function_configuration.return_value = {'RevisionId': 'r1', 'CodeSha256': 'oldsha'}
        client.list_versions_by_function.return_value = {'Versions': [{'Version': '3', 'CodeSha256': 'oldsha'}]}
        client.update_function_code.return_value = {'RevisionId': 'r2', 'Version': '4', 'LastUpdateStatus': 'InProgress'}
        with patch.object(authoring, 'resolve', return_value='demo-owned'), patch.object(app, 'client', return_value=client):
            result = authoring.publish(None, '1', {'confirmed': True, 'revisionId': 'r1', 'source': SOURCE}, app)
        self.assertEqual('3', result['rollbackVersion']); client.publish_version.assert_not_called()
        self.assertEqual('r1', client.update_function_code.call_args.kwargs['RevisionId']); self.assertTrue(client.update_function_code.call_args.kwargs['Publish'])

    def test_checkpoint_revision_handoff_checks_code_and_configuration(self):
        for external_change in (False,True):
            client=MagicMock();current={'RevisionId':'r1','CodeSha256':'oldsha','MemorySize':128}
            refreshed=dict(current,RevisionId='r2',MemorySize=256 if external_change else 128)
            client.get_function_configuration.side_effect=[current,refreshed]
            client.list_versions_by_function.return_value={'Versions':[]};client.publish_version.return_value={'Version':'1'}
            client.update_function_code.return_value={'Version':'2','RevisionId':'r3'}
            with patch.object(authoring,'resolve',return_value='owned'),patch.object(app,'client',return_value=client):
                if external_change:
                    with self.assertRaises(app.ApiError):authoring.publish(None,'1',{'confirmed':True,'revisionId':'r1','source':SOURCE},app)
                    client.update_function_code.assert_not_called()
                else:
                    authoring.publish(None,'1',{'confirmed':True,'revisionId':'r1','source':SOURCE},app)
                    self.assertEqual('r2',client.update_function_code.call_args.kwargs['RevisionId'])

    def test_resolver_rejects_changed_stack_and_foreign_nodes(self):
        stack = {'StackId': 'current-stack', 'StackStatus': 'CREATE_COMPLETE'}
        with patch.object(app, 'get_stack', return_value=stack), patch.object(app, 'template', return_value={'Resources': {}}):
            with self.assertRaises(app.ApiError): authoring.resolve(None, '1', {'stackId': 'old-stack', 'resourceId': 'lambda1'}, app)
            with self.assertRaises(app.ApiError): authoring.resolve(None, '1', {'stackId': 'current-stack', 'resourceId': 'foreign'}, app)

    def test_isolated_role_has_no_workload_permissions(self):
        from pathlib import Path
        template = json.loads((Path(__file__).parents[1]/'template.json').read_text(encoding='utf-8'))
        statements = template['Resources']['CodeTestRole']['Properties']['Policies'][0]['PolicyDocument']['Statement']
        self.assertEqual({'logs:CreateLogStream', 'logs:PutLogEvents'}, set(statements[0]['Action']))
        self.assertEqual(3, template['Resources']['CodeTestFunction']['Properties']['Timeout'])


if __name__ == '__main__': unittest.main()
