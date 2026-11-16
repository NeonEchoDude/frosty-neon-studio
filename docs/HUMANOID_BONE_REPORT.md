# Blender scene audit: humanoid bones

The Blender scene audit (`tools/blender/audit_avatar_scene.py`, or `frosty.ps1 audit <file.blend>`) checks an avatar before it leaves Blender. This page covers its humanoid bone report. Its performance estimate is described in [Performance budgets](PERFORMANCE_BUDGETS.md).

The bone report shows which of its bones look like Unity humanoid slots, which needed slots have no recognisable bone, and whether the parenting matches what VRChat's rig requirements describe. It is a report only. No bone is renamed, moved or reparented.

- Tool: `tools/blender/audit_avatar_scene.py` (the `humanoid_bones` section of its JSON report)
- Name table: `tools/blender/data/humanoid_bones_v1.json`
- Verified in: Blender 5.2.3 LTS (self-test). The required-slot list is verified against Unity 2022.3.22f1. On the generated 21-bone rig, Unity's automatic mapping agreed with the audit on 20 slots and left Chest empty (see [Measured in Unity](UNITY_VALIDATION.md)).

## Command

```powershell
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
& $blender --background --factory-startup --python tools/blender/audit_avatar_scene.py -- --self-test
& $blender --background path\to\avatar.blend --python tools/blender/audit_avatar_scene.py -- --json-out avatar.audit.json
```

`--bone-table other.json` uses a different name table. Exit code 1 means an audit error or a failed self-test; warnings alone exit 0.

## What the report contains

For each armature in the scene:

| Field | Meaning |
|---|---|
| `matched` | Unity slot -> the bone recognised for it |
| `ambiguous` | Slots where several bones fit equally; none is chosen |
| `unity_required_status` | Where the Unity required-slot list comes from; currently "verified in Unity 2022.3.22f1" |
| `missing_unity_required` | Slots Unity needs for a humanoid that have no recognised bone |
| `missing_vrchat_required` | Slots VRChat's rig requirements ask for that have no recognised bone |
| `unmatched_bones` | Bones not used for any slot (hair, tail, twist bones, or names outside the table) |

Findings are added to the audit's `issues` list:

| Code | Level | Meaning |
|---|---|---|
| `no_humanoid_names` | info | Nothing matched. Normal for a non-humanoid rig. |
| `humanoid_missing_unity_required` | warning | A slot Unity requires has no recognised bone. The message states the list's verification status. |
| `humanoid_missing_vrchat_required` | warning | Chest, neck or a shoulder has no recognised bone. |
| `humanoid_ambiguous_names` | warning | Two or more bones fit one slot. |
| `humanoid_split_hierarchy` | warning | The hips bone is not an ancestor of every matched bone. |
| `humanoid_unexpected_parent` | warning | A matched bone is not below the bone it should hang from (for example a hand not below its lower arm). |
| `humanoid_chest_children` | warning | A shoulder or the neck is not a direct child of the bone matched to UpperChest when one is matched, or to Chest otherwise. This is the rule the VRChat SDK 3.10.6 enforces as an error. |
| `humanoid_extra_limb_children` | info | An arm or leg bone has more than one child; VRChat's IK uses the first child, so check the order in Unity. |

`info` entries do not change the audit status. Warnings are deliberately not errors: a creature or stylised avatar may be non-humanoid on purpose.

## How names are matched

1. A side is read from the name: the word `left`/`right` anywhere, or `L`/`R` as a prefix or suffix separated by `.`, `_`, `-` or a space. Anything before a `:` (such as `mixamorig:`) is dropped.
2. The rest is lower-cased and reduced to letters and digits, then compared with each slot's list of spellings in the table.
3. Slots are tried in table order and spellings in listed order, and each bone is used once. That is how `LeftUpLeg` + `LeftLeg` (Mixamo) and `Left leg` + `Left knee` (English MMD-style) both resolve correctly even though "leg" means a different bone in each.

Spellings covered by the self-test: Unity slot names, Blender `.L`/`.R` names (`upper_arm.L`, `forearm.L`, `thigh.L`, `shin.L`), Mixamo, and English MMD-style names. To support another convention, add its spellings to the table and a rig to the self-test.

Not recognised: `UpperArmL`-style suffixes without a separator, numbered duplicates such as `Head.001`, Japanese names, and Rigify's `DEF-`/`ORG-` bones.

## Where the rules come from

- Slot names: Unity 2022.3's `HumanBodyBones` scripting reference.
- VRChat rules: [Rig Requirements](https://creators.vrchat.com/avatars/rig-requirements/), as read in November 2026. VRChat marks that page as significantly out of date but mostly accurate. Taken from it: head, hands and feet must be mapped; pelvis, spine, chest, neck and shoulders must be mapped; shoulders and neck are direct children of the chest; hips is the ancestor of all humanoid bones; lower arm/leg should be the first child of upper arm/leg.
- Unity's 15 required bones: verified in the Unity 2022.3.22f1 Editor. `HumanTrait.RequiredBone` returned exactly the set in the table, and Unity's total of 55 humanoid bones matches the table's slots (see [Measured in Unity](UNITY_VALIDATION.md)). The audit self-test asserts this set. Re-check if VRChat moves to another Unity version.

The chest rule follows the installed SDK, not the web page. VRChat SDK 3.10.6 (`VRCSdkControlPanelAvatarBuilder.AnalyzeIK`) requires both shoulders and the neck to be direct children of UpperChest when UpperChest is mapped, and of Chest otherwise; its message reads "Make sure that the parent of both Shoulders and the Neck is the Chest (or UpperChest if set)." The Rig Requirements web page names only Chest and is marked out of date. The same SDK method also makes Neck, both Shoulders, Hips, Spine and Chest mandatory, which matches the table's VRChat-required slots.

## Limits

- Matching is by name only. Unity's Avatar configuration also uses the hierarchy and can map bones this report misses, or map them differently. Measured example: on a rig with a correctly parented bone named `Chest`, the audit matched it and Unity 2022.3.22f1's automatic mapping left the Chest slot empty. After importing as Humanoid, check the Chest slot in Unity's Avatar Configure window.
- Bone orientation, roll, T-pose and proportions are not checked.
- "First child" order is not checked, because it is decided by the order in the imported file, not by Blender's bone list.
- A clean report is not a VRChat SDK validation.

## Tested (Blender 5.2.3)

The self-test's bone-report checks cover: side/name parsing; four 21-bone rigs (Unity, Blender, Mixamo, English MMD-style names) each matched to the right slots with no findings and names unchanged; finger bones in three spellings; a deliberately faulty rig producing the six expected finding codes; the required-slot list equal to Unity's 15 required bones; a rig with UpperChest mapped and shoulders and neck under it producing no finding; a rig with UpperChest mapped and a parentless neck reporting it; a rig with UpperChest mapped and shoulders and neck left under Chest reporting all three; duplicate names reported as ambiguous; the original minimal fixture still passing.

On a saved copy of the FBX exporter's four-bone fixture, the audit exited 0 with status `warn`, matched Hips, Spine, Head and LeftUpperArm (`LeftArm`), listed the 11 missing Unity-required and 4 missing VRChat-required slots, and left the `.blend` byte-identical.
