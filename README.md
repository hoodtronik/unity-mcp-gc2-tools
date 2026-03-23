# unity-mcp-gc2-tools

> A custom [MCPForUnity](https://github.com/unity-mcp/mcp-for-unity) editor tool that bridges the generic Unity MCP server with [Game Creator 2](https://www.gamecreator.io/) (GC2) by Catsoft Works.

## What It Does

GC2Tools lets AI agents (Gemini, Claude, Cursor, etc.) perform GC2-specific operations programmatically that would otherwise require manual Unity Inspector work:

- Wire FBX models as GC2 `Character` mannequins
- Assign `AnimationClip` sub-assets to `Skill` ScriptableObjects
- **Build entire `Combos` trees** (with full combo chains) from JSON — no node graph clicking required
- Link `Combos` assets to `MeleeWeapon` assets
- Configure `Character` as player-controlled
- Inspect GC2 asset state (`Skill`, `MeleeWeapon`)
- List animation clips inside FBX files

## Requirements

- Unity 6.x (tested on 6000.0.x)
- [MCPForUnity](https://github.com/unity-mcp/mcp-for-unity) installed in your project
- Game Creator 2 Core + Melee 2 module installed

## Installation

1. Copy `GC2Tools.cs` into your project under `Assets/YourProject/Scripts/Editor/`
2. Unity will compile it automatically
3. After domain reload, the `gc2_tools` MCP tool is registered and available to AI agents

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
| `setup_mannequin` | Instantiate FBX as Character mannequin child |
| `set_player` | Set `m_IsPlayer` on Character component |
| `setup_character` | Mannequin + player + CharacterController in one call |
| `assign_animation` | Assign FBX AnimationClip → Skill SO |
| `setup_combos` | Build full ComboTree from JSON node definition |
| `assign_combos_to_weapon` | Link Combos asset → MeleeWeapon |
| `get_skill_info` | Read Skill SO state (animation, transitions) |
| `get_weapon_info` | Read MeleeWeapon state |
| `list_fbx_clips` | Enumerate AnimationClips inside an FBX |
| `instantiate_model` | Place FBX in active scene |

### `setup_combos` — Example

Build a 3-hit MMA combo chain (A→A→A):

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

Build a TKD tree with two root nodes (A chain + B standalone):

```json
{
  "action": "setup_combos",
  "combos_path": "Assets/Combat/Combos/Combos_TKD.asset",
  "nodes": [
    {
      "key": "A",
      "skill_path": "Assets/Combat/Skills/Skill_FrontKick.asset",
      "children": [
        { "key": "A", "skill_path": "Assets/Combat/Skills/Skill_Roundhouse.asset" }
      ]
    },
    { "key": "B", "skill_path": "Assets/Combat/Skills/Skill_SpinKick.asset" }
  ]
}
```

## How `setup_combos` Works Internally

GC2's `Combos` asset stores its tree in a `ComboTree` (which extends `TSerializableTree<ComboItem>`). The internal fields are private and not exposed via a clean editor API.

This tool:
1. Clears existing nodes via reflection on `m_Data`, `m_Nodes`, `m_Roots`
2. Uses GC2's **public** `ComboTree.AddToRoot()` and `AddChild()` methods to build the tree
3. Sets private `ComboItem` fields (`m_Key`, `m_Mode`, `m_When`, `m_Skill`) via reflection
4. Marks the asset dirty and saves it

## Extending

Add new actions in `GC2Tools.cs`:

```csharp
// 1. Add to the switch in HandleCommand()
"my_new_action" => MyNewAction(@params),

// 2. Write the method
private static object MyNewAction(JObject @params)
{
    // ... your GC2 logic ...
    return new SuccessResponse("Done!", new JObject { ["result"] = "..." });
    // or on failure:
    return new ErrorResponse("Something went wrong");
}
```

## GC2 Internals Reference

| Type | Key Serialized Fields |
|------|-----------------------|
| `Combos` | `[SerializeReference] ComboTree m_Combos` |
| `ComboTree` | `m_Data` (TTreeData), `m_Nodes` (TreeNodes), `m_Roots` (List<int>) |
| `ComboItem` | `m_Key` (MeleeKey), `m_Mode` (MeleeMode), `m_When` (MeleeExecute), `m_Skill` (Skill) |
| `Skill` | `m_Animation` (AnimationClip), `m_TransitionIn/Out` (float) |
| `MeleeWeapon` | `m_Combos` (Combos), `m_Shield` (Shield) |
| `Character` | `m_Animim.m_Mannequin` (Animator), `m_IsPlayer` (bool) |

## License

MIT
