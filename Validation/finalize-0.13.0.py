"""Attach allowlisted live acceptance evidence and verify the completed release hashes."""
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

root=Path(__file__).resolve().parents[1]
release=root/'Releases/0.13.0-20261004T092241Z'
backend=root.parent/'GuateGeeksAWS2026'
for name in ('0.13.0-acceptance.md','quest3-0.13.0-startup.txt','RealtimeLive-summary.txt','RealtimeLive-results.xml','voice-turn-comparison.md','32-workflow-review.png','33-code-editor.png','34-live-diagnostics.png'):
    shutil.copy2(root/'Validation'/name,release/'Validation'/name)
for name in ('code-authoring-acceptance.json','release-0.13.0-verification.json'):
    shutil.copy2(backend/'deployment'/name,release/'Validation'/name)
manifest_path=release/'manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest['headsetAcceptance']='Installed versionCode 15 and native lab initialization verified. Wearer targeting and new UI ergonomics pending; headset asleep.'
manifest['backendDeployment']='UPDATE_COMPLETE; deployed sources matched; live code publish/restore tests passed; temporary fixture cleaned.'
manifest['installedDevice']='Quest 3 / 2G0YC5ZG9J06XL'
manifest['regressions']={'backend':69,'editMode':64,'playMode':44,'liveRealtime':1}
files=sorted(p for p in release.rglob('*') if p.is_file() and p!=manifest_path)
manifest['artifacts']=[{'path':str(p.relative_to(release)),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
for item in manifest['artifacts']:
    data=(release/item['path']).read_bytes()
    assert len(data)==item['bytes'] and hashlib.sha256(data).hexdigest()==item['sha256']
sources=json.loads((release/'source-hashes.json').read_text(encoding='utf-8-sig'))
with zipfile.ZipFile(release/'source.zip') as archive:
    for item in sources:
        assert hashlib.sha256(archive.read(item['path'])).hexdigest()==item['sha256']
print('Verified',len(manifest['artifacts']),'artifact hashes and',len(sources),'archived source hashes.')
