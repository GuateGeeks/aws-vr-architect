import importlib.util
import io
from pathlib import Path
import unittest
import zipfile

spec=importlib.util.spec_from_file_location('open_ip_patch',Path(__file__).resolve().parents[1]/'deployment/open-ip-access.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class PublicIpPatchTests(unittest.TestCase):
    def test_live_patch_removes_only_guard_and_preserves_authentication_and_other_modules(self):
        original="def authorize_source(event):\n    raise ValueError('blocked')\n\ndef route(event):\n    authorize_source(event)\n    authenticate(event)\n    return 200\n"
        source=io.BytesIO()
        with zipfile.ZipFile(source,'w') as archive:
            archive.writestr('app.py',original);archive.writestr('assistant.py','private_broker=42\n')
        result,changed=module.patch_package(source.getvalue())
        with zipfile.ZipFile(io.BytesIO(result)) as archive:
            app=archive.read('app.py').decode()
            self.assertNotIn('authorize_source',app);self.assertIn('authenticate(event)',app)
            self.assertEqual(b'private_broker=42\n',archive.read('assistant.py'))
        self.assertEqual(['app.py'],changed)

    def test_policy_parser_supports_gateway_escaped_policy(self):
        self.assertEqual({'Statement':[]},module.parse_policy('{\\"Statement\\":[]}'))

if __name__=='__main__':unittest.main()
