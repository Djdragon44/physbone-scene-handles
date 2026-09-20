# PhysBone Scene Handles

A free, open-source, cross-platform batch scene-view editor for VRChat `VRCPhysBoneCollider`
and `VRCPhysBone` components, plus a tool to auto-generate a full set of body colliders from
your avatar's mesh. No license key, no phone-home license/HWID check, no OS check — it's a
normal Unity Editor package and should work anywhere Unity + the VRChat SDK does, Linux
included.

## Provenance

This is an **original implementation**, written from scratch against VRChat's own public
`VRC.Dynamics`/`VRC.SDK3.Dynamics.PhysBone` SDK (decompiled from VRChat's official, publicly
distributed SDK DLLs purely to confirm exact field names — that's normal SDK usage, not
reverse-engineering a third party's product) and the publicly-described feature set /
promotional GIF of Dreadrith's "Avatar Dynamics Overhaul" (batch, additive scene-handle
editing of PhysBoneColliders with Alt = "edit only this one" / Shift = "equalize across
selection" modifiers). No code, IL, or implementation detail from that tool's binary was
read, referenced, or ported — the interaction *idea* (batch handles with modifier keys) is
functionality, not copyrightable expression, and every line here is new. The Auto Collider
Generator (mesh-weight-based capsule fitting) has no equivalent in that tool at all — it's
new territory built for this package.

## What it does

### Batch collider/PhysBone scene handles

- Select one or more objects that have a `VRCPhysBoneCollider` component. Handles for
  Radius, Height (capsule only), Position, and Rotation (capsule only) are drawn on **every**
  selected collider at once in the Scene view.
- Select one or more objects that have a `VRCPhysBone` component instead, and the same
  "Radius" toggle draws one grab handle per bone in the chain, sticking out sideways from
  each node. Dragging a handle shapes the `radiusCurve` (the per-position multiplier on the
  base `radius`) at that point — e.g. grab the middle of a tail and pull it out to bulge it,
  or pull the tip in to taper it, without hand-editing curve keys in the inspector.
- A small toggle panel in the bottom-right of the Scene view turns each handle type on/off
  (green = active, red = off).
- While Position or Rotation editing is active, Unity's own built-in Move/Rotate gizmo for
  the selected object is temporarily hidden (`Tools.hidden`), since it otherwise sits right
  on top of our handles and it's easy to grab the wrong one and drag the actual bone.
- Drag any handle:
  - **No modifier** — the delta you drag is applied to every selected collider/chain (batch,
    additive).
  - **Alt** — only the one you're actually dragging changes.
  - **Shift** — every selected one is set to the exact same value as the one you dragged
    (equalize).
- `GameObject > PhysBone Handles` menu:
  - **Add VRC Phys Bone Collider to Selection** / **Add VRC Phys Bone to Selection** — adds
    the component to every selected object that doesn't already have one.
  - **Copy Collider Settings (Active -> Selection)** — copies shape/radius/height/bounds
    behavior (not root/position/rotation, which are per-bone) from the active object's
    collider onto the rest of the selection.

### Auto Collider Generator

`Tools > PhysBone Scene Handles > Auto Collider Generator` — fits a `VRCPhysBoneCollider` to
each of an avatar's core Humanoid bones (Hips, Spine, Chest, Shoulders, Upper/Lower Arms,
Upper/Lower Legs, Neck, Head) automatically:

- Point it at your avatar's root (needs a Humanoid `Animator`). It lists every
  `SkinnedMeshRenderer` under the avatar with a checkbox — pick which mesh(es) count as "the
  body" (it guesses using a mesh named "Body", or the highest-vertex mesh otherwise).
  **This matters**: leaving clothing/hair/accessories checked will inflate colliders past the
  actual body, since a flared skirt weighted to Hips (for example) gets treated as part of
  the hips.
- **Radius** is fit from actual mesh geometry: for each bone, it looks at which vertices are
  dominantly skin-weighted to it, recenters on their true centroid (a bone's pivot is rarely
  dead-center of its own cross-section), and takes a percentile of the spread — controlled by
  the **Fit Tightness** slider (lower = tighter, ignores more of the wide/outlier parts).
- **Length** comes from the skeleton directly — the actual distance from the bone to its
  child joint (e.g. UpperArm → LowerArm) — not from vertex weighting, so capsules reliably
  span the full limb even on rigs with twist bones that would otherwise fragment the mesh
  weighting data for the middle of a limb.
- **Radius Margin** inflates every radius afterward, so colliders sit slightly outside the
  skin rather than exactly on it.
- A live cyan wireframe preview in the Scene view shows exactly what Apply would create —
  updates as you drag the sliders or toggle bones/meshes, no need to actually apply to see
  the effect.
- Hit **Scan**, review the per-bone list (uncheck anything you don't want), then **Apply**.
  "Skip bones that already have a Collider" (on by default) leaves anything you've since
  hand-tuned alone on future runs.
- It only knows about Unity's Humanoid rig mapping, so custom jiggle bones (breasts, etc.)
  aren't covered — add those with the batch-add menu command above instead, then size them
  with the scene handles.

## Installing

Via VCC / ALCOM: add `https://djdragon44.github.io/physbone-scene-handles/index.json` as a
repository (Settings > Packages > Add Repository), then add "PhysBone Scene Handles" to your
project. Manually: download the `.zip`/`.unitypackage` from the
[latest release](https://github.com/Djdragon44/physbone-scene-handles/releases/latest), or
copy `Packages/com.opensource.physbonehandles` from this repo straight into your project's
`Packages/` folder. Requires the VRChat SDK3 Avatars package (`com.vrchat.base`) — everything
here compiles out (`#if PBHANDLES_VRCSDK_PRESENT`) if it isn't installed.

## Status

Collider/PhysBone scene handles and the Auto Collider Generator have both been used and
iterated on in a real project. One thing that's still unverified either way:

- `PhysBoneRadiusSceneHandles.cs`'s wireframe chain-walk assumes a simple single-child bone
  chain (a finger, a ponytail); branching chains (hair with multiple strands off one root)
  just stop drawing at the branch point, which is probably fine but hasn't been checked.
- Non-uniform scale on a bone — everything uses `lossyScale.x` as a uniform scale factor,
  which will be wrong if a bone is scaled non-uniformly.

Report anything that doesn't feel right and it'll get fixed.

## License

MIT — see `LICENSE`. Do whatever you want with it.
