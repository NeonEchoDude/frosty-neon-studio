# VRChat versions and rules

The toolkit is built and tested against these versions. They were checked against VRChat's official Creator documentation in November 2026. Versions change, so check the linked pages before you upgrade anything.

| What | Version |
|---|---|
| Unity | 2022.3.22f1 |
| VRChat SDK (Base and Avatars) | 3.10.6 |
| Blender (for `.blend` files) | 5.2 |
| UniVRM (for `.vrm` files) | 0.131.4, VRM 0.x |

## Sources

- [Current Unity Version](https://creators.vrchat.com/sdk/upgrade/current-unity-version/): Unity 2022.3.22f1 is the documented editor. VRChat recommends staying on it and not upgrading to Unity 6 for VRChat content.
- [VRChat SDK Releases](https://creators.vrchat.com/releases/): SDK 3.10.6, which remains on Unity 2022.3.
- [Getting Started](https://creators.vrchat.com/sdk/): VRChat recommends Creator Companion for setting up Unity and the SDK. Quest builds need Android Build Support.
- [Avatar Performance Ranking System](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/): the PC and mobile limits for triangles, meshes, material slots and bones are copied into `tools/blender/data/performance_budgets_v1.json`. Mobile limits apply to Android, iOS and Meta Quest. On mobile, the default minimum displayed rank is Medium, so Poor and Very Poor avatars are hidden from other players by default. VRChat calls the rank a broad signal; the SDK build and testing on a device have the final word.
- [PhysBones](https://creators.vrchat.com/common-components/physbones/): mobile has hard limits, including at most 256 transforms affected by one PhysBone. The toolkit does not check PhysBones yet.
- [Rig Requirements](https://creators.vrchat.com/avatars/rig-requirements/): VRChat marks this page as significantly out of date but mostly accurate. A humanoid avatar must have the head, hands and feet mapped; the pelvis, spine, chest, neck and shoulders must be mapped; the shoulders and neck must be direct children of the chest; and the hips must be the ancestor of all humanoid bones.

## Where the toolkit follows the SDK rather than the web page

On the shoulder and neck rule, the Rig Requirements page is out of date. SDK 3.10.6 expects the shoulders and neck under the UpperChest when one is mapped, and under the Chest otherwise. Where the page and the installed SDK disagree, the toolkit follows the SDK. See [Measured in Unity](UNITY_VALIDATION.md#vrchat-sdk-3106).

The page also says that Blender exports should have "a rest X rotation of 90 degrees". The toolkit's FBX profile produces a -90° X rotation on the imported `Armature`, with none on the model root. Whether that is what the page means has not been established.

## If the SDK changes

The check repeats some of the SDK's private upload checks, written against SDK 3.10.6. With a different SDK version, `frosty.ps1 doctor` shows the version, and the check adds the warning `sdk_version_differs`. See [Avatar preflight](AVATAR_PREFLIGHT.md#limits) for what to compare after an update.
