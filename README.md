# unity-mcp-gc2-tools

> A custom [MCPForUnity](https://github.com/unity-mcp/mcp-for-unity) editor tool that bridges the generic Unity MCP server with [Game Creator 2](https://www.gamecreator.io/) (GC2) by Catsoft Works.

## What It Does

GC2Tools lets AI agents (Gemini, Claude, Cursor, etc.) perform GC2-specific operations programmatically that would otherwise require manual Unity Inspector work:

- Wire FBX models as GC2 `Character` mannequins (correct `SerializeReference` chain)
- Assign `AnimationClip` sub-assets to `Skill` ScriptableObjects
- **Build entire `Combos` trees** (with full combo chains) from JSON — no node graph clicking required
- Link `Combos` assets to `MeleeWeapon` assets
- Configure `Character` as player-controlled
- Inspect & debug GC2 asset state (`Skill`, `MeleeWeapon`, `Character`)
- List animation clips inside FBX files
- Remove children, configure Animators, full character setup in one call

## Requirements

- Unity 6.x (tested on 6000.0.x)
- [MCPForUnity](https://github.com/unity-mcp/mcp-for-unity) installed in your project
- Game Creator 2 Core + Melee 2 module installed

## Installation

1. Copy `GC2Tools.cs` into your project under `Assets/YourProject/Scripts/Editor/`
2. Unity will compile it automatically
3. After domain reload, the `gc2_tools` MCP tool is registered and available to AI agents

> ⚠️ **CRITICAL**: GC2 characters MUST be created via Unity menu (`Game Creator → Characters → Player`), NOT by manual instantiation. Manual setup breaks `[SerializeReference]` chains silently. See the Character Creation section below.

## Usage

Call via MCPForUnity's `execute_custom_tool`:

```json
{
  "tool_name": "gc2_tools",
  "parameters": {
    "action": "<action_name>",
    ...action-specific params...
  }
}
```

## Available Actions

| Action | Description |
|--------|-------------|
| `setup_mannequin` | Instantiate FBX as Character mannequin child, wire `m_Kernel.m_Animim` refs |
| `set_player` | Set `m_IsPlayer` on Character component |
| `setup_character` | Mannequin + player + CharacterController in one call |
| `assign_animation` | Assign FBX AnimationClip → Skill SO |
| `setup_combos` | Build full ComboTree from JSON node definition |
| `assign_combos_to_weapon` | Link Combos asset → MeleeWeapon |
| `get_skill_info` | Read Skill SO state (animation, transitions, gravity) |
| `get_weapon_info` | Read MeleeWeapon state (combos, shield) |
| `list_fbx_clips` | Enumerate AnimationClips inside an FBX |
| `instantiate_model` | Place FBX in active scene |
| `inspect_character` | Deep inspect: mannequin, animator, avatar, player flag, children |
| `remove_child` | Remove named child (or all children) from a GameObject |
| `configure_animator` | Set Avatar on an Animator from an FBX |

### `setup_combos` — Example

Build a 3-hit combo chain (A→A→A):

```json
{
  "action": "setup_combos",
  "combos_path": "Assets/Combat/Combos/Combos_MMA.asset",
  "nodes": [
    {
      "key": "A",
      "skill_path": "Assets/Combat/Skills/Skill_LeftHook.asset",
      "children": [
        {
          "key": "A",
          "skill_path": "Assets/Combat/Skills/Skill_RightHook.asset",
          "children": [
            { "key": "A", "skill_path": "Assets/Combat/Skills/Skill_HighKick.asset" }
          ]
        }
      ]
    }
  ]
}
```

### `assign_animation` — Batch Example (swap Brawl template anims)

```json
{ "action": "assign_animation", "skill_path": "Assets/.../Brawl@Combo1.asset", "fbx_path": "Assets/.../Fighter@Attack_3Combo_A_1.FBX" }
{ "action": "assign_animation", "skill_path": "Assets/.../Brawl@Kick.asset", "fbx_path": "Assets/.../kick_01_inplace.fbx" }
```

### `inspect_character` — Example

```json
{ "action": "inspect_character", "target": "Player" }
```

Returns: mannequin status, animator status, avatar name, player flag, CharacterController presence, all children with their components.

## GC2 Character Creation — Critical Rules

> **NEVER** instantiate a 3D model and try to manually attach GC2 `Character` components.
> This breaks the `[SerializeReference]` chain (`Character → m_Kernel → m_Animim → m_Mannequin/m_Animator`).

The correct procedure:
1. Use Unity menu: `Game Creator → Characters → Player` (or NPC)
2. Drag the 3D model prefab onto the **Animation Drop Zone** in the Character inspector
3. Ensure the Animator uses `CompleteLocomotion.controller`

The `setup_mannequin` tool automates step 2 via `SerializedObject` property manipulation, but if a character is deeply broken, delete it and recreate via the menu.

## Recommended Workflow: Brawl Template

Instead of building from scratch, use GC2's built-in **Brawl template**:
1. Install: `Game Creator → Install... → Brawl`
2. Open the Brawl example scene
3. Use `get_skill_info` to catalog existing Skills
4. Use `assign_animation` to swap in custom animation clips
5. Use `setup_combos` to modify combo trees

## GC2 Internals Reference

| Type | Key Serialized Fields |
|------|-----------------------|
| `Character` | `m_Kernel: CharacterKernel [SerializeReference]`, `m_IsPlayer` |
| `CharacterKernel` | `m_Animim: TUnitAnimim [SerializeReference]` |
| `TUnitAnimim` | `m_Mannequin: Transform`, `m_Animator: Animator` (access via `m_Kernel.m_Animim.*`) |
| `Combos` | `[SerializeReference] ComboTree m_Combos` |
| `ComboTree` | `m_Data` (TTreeData), `m_Nodes` (TreeNodes), `m_Roots` (List<int>) |
| `ComboItem` | `m_Key` (MeleeKey), `m_Mode` (MeleeMode), `m_When` (MeleeExecute), `m_Skill` (Skill) |
| `Skill` | `m_Animation` (AnimationClip), `m_TransitionIn/Out` (float) |
| `MeleeWeapon` | `m_Combos` (Combos), `m_Shield` (Shield) |

## License

MIT
