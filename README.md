# PhysBone Scene Handles

Free, open-source batch scene-view handles for editing VRChat `VRCPhysBoneCollider` and
`VRCPhysBone` components across multiple selected objects at once — drag one handle, apply
the change to a whole selection (Alt = just this one, Shift = equalize). No license key, no
phone-home license/HWID check, no OS check. Cross-platform.

The actual package lives in [`Packages/com.opensource.physbonehandles`](Packages/com.opensource.physbonehandles) —
see its README for what it does and how to use it.

## Installing

**Via VCC / ALCOM (VPM):** Add this repository's listing URL —
`https://djdragon44.github.io/physbone-scene-handles/index.json` — under Settings > Packages >
Add Repository, then add "PhysBone Scene Handles" to your project from the package list.

**Manually:** Download the `.zip` or `.unitypackage` from the
[latest release](https://github.com/Djdragon44/physbone-scene-handles/releases/latest) and
import/extract it into your project.

## Contributing / publishing new versions

This repo is built from VRChat's official
[template-package](https://github.com/vrchat-community/template-package) automation. To ship
a new version: bump `version` in `Packages/com.opensource.physbonehandles/package.json`,
push to `main`, then manually run the **Build Release** action from the Actions tab. That
creates a GitHub Release (zip + `.unitypackage`) tagged with the new version, which in turn
triggers **Build Repo Listing** to regenerate the VPM listing published on GitHub Pages.

See VRChat's [Create a Package Listing](https://vcc.docs.vrchat.com/guides/create-listing/)
guide for the full picture of how this automation works.

## License

MIT — see [`Packages/com.opensource.physbonehandles/LICENSE`](Packages/com.opensource.physbonehandles/LICENSE).
