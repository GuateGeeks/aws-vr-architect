# Hand-first, near-field interface

**Problem:** panels were large and far away. The code studio was 1.4 × 1.6 m at 1.6 m, the keyboards were 2–2.3 m wide with 16–18 cm keys, and the catalog and inspector were 1.1 × 1.7 m about 3.4 m away. Settings sat at 2.05 m height, and the cloud and environment panels sat above 3 m. Everything needed a pointing ray and a raised or fully extended arm, and only the index fingertip could press a button.

**Goal:** a wrist menu for access, keyboards you type on with your fingers, code and inspection screens that follow your head at a comfortable size, any finger presses, and nothing needs a raised or fully extended arm.

## What changed

### Near-field cockpit (`SharedSpace.Cockpit`)
In the headset, the personal console becomes a cockpit around your station. It is defined as physical offsets from your eyes (standing eye height 1.65 m):

| Surface | Where (from the eyes) | Size | Smallest target |
|---|---|---|---|
| Controls desk (02) | 0.65 m ahead, 47° down, tilted 44° toward you | 0.58 × 0.20 m | 2.2 cm (secondary row), 2.9 cm (main row) |
| Catalog (01) / Inspector (03) | 0.66 m, ±48° to the sides, 26° down | 0.24 × 0.38 m | 2.4–2.8 cm |
| Settings | 0.54 m ahead, 16° down | 0.50 × 0.50 m | connection keys 3.2 × 2.3 cm |
| ATLAS panel | 0.64 m, 29° left | 0.42 × 0.43 m | — |
| ATLAS presence / Guide | above the wings, at eye height | 0.24–0.26 m wide | — |
| Status line | 1 m ahead, 17° up (clears every hologram) | 0.77 m wide | read-only |

- Every interactive surface is within about 0.5 m of a shoulder and below the eye line. Body text is about 15 dmm (1 cm tall at 0.65 m).
- When you are alone at the full table, the cockpit also moves your stand to station 1's spot, 2.6 m from the centre, so the table is closer.
- The cockpit replaces the old shared-room compact console. It keeps the same physical size and reach at every table size. On the 1 m table the desk sits lower, under the holograms, and the status line uses the small table's own pose.
- **Settings → Controles → Paneles** chooses the layout:
  - **automático** (default): the cockpit in the headset, the panoramic console in desktop rehearsal;
  - **al alcance**: always the cockpit;
  - **panorámicos**: always the panoramic console.

  A shared room and the smaller tables always use the cockpit.

### Wrist menu (`LabRig.Wrist.cs`, replaces the palm grid)
- Turn the inside of your **left** wrist toward your face for a moment (palm toward the eyes, wrist in view, 0.22 s). A 17 × 13 cm menu appears beside the wrist, on the little-finger side, facing you. Tap it with any finger of the other hand.
- It stays up while a finger is on it and folds away about 0.4 s after the wrist turns away. The hand wearing it cannot press it.
- **Settings → Controles → Menú de muñeca** moves it to the right wrist.
- It has 12 buttons, each 3.6 × 2.9 cm:
  - **CREAR** (catalog), **MESA** (controls desk), **FICHA** (inspector), **ATLAS**;
  - **INSPECCIÓN** (slots / reader), **CÓDIGO** (the selected Lambda), **AJUSTES**, **GUÍA**;
  - **DESHACER**, **CONECTAR**, **TRAER AQUÍ**, **CENTRAR**.
- Buttons light green while their panel is open, so the menu also shows what is open. **CÓDIGO** is greyed out until a Lambda is selected.
- **TRAER AQUÍ** moves the whole console so it is in front of you where you stand and look, at your real eye height. It also brings the head-following panels back in front and unpins them. **CENTRAR** returns everything to the station. In a shared room, TRAER AQUÍ only returns the console to your station, to keep co-location.
- With controllers, the left menu button (☰) toggles the menu above the controller. In desktop rehearsal, **M** toggles it in the lower-left corner.

### Touch keyboards (`ComfortLayout.PlaceKeyboard`)
- The design, code and search keyboards open at a typing pose in front of the lower chest: 0.55 m from the eyes, about 40° down, tilted 38° toward you, about 0.48 m from the shoulder. They stay fixed in the world while you type.
- Keys are 3.6 cm apart centre to centre. Design keys are 3.3 × 2.4 cm and code keys 3.3 × 2.2 cm.
- The code keyboard was re-laid out so that every key, including the function rows, is at least 2.2 cm tall.
- The typed text shows on the keyboard itself. In desktop rehearsal the keyboard opens lower in the view, about 0.87 m away.
- The connection keyboard lives in the settings panel, which in the cockpit puts its keys at 3.2 × 2.3 cm at chest height.

### Code and inspection follow your head (`HeadFollow.cs`)
- The **Lambda code studio**, the **workspace reader** (inspection, review, library, placement) and **live diagnostics** follow a shared body heading, 0.7 m from the eyes and 7° below the eye line.
- Each has a slot: code in the middle, inspection 25° to the right, evidence 25° to the left. Open panels push apart, so they never overlap.
- Sizes:
  - code studio: 0.49 × 0.57 m (about 38° × 44°);
  - narrow reader: 0.24 × 0.36 m; wide reader: 0.49 m;
  - text is about 14–15 dmm.
- The follow is lazy. Glancing around the table (up to 38°), leaning in, or reading the far edge of a panel moves nothing. A real turn, a step of more than 25 cm, or a height change of more than 18 cm brings the panels along smoothly. With reduced motion they jump instead.
- A newly opened panel appears where you are looking.
- Dragging a panel by its grip bar pins it where you leave it. **TRAER AQUÍ** or **Restaurar paneles** unpins it.

### Every finger presses (`MultiFingerTouch`, `LabRig.Hands.cs`)
- All five fingertips of both hands can press buttons and keys. Each finger approaches from the front, touches and dwells for 80 ms, then has to withdraw before pressing again.
- When several fingers complete a press at once (a flat hand), only the deepest presses. The hand then has a 120 ms refractory period, and fingers that lose the arbitration must withdraw first. A resting neighbour finger never fires a key later.
- Feedback with bare hands:
  - a ring on the surface under each finger that comes within 3 cm, which tightens as the finger approaches and turns green on contact;
  - every fingertip carries an emitter that swells as it approaches;
  - the button flashes on a press and the glass click plays.
- Ray pinch for distant objects is unchanged. A finger near a button cancels a pending ray click, as before.

## Validation (Unity 6000.6.3f1 editor, October 5, 2026)
- EditMode **93/93**, including `FingerTouchTests`: every finger presses, a flat hand presses once, the refractory period, and reset mid-press.
- PlayMode **66/66**, including `NearFieldInterfaceTests`:
  - the cockpit is within reach, below the eyes, compact and facing you, with finger-sized targets and no clipped labels; TRAER AQUÍ and CENTRAR work; the panoramic layout restores exactly;
  - the wrist menu has 12 targets of at least 2.8 cm, opens and closes each workspace, and switches wrist;
  - keyboards sit 0.45–0.62 m away, below the eyes, facing you, with a 3.6 cm pitch; a middle-finger press types through the real collider geometry; there are no presses from behind; all code keys are at least 2.2 cm;
  - follow panels: code in the middle and the reader to the right without overlap, 25° glances ignored, a 100° turn followed, pinning and unpinning.
- Renders: `Validation/65-near-cockpit.png`, `66-wrist-menu.png`, `67-touch-keyboard.png` and `68-follow-readers.png`.
- The existing table-size and shared-room tests pass with the cockpit: in sector, the controls under the small table's holograms, the panoramic console restored.

## Not yet verified on a headset
No headset was attached. These need a Quest 3 session before the event:
- the wrist gesture thresholds (palm-to-eyes dot 0.55, 50° in view, 0.22 s dwell) and the menu's offset beside the wrist;
- accidental presses with five fingers, the 3 cm ring threshold and the 80 ms dwell when typing quickly;
- the left-controller menu button binding (`<XRController>{LeftHand}/{MenuButton}`);
- frame time with 10 fingertip probes and rings;
- comfort of the 0.7 m follow distance during long code reading. If needed, increase `HeadFollow.XrDistance` and the panel scales together to keep the same visual angle.
