# AWS icons in the VR lab

## 3D service emblems (source update)

Tabletop objects are now solid emblems built from the official icon geometry, replacing the flat PNG badge and the handmade service silhouettes. Each emblem is a 30 × 30 × 7 cm tile in the official category colour with a 1.2 cm machined bevel. The white service symbol is raised 9 mm on both faces; the back face is mirrored so it never reads backwards. The emblem floats in the projection cone, turns smoothly to face the viewer and sways ±13° so the relief catches the light. Reduced motion removes the sway. The catalog, inspector and library still use the official flat PNGs.

- **Geometry source:** `Tools/Generate-AwsIconGeometry.py` (Python standard library only) reads the white symbol path from each 64 px SVG in `aws-icons/Architecture-Service-Icons_07312026`. It:
  - flattens Bézier and arc segments to a 0.035-unit tolerance in the 80 × 80 viewBox;
  - resolves the even-odd fill into polygons with holes;
  - triangulates them with an embedded port of mapbox/earcut (ISC);
  - writes `Assets/GuateGeeks/Runtime/AwsIconGeometry.Data.cs`.

  Triangle area matches polygon area for all seven icons. When rasterised, the generated symbols overlap the official 512 px PNGs at 0.89–0.94 IoU; the difference is anti-aliasing on 2-unit strokes.
- **Mesh** (`AwsIconGeometry.cs`): one shared mesh per service, built once and reused by every object of that kind. It has a bevelled tile, front and back symbol faces, and side walls with smoothed normals on curves and crisp corners. Vertex colours carry the category colour (converted to linear space) and an emission flag. The largest mesh (SQS) has about 7k vertices. Each object is one draw call with no textures.
- **Shading** (`Resources/LabEmblem.shader`): a view-relative key and fill light, specular highlight, cyan holographic rim and a slow scan band tied to the reduced-motion clock. The white symbol is self-lit and its walls are partly lit, so the relief reads from any side of the table.
- **Interaction:** the emblem has no collider; the node keeps its existing hit box, ports and grab rules. Placement previews show a full-size emblem inside the ghost volume.
- **Regenerate** after replacing the icon set: `python3 Tools/Generate-AwsIconGeometry.py`. The script prints per-icon vertex/triangle counts and the area check.
- **Tests:** EditMode `AwsIconGeometryTests` checks complete triangulation, clockwise front winding, bounds, relief depth and official colours. PlayMode `OfficialAwsIconsRemainReadableAndDoNotInterceptInteractions` checks one shared mesh per service, no tabletop sticker, no colliders, facing the viewer from both sides of the table and placement size. It writes `Validation/36-aws-icons-overview.png`, `37-aws-icons-tabletop.png` and `39-aws-emblem-back.png`.

Verified on October 5, 2026 in the open Unity 6000.6.3f1 editor: EditMode 74/74 and PlayMode 53/53. Quest frame timing and stereo legibility have not yet been checked on the headset.

## Flat icon textures (0.17.0)

The supplied `aws-icons/Architecture-Service-Icons_07312026` collection contains an official service icon for each of the lab's seven supported components: API Gateway, Lambda, DynamoDB, S3, SQS, EventBridge and CloudWatch. The 64-size source variants have an 80 × 80 SVG viewBox, solid category backgrounds and white service outlines. Resource/category/group icons and `__MACOSX` archive metadata are not needed for the current service catalog.

The existing UI used text-only catalog buttons, handmade front-facing node symbols and letter abbreviations in saved-design thumbnails. Official icons now appear in catalog buttons, the selected object's service row, placement previews, tabletop service volumes and library thumbnails. Service names, input/output ports, state text and selection rings retain their meaning. Original three-dimensional service silhouettes remain behind the badges.

## Rendering choices

- Preserve the original SVG paths, backgrounds and colors. The solid tile provides contrast against the animated holographic geometry; no artwork modification is required.
- Generate seven 512 × 512 PNG textures in `Assets/GuateGeeks/Resources/AwsIcons`. Only these selected assets ship in the player; the large source archive remains outside `Assets`.
- Use trilinear filtering, mipmaps, clamp wrapping and uncompressed textures to keep white outlines clean at viewing distance. These seven RGBA textures require approximately 9.3 MiB including mipmaps; no runtime SVG parser or additional Unity package is required.
- Catalog tiles occupy 48 of the existing 64-pixel row height. Text moves right into a separate 376-pixel area. Icons do not receive UI raycasts or add colliders.
- Tabletop badges face the viewer horizontally and sit outside the service volume, including during size changes and placement. They follow reduced-motion behavior through their parent volume and do not introduce another animated orbit.
- Existing accent colors identify controls and state. The supplied official tile colors identify the service; for example, the purple CloudWatch tile appears alongside the lab's cyan observability controls.

## Regenerate assets

Run `Tools/Generate-AwsIcons.cjs` with Node and pass the directory containing the `sharp` package as its first argument. The script lists its source mapping and preserves the supplied SVGs. Unity's `AwsIconImporter` applies the texture settings on import. Commit the generated PNGs and their Unity `.meta` files together.

## Validation

The PlayMode test `OfficialAwsIconsRemainReadableAndDoNotInterceptInteractions` checks all seven loaded textures, mipmaps, node badge mappings, camera-facing orientation, placement visibility and catalog/cancel actions. It writes `Validation/36-aws-icons-overview.png` and `Validation/37-aws-icons-tabletop.png` for visual review. Physical Quest readability and sustained performance require headset validation.

Verified on October 4, 2026 in Unity 6000.6.3f1: the focused icon test passed, then all 43 `LabExperienceTests` passed with zero failures. Results are in `Validation/AwsIcons-results.xml` and `Validation/AwsIcons-regression-results.xml`. Both rendered captures were visually reviewed.

## Quest installation

Version 0.17.0 (Android version code 19) was built and installed on the connected Quest 3 on October 4, 2026. The Android ARM64/Vulkan build succeeded with zero errors and nine warnings; its APK signature verified. Installation used `adb install -r`, preserving app data, and the launch request returned `Status: ok`. The installed package reports version 0.17.0 and a running app process.

The versioned APK and SHA-256 file are in `Builds/Quest3/GuateGeeksAWSVR-Quest3-0.17.0.apk` and its `.sha256` companion. Build, preflight, metadata, signature, install and runtime records are under `Validation/quest3-*-0.17.0.txt`.

The headset reported `mWakefulness=Asleep`, so a device screenshot was unavailable and physical visual acceptance remains pending. Startup logs include the same missing Google Play `AssetPackManager` class warning recorded in version 0.16.0. Put on the headset and open AWS Architect Lab to check the icons, placement and selection in stereo.
