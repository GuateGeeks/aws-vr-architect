# 0.24.4 recovery

Recovered from the supplied production APK and read-only AWS CLI access through WSL on October 17, 2026 (Guatemala).

## Result

The production Python backend is restored in `backend/src`. A working reconstruction of the quetzal and hand interactions is now integrated into Unity, labeled **0.24.4-recovery.1** (Android versionCode 31). See [Unity reconstruction](UNITY-RECONSTRUCTION.md) for controls, validation, and remaining differences. This is reconstructed behavior rather than the original 0.24.4 source text. The supplied signed production APK remains intact at the project root.

### Exact recovery

- Downloaded all four Lambda functions in us-east-1, including both published versions of the demo function and its current code: six packages total. Verified each ZIP's SHA-256 against AWS's `CodeSha256`.
- `ggawsday-control` and `ggawsday-rooms` contain identical first-party Python source. These files were copied into `backend/src`, with prior bytes preserved in `baseline/backend/src`. Six files differ byte-for-byte because of newline differences; only `assistant.py` and `collaboration.py` differ in text content.
- Recovered collaboration changes clamp float32 table-edge rounding and broadcast renewed object leases to observers. The production assistant omits two event/GuateGeeks context paragraphs present in the baseline; the original paragraphs remain in the baseline backup and `backend.patch`.
- All **114 backend tests pass**, including three added regression tests. See `backend-tests.log`. Tests run offline; they did not invoke production functions or create workloads.
- Saved original and processed CloudFormation templates, stack resource inventory, Lambda package/version inventory, and S3 deployment object/version inventory. The original template differs from the local template only in its three packaged `CodeUri` references. Its packaged references predate the latest direct Lambda code updates; **the downloaded live Lambda packages are authoritative for current backend code**.
- Preserved all APK `assets/`, IL2CPP metadata, and the ARM64 native binary. `apk-manifest.json` records the original APK hash.
- Parsed the Android manifest: `com.guategeeks.awsarchitectlab`, version **0.24.4**, versionCode **30**, ARM64, minSdk **32**, targetSdk **34**. The binary reports Unity **6000.6.3f1**.

No AWS resources, configuration, workload data, credentials, or secrets were modified. No Secrets Manager values were fetched. Temporary signed Lambda download URLs were used in memory and were not saved.

### Unity recovery evidence

- `decompiled/GuateGeeks.AwsVr/`: 84 production type files containing approximate decompiled C#. Many bodies contain invalid IL translations, unresolved operations, and incorrect types. They are **reference material, not compilable replacement scripts**. The analyzer's “100%” message means its analysis completed, not that it recovered correct original C#.
- `cpp2il-types/`: exact recoverable class/member metadata with empty method bodies. These are signatures, not working scripts.
- `address-map/GuateGeeks.AwsVr/`: method/file-offset/length annotations. Bodies here are dummy defaults and must not replace working code.
- `native-disassembly/` and `native-method-map.json`: 1,856 nonzero native method entries across 76 named types, including nested compiler-generated methods retained in their containing output. ARM64 instruction addresses are mapped through ELF load segments; file offsets and virtual addresses are distinguished.
- `unity-member-delta.json` and `unity-delta-summary.txt`: heuristic member-name comparison to the baseline. This identifies additions, not all changed method bodies. It can overcount nested declarations or members shared across files.
- `exported-assets/`: decoded textures (88 source Texture2D objects), 13 mesh objects, four TextAssets, material/shader/scene JSON, and MonoScript identities. See `asset-inventory.json` for source file/object identities. Subdirectories distinguish asset files. Root-level older exports may duplicate objects from the initial extraction pass.
- `asset-export-errors.json`: 91 objects (PlayerSettings and MonoBehaviours) cannot be fully decoded with the available type definitions. Their original bytes and explicitly labeled partial JSON are preserved under `exported-assets/unparsed-raw/`; the complete original asset container is also preserved. Partial JSON must not be treated as a complete serialized scene/settings recovery.
- `reconstructed/BeaconButton.cs`: a small readable reconstruction from the native instructions, kept outside Assets. It is not integrated or validated in Unity.

Confirmed production additions/expansions include:

1. A substantially expanded interactive `DigitalQuetzal`: perching, touch/body regions, head/eye behavior, landing and departure states; expanded `QuetzalMesh`, `QuetzalArm`, and `QuetzalSenses`.
2. `DatabaseItemView`, structured item/card parsing, and item-reader integration.
3. `BeaconButton`, hand beacon visuals, and associated LabRig interaction.
4. Panel near-grab/grip-edge methods in `LabMenu`.
5. Welcome/guide action and focus controls, plus CloudWatch reader additions in `ArchitectureLab`.

The quetzal, hand beacons, production index-only UI pressing, and near-grab panel edges have since been reconstructed and integrated; see UNITY-RECONSTRUCTION.md. The structured database viewer and welcome/guide refinements remain recovery evidence only. The project was migrated by the installed Unity 6000.6.6f1 editor for compilation and testing; the original APK uses 6000.6.3f1. Android build support is installed and a locally signed demo APK has built successfully. Physical headset acceptance is still pending.

## Evidence locations and reproduction

- `lambda/`: exact downloaded ZIPs and extracted contents; includes older code-test package and active demo source/version history. Keep these locally; they are excluded from Git with other large binary evidence.
- `baseline/`: original backend files before restoration.
- `recover.py`: read-only AWS collection and APK extraction. Uses the currently authenticated WSL AWS CLI, verifies the expected AWS account, and does not save credentials or signed URLs.
- `restore_backend.py`: verifies package hashes and copies only first-party source after saving baseline files.
- `apk_manifest.py`, `compare_unity.py`, `extract_assets.py`, `disassemble.py`: reproducible inspection/export helpers.
- `tools/`: downloaded tools and libraries. Official Cpp2IL development build was required because the release build rejected metadata version 108. Its exact version is in `cpp2il-dev.log`; ILSpy version is 9.1.0.7988. Sources: https://github.com/SamboyCoding/Cpp2IL and https://github.com/icsharpcode/ILSpy.

`recovered-source-and-evidence.zip` packages first-party backend snapshots, reports, approximate C#, address maps, native disassembly, exported assets, and the downloaded Lambda ZIPs. The original APK and separately extracted raw APK files are not duplicated inside this archive; preserve the original APK alongside it. `recovery-bundle-manifest.json` records the archive hash and included file count.

No Git commit was made. Large generated evidence/tool directories are excluded by `Recovery/.gitignore`; readable first-party recovery material remains available for version control.
