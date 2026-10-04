# 0.12.1 — Voice destination fix

Fixes the reported failure where ATLAS could hear “muévelo aquí” but could not find a valid destination even while the user pointed at the table.

The old ray test intersected an invisible plane at the holograms' 1.5 m height. A hand/controller held below that height and aimed down could never hit it. Voice destinations now use the visible tabletop at 0.74 m and place the hologram above the same x/z position at its normal 1.5 m height.

Activate ATLAS, identify the component, then point at a free spot on the table. Hold the aim briefly until **AQUÍ · DESTINO DE VOZ** appears, and say “muévelo aquí.” The marker shows both the point on the table and the floating destination. Floating buttons keep their click behavior while permitting a voice destination on the table behind them; component targets and menu grab handles retain priority.

A stable destination from the current speech turn survives up to ten seconds after the hand is lowered, covering delayed turn acceptance. It is not reused for an unrelated later utterance. Conflicting destinations from two hands remain ambiguous. The live marker updates independently from the frozen context of the previous turn.

Movement still validates workspace bounds and component collisions and supports undo. This patch does not alter AWS resources or the Realtime audio settings.

Regression tests cover rays from below hologram height, transformed workspaces, delayed acceptance, stale and conflicting destinations, live feedback, and an actual scene move through the same ray-picking path as the controllers.
