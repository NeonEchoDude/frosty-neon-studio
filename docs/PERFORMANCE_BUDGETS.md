# Blender scene audit: performance budgets

The Blender scene audit (`tools/blender/audit_avatar_scene.py`, or `frosty.ps1 audit <file.blend>`) also estimates performance. While the avatar is still in Blender, it estimates which VRChat performance rank its geometry would get on PC and on Quest, and say which numbers push it past the rank you are aiming for. PC and Quest are judged separately; passing one says nothing about the other.

It is an estimate from five counts. It changes nothing in the scene, and the VRChat SDK build panel remains the real answer.

- Tool: `tools/blender/audit_avatar_scene.py` (the `performance` section of its JSON report)
- Budget table: `tools/blender/data/performance_budgets_v1.json`
- Verified in: Blender 5.2.3 LTS (self-test) and against VRChat SDK 3.10.6 in Unity 2022.3.22f1: the limits equal the SDK's own, and the five counts equal the SDK's counts for both test fixtures (see [Measured in Unity](UNITY_VALIDATION.md)).

## Commands

```powershell
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'

# Both platforms, default targets (PC: Good, Quest: Medium)
& $blender --background path\to\avatar.blend --python tools/blender/audit_avatar_scene.py -- --json-out avatar.audit.json

# A PC-only source file: do not judge it against Quest
& $blender --background path\to\avatar.blend --python tools/blender/audit_avatar_scene.py -- --platforms pc

# Stricter targets
& $blender --background path\to\avatar.blend --python tools/blender/audit_avatar_scene.py -- --target-rank pc=Excellent --target-rank quest=Good
```

`--budgets other.json` uses a different table. An unknown platform or rank stops the audit with exit code 1.

## What is counted

| Statistic | How the audit counts it in Blender |
|---|---|
| `triangles` | Triangles of all mesh objects as authored, minus zero-area triangles. Modifiers are not applied, matching the export profile. |
| `skinned_meshes` | Mesh objects with an Armature modifier or shape keys. |
| `basic_meshes` | All other mesh objects. |
| `material_slots` | Per mesh, the slots used by at least one face. An empty, unused slot is not counted. |
| `bones` | Bones of every armature in the scene. |

Everything in the scene is counted, including hidden objects. VRChat also counts disabled objects.

Zero-area triangles are triangles whose corners do not span any area, typically a quad with two corners at the same position. On the starter avatar `FrostyNeonBot` Blender counted 3,696 triangles, 144 of them zero-area, and the VRChat SDK counted 3,552 for the imported model, so they are left out of the estimate. The report still gives the authored total (`triangle_count`), the number excluded (`zero_area_triangle_count`, and `zero_area_triangles` per mesh), and an info-level `zero_area_triangles` note. That Unity drops them is inferred from that one exact match.

## What the report contains

`performance.measured` holds the five counts. For each platform, `performance.platforms.<name>` holds:

| Field | Meaning |
|---|---|
| `estimated_rank` | The worst rank among the five statistics |
| `target_rank` | The rank being aimed for |
| `meets_target` | Whether every statistic is within the target rank's maximum |
| `statistics` | Per statistic: the value, its own rank, and the maximum for the target rank |

When a platform misses its target, the audit adds a `performance_below_target` warning naming the platform and each statistic over budget. `performance.not_estimated` lists the VRChat statistics the audit cannot see.

## Limits used

Copied from VRChat's [Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/) page as it stood in November 2026 (last updated by VRChat on 2026-10-21). Values are the maximum for each rank; above the Poor maximum is Very Poor.

PC:

| Statistic | Excellent | Good | Medium | Poor |
|---|---|---|---|---|
| Triangles | 32,000 | 70,000 | 70,000 | 70,000 |
| Skinned Meshes | 1 | 2 | 8 | 16 |
| Basic Meshes | 4 | 8 | 16 | 24 |
| Material Slots | 4 | 8 | 16 | 32 |
| Bones | 75 | 150 | 256 | 400 |

Quest (VRChat's "Mobile Limits", which cover Android, iOS and Meta Quest):

| Statistic | Excellent | Good | Medium | Poor |
|---|---|---|---|---|
| Triangles | 7,500 | 10,000 | 15,000 | 20,000 |
| Skinned Meshes | 1 | 1 | 2 | 2 |
| Basic Meshes | 1 | 1 | 2 | 2 |
| Material Slots | 1 | 1 | 2 | 4 |
| Bones | 75 | 90 | 150 | 150 |

VRChat changes these numbers. Before relying on them, re-read the page, update the table, and bump `table_version`.

## Default targets

- PC: **Good**. The page says to aim for Good, and that Medium is fine if Good is out of reach.
- Quest: **Medium**. On mobile the default Minimum Displayed Performance Rank is Medium, so Poor and Very Poor avatars are hidden from other users by default.

These are starting points, not rules. Change them per run with `--target-rank`, or permanently in the table.

## Limits of the estimate

- Not estimated: texture memory, bounds size, PhysBones, contacts, constraints, animators, lights, particles, trail and line renderers, raycasts, cloth, physics components, audio sources and face tracking. Most of these only exist once the avatar is set up in Unity. An avatar can meet every budget here and still rank lower.
- The five counts matched the SDK's own counts on the two generated fixtures (one skinned mesh, one material each). Avatars with several meshes, rigid props, or multiple and unused material slots have not been compared.
- The bone count is every bone in the armature. It matched the SDK on the fixtures, where every bone deforms the mesh; an armature with bones that do not deform anything may be counted differently by the SDK.
- VRChat itself describes the rank as a broad static signal, not the final word on performance.

## Tested (Blender 5.2.3)

The self-test's budget checks:

- The minimal fixture ranks Excellent on both platforms, and 12 rank boundaries (for example 32,000 vs 32,001 triangles on PC, 7,500 vs 7,501 on Quest) give the expected rank.
- A generated avatar with exactly 7,501 triangles, 2 used and 1 unused material slot and 91 bones is counted exactly, ranks Good on PC and Medium on Quest, and produces no finding.
- The same avatar against stricter targets produces one finding per platform naming the right statistics; `--platforms pc` limits the report to PC.
- A generated avatar with 20,001 triangles, 5 material slots and 3 rigid meshes is on target for PC and Very Poor on Quest, reported as a Quest-only finding.
- An unknown platform or rank is refused.

### FrostyNeonBot

- FrostyNeonBot has 3,696 authored triangles; 144 have zero area. The audit reports all three counts and estimates ranks from 3,552 triangles. The VRChat SDK measured 3,552 triangles after Unity import. This exact match suggests Unity removes the zero-area triangles; that behavior was not directly observed.
- The updated audit report estimates Excellent on PC and Medium on Quest. The SDK gives those same overall ranks; the Quest result applies mobile limits to a Windows import, not an Android build.

On a saved copy of the exporter fixture, `--platforms pc --target-rank pc=Excellent` exited 0 and reported 14 triangles, 1 skinned mesh, 1 material slot, 4 bones, rank Excellent; `--target-rank quest=Great` stopped with exit 1; the `.blend` was byte-identical afterwards.
