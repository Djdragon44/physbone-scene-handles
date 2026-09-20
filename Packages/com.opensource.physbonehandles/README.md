# PhysBone Scene Handles

A free, open-source, cross-platform batch scene-view editor for VRChat `VRCPhysBoneCollider`
components. No license key, no phone-home license/HWID check, no OS check — it's a normal
Unity Editor package and should work anywhere Unity + the VRChat SDK does, Linux included.

## Provenance

This is an **original implementation**, written from scratch against VRChat's own public
`VRC.Dynamics`/`VRC.SDK3.Dynamics.PhysBone` SDK (decompiled from VRChat's official, publicly
distributed SDK DLLs purely to confirm exact field names — that's normal SDK usage, not
reverse-engineering a third party's product) and the publicly-described feature set /
promotional GIF of Dreadrith's "Avatar Dynamics Overhaul" (batch, additive scene-handle
editing of PhysBoneColliders with Alt = "edit only this one" / Shift = "equalize across
selection" modifiers). No code, IL, or implementation detail from that tool's binary was
read, referenced, or ported — the interaction *idea* (batch handles with modifier keys) is
functionality, not copyrightable expression, and every line here is new.

## What it does

- Select one or more objects that have a `VRCPhysBoneCollider` component. Handles for
  Radius, Height (capsule only), Position, and Rotation (capsule only) are drawn on **every**
  selected collider at once in the Scene view.
- Select one or more objects that have a `VRCPhysBone` component instead, and the same
  "Radius" toggle draws one grab handle per bone in the chain, sticking out sideways from
  each node. Dragging a handle shapes the `radiusCurve` (the per-position multiplier on the
  base `radius`) at that point — e.g. grab the middle of a tail and pull it out to bulge it,
  or pull the tip in to taper it, without hand-editing curve keys in the inspector. The base
  `radius` field itself is untouched by these handles (still just a normal inspector field) —
  they only ever shape the curve. Same Alt (only this chain) / Shift (set every selected
  chain's curve to the same value at that point) batching as everything else.
- A small toggle panel in the bottom-right of the Scene view turns each handle type on/off
  (green = active, red = off) so you're not fighting five overlapping gizmos at once.
- While Position or Rotation editing is active, Unity's own built-in Move/Rotate gizmo for
  the selected object is temporarily hidden (`Tools.hidden`), since it otherwise sits right
  on top of our handles and it's easy to grab the wrong one and drag the actual bone instead
  of just the collider's offset. It comes back as soon as nothing with a collider is selected
  or both toggles are off.
- Drag any handle:
  - **No modifier** — the delta you drag is applied to every selected collider (batch,
    additive).
  - **Alt** — only the collider you're actually dragging changes.
  - **Shift** — every selected collider is set to the exact same value as the one you
    dragged (equalize).
- `GameObject > PhysBone Handles` menu:
  - **Add VRC Phys Bone Collider to Selection** — adds the component to every selected
    object that doesn't already have one.
  - **Add VRC Phys Bone to Selection** — same, for `VRCPhysBone`.
  - **Copy Collider Settings (Active -> Selection)** — copies shape/radius/height/bounds
    behavior (not root/position/rotation, which are per-bone) from the active object's
    collider onto the rest of the selection.

## Installing

Copy this whole folder into your project's `Packages/` directory (or add it via the Unity
Package Manager as a local package pointing at this folder). It requires the VRChat SDK3
Avatars package (`com.vrchat.base`) to already be installed — everything here is compiled
out (`#if PBHANDLES_VRCSDK_PRESENT`) if it isn't, so it's safe to have installed even in
non-VRChat projects.

## Status / what to test

I don't have a way to run the Unity Editor myself, so this hasn't been compiled or tested
in-editor yet — it's written carefully against the real SDK API, but you're the first
compile+run pass. Things worth checking first:

- Does it compile cleanly in your project (2022.3.22f1 + `com.vrchat.base`)?
- Sphere radius handle (`Handles.RadiusHandle`) — drag behavior and visuals.
- Capsule height handles (top/bottom cones) — do they resize symmetrically the way you'd
  expect, including when `rootTransform` is set to something other than the collider's own
  transform?
- Alt / Shift modifier behavior across a multi-selection (e.g. all finger tip colliders).
- Non-uniform scale on the root bone — the code only uses `lossyScale.x` as a uniform scale
  factor, which will be wrong if a bone is scaled non-uniformly. Worth flagging if that's
  a real case for your rig.
- PhysBone radius handle (`PhysBoneRadiusSceneHandles.cs`) — this one's newer and untested
  in-editor too. The chain-walk for the wireframe preview assumes a simple single-child bone
  chain (like a finger or ponytail); branching chains just stop drawing at the branch, which
  is probably fine but worth a look on something like hair with multiple strands.

Report back anything that doesn't compile or feel right and I'll fix it directly.

## License

MIT — see `LICENSE`. Do whatever you want with it.
