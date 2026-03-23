using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Melee;
using GameCreator.Runtime.Common;
using Object = UnityEngine.Object;

namespace ARBrawler.Editor.MCPTools
{
    /// <summary>
    /// Custom MCP tool for Game Creator 2 operations that the generic MCP tools can't handle.
    /// This gives the AI agent direct access to GC2-specific operations like:
    /// - Setting up Character mannequins from FBX models
    /// - Configuring Character as player-controlled
    /// - Assigning animation clips from FBX sub-assets to Skill SOs  
    /// - Reading Skill/Weapon/Combo state for verification
    /// - Instantiating FBX models as scene children
    /// </summary>
    [McpForUnityTool("gc2_tools", Description = "Game Creator 2 operations: setup characters, assign animations, configure combat")]
    public static class GC2Tools
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("Required parameter 'action' is missing.");

            try
            {
                return action switch
                {
                    "setup_character" => SetupCharacter(@params),
                    "set_player" => SetPlayer(@params),
                    "assign_animation" => AssignAnimation(@params),
                    "instantiate_model" => InstantiateModel(@params),
                    "get_skill_info" => GetSkillInfo(@params),
                    "get_weapon_info" => GetWeaponInfo(@params),
                    "list_fbx_clips" => ListFbxClips(@params),
                    "setup_mannequin" => SetupMannequin(@params),
                    "setup_combos" => SetupCombos(@params),
                    "assign_combos_to_weapon" => AssignCombosToWeapon(@params),
                    "inspect_character" => InspectCharacter(@params),
                    "remove_child" => RemoveChild(@params),
                    "configure_animator" => ConfigureAnimator(@params),
                    _ => new ErrorResponse($"Unknown action: {action}")
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error in gc2_tools/{action}: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Instantiate an FBX model as a child of a target GameObject and configure it as the GC2 mannequin.
        /// params: target (instanceID or name), fbx_path (asset path to FBX), set_as_mannequin (bool)
        /// </summary>
        private static object SetupMannequin(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            string fbxPath = @params["fbx_path"]?.ToString();
            
            if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(fbxPath))
                return new ErrorResponse("Required: 'target', 'fbx_path'");

            // Find target GameObject
            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            // Load FBX
            GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxPrefab == null)
                return new ErrorResponse($"FBX not found at: {fbxPath}");

            // Check if there's already a mannequin child - remove it
            for (int i = target.transform.childCount - 1; i >= 0; i--)
            {
                var child = target.transform.GetChild(i);
                if (child.GetComponent<Animator>() != null)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            // Instantiate as child
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab, target.transform);
            instance.name = "Mannequin";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            
            Undo.RegisterCreatedObjectUndo(instance, "Setup Mannequin");

            // Get or add Animator
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();

            // Auto-detect and assign Avatar from the FBX
            Avatar avatar = null;
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (Object sub in subAssets)
            {
                if (sub is Avatar av)
                {
                    avatar = av;
                    break;
                }
            }
            if (avatar != null && animator != null)
            {
                animator.avatar = avatar;
            }

            // Set model as mannequin on the Character component
            // Path: Character.m_Kernel (SerializeReference) -> CharacterKernel.m_Animim (SerializeReference) -> TUnitAnimim.m_Mannequin/m_Animator
            Character character = target.GetComponent<Character>();
            bool mannequinSet = false;
            bool animatorSet = false;
            if (character != null)
            {
                SerializedObject so = new SerializedObject(character);
                
                // Correct path through the SerializeReference chain
                var mannequinProp = so.FindProperty("m_Kernel.m_Animim.m_Mannequin");
                var animatorProp = so.FindProperty("m_Kernel.m_Animim.m_Animator");
                
                if (mannequinProp != null)
                {
                    mannequinProp.objectReferenceValue = instance.transform;
                    mannequinSet = true;
                }
                else
                {
                    Debug.LogWarning("[gc2_tools] Could not find m_Kernel.m_Animim.m_Mannequin property");
                }
                
                if (animatorProp != null)
                {
                    animatorProp.objectReferenceValue = animator;
                    animatorSet = true;
                }
                else
                {
                    Debug.LogWarning("[gc2_tools] Could not find m_Kernel.m_Animim.m_Animator property");
                }
                
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            return new SuccessResponse($"Mannequin '{fbxPrefab.name}' set up on '{target.name}'. Mannequin assigned: {mannequinSet}, Animator assigned: {animatorSet}, Avatar: {(avatar != null ? avatar.name : "none")}",
                new JObject
                {
                    ["target"] = target.name,
                    ["mannequin"] = instance.name,
                    ["hasAnimator"] = animator != null,
                    ["mannequinAssigned"] = mannequinSet,
                    ["animatorAssigned"] = animatorSet,
                    ["avatar"] = avatar != null ? avatar.name : "none",
                    ["instanceID"] = instance.GetInstanceID()
                });
        }

        /// <summary>
        /// Set a Character as player-controlled.
        /// params: target (instanceID or name), is_player (bool)
        /// </summary>
        private static object SetPlayer(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            bool isPlayer = @params["is_player"]?.Value<bool>() ?? true;

            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            Character character = target.GetComponent<Character>();
            if (character == null)
                return new ErrorResponse($"No Character component on: {targetName}");

            SerializedObject so = new SerializedObject(character);
            var prop = so.FindProperty("m_IsPlayer");
            if (prop != null)
            {
                prop.boolValue = isPlayer;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            return new SuccessResponse($"Set IsPlayer={isPlayer} on '{target.name}'");
        }

        /// <summary>
        /// Full character setup: instantiate model, configure as player, add CharacterController.
        /// params: target, fbx_path, is_player
        /// </summary>
        private static object SetupCharacter(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            string fbxPath = @params["fbx_path"]?.ToString();
            bool isPlayer = @params["is_player"]?.Value<bool>() ?? false;

            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("Required: 'target'");

            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            var results = new JArray();

            // Setup mannequin if fbx_path provided
            if (!string.IsNullOrEmpty(fbxPath))
            {
                var mannequinResult = SetupMannequin(@params);
                results.Add(JObject.FromObject(new { step = "mannequin", result = mannequinResult.ToString() }));
            }

            // Set player flag
            if (isPlayer)
            {
                var playerResult = SetPlayer(@params);
                results.Add(JObject.FromObject(new { step = "set_player", result = playerResult.ToString() }));
            }

            // Ensure CharacterController exists
            if (target.GetComponent<CharacterController>() == null)
            {
                Undo.AddComponent<CharacterController>(target);
                results.Add(JObject.FromObject(new { step = "add_character_controller", result = "added" }));
            }

            EditorUtility.SetDirty(target);
            
            return new SuccessResponse($"Character '{target.name}' setup complete.",
                new JObject { ["steps"] = results });
        }

        /// <summary>
        /// Assign an AnimationClip from an FBX to a Skill's m_Animation field.
        /// params: skill_path (asset path), fbx_path (asset path), clip_name (optional, uses first clip if omitted)
        /// </summary>
        private static object AssignAnimation(JObject @params)
        {
            string skillPath = @params["skill_path"]?.ToString();
            string fbxPath = @params["fbx_path"]?.ToString();
            string clipName = @params["clip_name"]?.ToString();

            if (string.IsNullOrEmpty(skillPath) || string.IsNullOrEmpty(fbxPath))
                return new ErrorResponse("Required: 'skill_path', 'fbx_path'");

            Skill skill = AssetDatabase.LoadAssetAtPath<Skill>(skillPath);
            if (skill == null)
                return new ErrorResponse($"Skill not found: {skillPath}");

            // Load all sub-assets from FBX
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            AnimationClip clip = null;

            foreach (Object sub in subAssets)
            {
                if (sub is AnimationClip ac && !ac.name.StartsWith("__preview__"))
                {
                    if (string.IsNullOrEmpty(clipName) || ac.name == clipName)
                    {
                        clip = ac;
                        break;
                    }
                }
            }

            if (clip == null)
                return new ErrorResponse($"No AnimationClip found in '{fbxPath}'" + 
                    (string.IsNullOrEmpty(clipName) ? "" : $" with name '{clipName}'"));

            SerializedObject so = new SerializedObject(skill);
            var animProp = so.FindProperty("m_Animation");
            if (animProp == null)
                return new ErrorResponse($"m_Animation property not found on Skill");

            animProp.objectReferenceValue = clip;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return new SuccessResponse($"Assigned '{clip.name}' ({clip.length:F2}s) → {skill.name}",
                new JObject
                {
                    ["skill"] = skill.name,
                    ["clip"] = clip.name,
                    ["duration"] = clip.length,
                    ["frameRate"] = clip.frameRate,
                    ["isLooping"] = clip.isLooping
                });
        }

        /// <summary>
        /// Instantiate an FBX model into the scene.
        /// params: fbx_path, position [x,y,z], parent (optional instanceID or name)
        /// </summary>
        private static object InstantiateModel(JObject @params)
        {
            string fbxPath = @params["fbx_path"]?.ToString();
            
            if (string.IsNullOrEmpty(fbxPath))
                return new ErrorResponse("Required: 'fbx_path'");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (prefab == null)
                return new ErrorResponse($"Asset not found: {fbxPath}");

            Transform parent = null;
            string parentName = @params["parent"]?.ToString();
            if (!string.IsNullOrEmpty(parentName))
            {
                GameObject parentGo = FindGameObject(parentName);
                if (parentGo != null) parent = parentGo.transform;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);

            var posArr = @params["position"] as JArray;
            if (posArr != null && posArr.Count == 3)
            {
                instance.transform.localPosition = new Vector3(
                    posArr[0].Value<float>(),
                    posArr[1].Value<float>(),
                    posArr[2].Value<float>()
                );
            }

            Undo.RegisterCreatedObjectUndo(instance, "Instantiate Model");

            return new SuccessResponse($"Instantiated '{prefab.name}' in scene.",
                new JObject
                {
                    ["name"] = instance.name,
                    ["instanceID"] = instance.GetInstanceID()
                });
        }

        /// <summary>
        /// Get detailed info about a Skill SO including its animation, power, phases.
        /// params: skill_path
        /// </summary>
        private static object GetSkillInfo(JObject @params)
        {
            string path = @params["skill_path"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("Required: 'skill_path' or 'path'");

            Skill skill = AssetDatabase.LoadAssetAtPath<Skill>(path);
            if (skill == null)
                return new ErrorResponse($"Skill not found: {path}");

            var so = new SerializedObject(skill);
            var animProp = so.FindProperty("m_Animation");
            var transInProp = so.FindProperty("m_TransitionIn");
            var transOutProp = so.FindProperty("m_TransitionOut");
            var gravityProp = so.FindProperty("m_Gravity");

            string animName = animProp?.objectReferenceValue != null 
                ? animProp.objectReferenceValue.name : "(none)";
            float animLength = animProp?.objectReferenceValue is AnimationClip ac ? ac.length : 0;

            return new SuccessResponse($"Skill info: {skill.name}",
                new JObject
                {
                    ["name"] = skill.name,
                    ["animation"] = animName,
                    ["animationDuration"] = animLength,
                    ["transitionIn"] = transInProp?.floatValue ?? 0,
                    ["transitionOut"] = transOutProp?.floatValue ?? 0,
                    ["gravity"] = gravityProp?.floatValue ?? 1,
                    ["path"] = path
                });
        }

        /// <summary>
        /// Get info about a MeleeWeapon SO.
        /// params: weapon_path
        /// </summary>
        private static object GetWeaponInfo(JObject @params)
        {
            string path = @params["weapon_path"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("Required: 'weapon_path' or 'path'");

            MeleeWeapon weapon = AssetDatabase.LoadAssetAtPath<MeleeWeapon>(path);
            if (weapon == null)
                return new ErrorResponse($"Weapon not found: {path}");

            var so = new SerializedObject(weapon);
            var comboProp = so.FindProperty("m_Combos");
            var stateProp = so.FindProperty("m_State");
            var shieldProp = so.FindProperty("m_Shield");

            return new SuccessResponse($"Weapon info: {weapon.name}",
                new JObject
                {
                    ["name"] = weapon.name,
                    ["hasCombos"] = weapon.Combo != null,
                    ["hasShield"] = weapon.Shield != null,
                    ["path"] = path
                });
        }

        /// <summary>
        /// List all AnimationClips inside an FBX file.
        /// params: fbx_path
        /// </summary>
        private static object ListFbxClips(JObject @params)
        {
            string fbxPath = @params["fbx_path"]?.ToString() ?? @params["path"]?.ToString();
            if (string.IsNullOrEmpty(fbxPath))
                return new ErrorResponse("Required: 'fbx_path' or 'path'");

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            var clips = new JArray();

            foreach (Object sub in subAssets)
            {
                if (sub is AnimationClip ac && !ac.name.StartsWith("__preview__"))
                {
                    clips.Add(new JObject
                    {
                        ["name"] = ac.name,
                        ["duration"] = ac.length,
                        ["frameRate"] = ac.frameRate,
                        ["isLooping"] = ac.isLooping,
                        ["isHumanMotion"] = ac.isHumanMotion
                    });
                }
            }

            return new SuccessResponse($"Found {clips.Count} animation clip(s) in '{fbxPath}'",
                new JObject { ["clips"] = clips, ["path"] = fbxPath });
        }

        /// <summary>
        /// Programmatically build a Combos asset's combo tree and assign Skills to nodes.
        /// params: combos_path, nodes (array of {key, skill_path, children: [...]})
        /// key: A|B|C|D|E|F  (maps to MeleeKey enum)
        /// Example: [{"key":"A","skill_path":"...","children":[{"key":"A","skill_path":"..."}]}]
        /// </summary>
        private static object SetupCombos(JObject @params)
        {
            string combosPath = @params["combos_path"]?.ToString();
            JArray nodes = @params["nodes"] as JArray;

            if (string.IsNullOrEmpty(combosPath))
                return new ErrorResponse("Required: 'combos_path'");
            if (nodes == null || nodes.Count == 0)
                return new ErrorResponse("Required: 'nodes' array with at least one entry");

            Combos combosAsset = AssetDatabase.LoadAssetAtPath<Combos>(combosPath);
            if (combosAsset == null)
                return new ErrorResponse($"Combos asset not found: {combosPath}");

            SerializedObject so = new SerializedObject(combosAsset);
            ComboTree tree = combosAsset.Get;

            // Clear existing nodes by rebuilding from scratch via reflection
            var mDataField = typeof(TSerializableTree<ComboItem>)
                .GetField("m_Data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mNodesField = typeof(TSerializableTree<ComboItem>)
                .GetField("m_Nodes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var mRootsField = typeof(TSerializableTree<ComboItem>)
                .GetField("m_Roots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Reset the tree to empty
            mDataField.SetValue(tree, Activator.CreateInstance(mDataField.FieldType));
            mNodesField.SetValue(tree, Activator.CreateInstance(mNodesField.FieldType));
            mRootsField.SetValue(tree, new System.Collections.Generic.List<int>());

            var log = new JArray();
            BuildComboNodes(tree, nodes, TSerializableTree<ComboItem>.NODE_INVALID, log);

            // Mark dirty and save
            so.Update();
            EditorUtility.SetDirty(combosAsset);
            AssetDatabase.SaveAssetIfDirty(combosAsset);

            return new SuccessResponse($"Built combo tree in '{combosAsset.name}' with {log.Count} node(s).",
                new JObject { ["nodes_created"] = log, ["path"] = combosPath });
        }

        private static void BuildComboNodes(ComboTree tree, JArray nodeDefs, int parentId, JArray log)
        {
            if (nodeDefs == null) return;
            foreach (JObject nodeDef in nodeDefs)
            {
                string keyStr = nodeDef["key"]?.ToString() ?? "A";
                string skillPath = nodeDef["skill_path"]?.ToString();

                MeleeKey key = Enum.TryParse<MeleeKey>(keyStr, true, out MeleeKey parsed) ? parsed : MeleeKey.A;

                Skill skill = null;
                if (!string.IsNullOrEmpty(skillPath))
                {
                    skill = AssetDatabase.LoadAssetAtPath<Skill>(skillPath);
                }

                // Build a ComboItem via reflection (private setters)
                var item = new ComboItem();
                SetField(item, "m_Key", key);
                SetField(item, "m_Mode", MeleeMode.Tap);
                SetField(item, "m_When", MeleeExecute.InOrder);
                SetField(item, "m_Skill", skill);

                int nodeId = parentId == TSerializableTree<ComboItem>.NODE_INVALID
                    ? tree.AddToRoot(item)
                    : tree.AddChild(item, parentId);

                log.Add(new JObject
                {
                    ["id"] = nodeId,
                    ["key"] = key.ToString(),
                    ["skill"] = skill != null ? skill.name : "(none)",
                    ["parent"] = parentId
                });

                // Recurse into children
                JArray children = nodeDef["children"] as JArray;
                if (children != null && children.Count > 0)
                    BuildComboNodes(tree, children, nodeId, log);
            }
        }

        /// <summary>
        /// Assign a Combos asset to a MeleeWeapon's m_Combos field.
        /// params: weapon_path, combos_path
        /// </summary>
        private static object AssignCombosToWeapon(JObject @params)
        {
            string weaponPath = @params["weapon_path"]?.ToString();
            string combosPath = @params["combos_path"]?.ToString();

            if (string.IsNullOrEmpty(weaponPath) || string.IsNullOrEmpty(combosPath))
                return new ErrorResponse("Required: 'weapon_path', 'combos_path'");

            MeleeWeapon weapon = AssetDatabase.LoadAssetAtPath<MeleeWeapon>(weaponPath);
            if (weapon == null) return new ErrorResponse($"Weapon not found: {weaponPath}");

            Combos combos = AssetDatabase.LoadAssetAtPath<Combos>(combosPath);
            if (combos == null) return new ErrorResponse($"Combos not found: {combosPath}");

            SerializedObject so = new SerializedObject(weapon);
            SerializedProperty comboProp = so.FindProperty("m_Combos");
            if (comboProp == null)
                return new ErrorResponse("Could not find 'm_Combos' property on MeleeWeapon");

            comboProp.objectReferenceValue = combos;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssetIfDirty(weapon);

            return new SuccessResponse($"Assigned '{combos.name}' → '{weapon.name}'",
                new JObject { ["weapon"] = weapon.name, ["combos"] = combos.name });
        }

        // ─── NEW ACTIONS ───────────────────────────────────────────────────

        /// <summary>
        /// Inspect a Character's setup: mannequin, animator, avatar, player flag, children.
        /// params: target (instanceID or name)
        /// </summary>
        private static object InspectCharacter(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("Required: 'target'");

            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            Character character = target.GetComponent<Character>();
            if (character == null)
                return new ErrorResponse($"No Character component on: {targetName}");

            var so = new SerializedObject(character);

            // Read key properties
            bool isPlayer = so.FindProperty("m_IsPlayer")?.boolValue ?? false;

            // Animim references
            var mannequinProp = so.FindProperty("m_Kernel.m_Animim.m_Mannequin");
            var animatorProp = so.FindProperty("m_Kernel.m_Animim.m_Animator");
            var startStateProp = so.FindProperty("m_Kernel.m_Animim.m_StartState");

            Transform mannequin = mannequinProp?.objectReferenceValue as Transform;
            Animator animator = animatorProp?.objectReferenceValue as Animator;
            Object startState = startStateProp?.objectReferenceValue;

            // Children info
            var children = new JArray();
            for (int i = 0; i < target.transform.childCount; i++)
            {
                var child = target.transform.GetChild(i);
                var childAnimator = child.GetComponent<Animator>();
                var childInfo = new JObject
                {
                    ["name"] = child.name,
                    ["instanceID"] = child.gameObject.GetInstanceID(),
                    ["hasAnimator"] = childAnimator != null,
                    ["isPrefabInstance"] = PrefabUtility.IsPartOfPrefabInstance(child.gameObject),
                    ["childCount"] = child.childCount
                };
                if (childAnimator != null)
                {
                    childInfo["avatarName"] = childAnimator.avatar != null ? childAnimator.avatar.name : "none";
                    childInfo["controllerName"] = childAnimator.runtimeAnimatorController != null ? childAnimator.runtimeAnimatorController.name : "none";
                }
                // Check if it's a prefab and get source
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                {
                    var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    if (prefabAsset != null)
                    {
                        childInfo["prefabSource"] = AssetDatabase.GetAssetPath(prefabAsset);
                    }
                }
                children.Add(childInfo);
            }

            return new SuccessResponse($"Character inspection: {target.name}",
                new JObject
                {
                    ["name"] = target.name,
                    ["isPlayer"] = isPlayer,
                    ["mannequin"] = mannequin != null ? mannequin.name : "(none)",
                    ["mannequinAssigned"] = mannequin != null,
                    ["animator"] = animator != null ? animator.name : "(none)",
                    ["animatorAssigned"] = animator != null,
                    ["avatar"] = animator != null && animator.avatar != null ? animator.avatar.name : "(none)",
                    ["startState"] = startState != null ? startState.name : "(none)",
                    ["hasCharacterController"] = target.GetComponent<CharacterController>() != null,
                    ["children"] = children
                });
        }

        /// <summary>
        /// Remove a named child (or all children) from a target GameObject.
        /// params: target, child_name (optional, removes all if omitted), match_type ("exact" or "contains", default "exact")
        /// </summary>
        private static object RemoveChild(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            string childName = @params["child_name"]?.ToString();
            string matchType = @params["match_type"]?.ToString() ?? "exact";

            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("Required: 'target'");

            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            var removed = new JArray();
            for (int i = target.transform.childCount - 1; i >= 0; i--)
            {
                var child = target.transform.GetChild(i);
                bool shouldRemove = false;

                if (string.IsNullOrEmpty(childName))
                {
                    shouldRemove = true;  // Remove all
                }
                else if (matchType == "contains")
                {
                    shouldRemove = child.name.IndexOf(childName, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else
                {
                    shouldRemove = child.name.Equals(childName, StringComparison.OrdinalIgnoreCase);
                }

                if (shouldRemove)
                {
                    removed.Add(new JObject
                    {
                        ["name"] = child.name,
                        ["instanceID"] = child.gameObject.GetInstanceID()
                    });
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            EditorUtility.SetDirty(target);
            return new SuccessResponse($"Removed {removed.Count} child(ren) from '{target.name}'",
                new JObject { ["removed"] = removed });
        }

        /// <summary>
        /// Configure an Animator component on a child of a target: set Avatar from an FBX.
        /// params: target, child_name (optional, uses first child with Animator), fbx_path (for Avatar lookup)
        /// </summary>
        private static object ConfigureAnimator(JObject @params)
        {
            string targetName = @params["target"]?.ToString();
            string childName = @params["child_name"]?.ToString();
            string fbxPath = @params["fbx_path"]?.ToString();

            if (string.IsNullOrEmpty(targetName))
                return new ErrorResponse("Required: 'target'");

            GameObject target = FindGameObject(targetName);
            if (target == null)
                return new ErrorResponse($"GameObject not found: {targetName}");

            // Find Animator on a child
            Animator animator = null;
            if (!string.IsNullOrEmpty(childName))
            {
                Transform child = target.transform.Find(childName);
                if (child != null) animator = child.GetComponent<Animator>();
                // Also check nested children
                if (animator == null && child != null)
                    animator = child.GetComponentInChildren<Animator>();
            }
            if (animator == null)
                animator = target.GetComponentInChildren<Animator>();
            if (animator == null)
                return new ErrorResponse($"No Animator found on '{targetName}' or its children");

            // Load Avatar from FBX
            Avatar avatar = null;
            if (!string.IsNullOrEmpty(fbxPath))
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                foreach (Object sub in subAssets)
                {
                    if (sub is Avatar av)
                    {
                        avatar = av;
                        break;
                    }
                }

                if (avatar != null)
                {
                    Undo.RecordObject(animator, "Configure Animator Avatar");
                    animator.avatar = avatar;
                    EditorUtility.SetDirty(animator);
                }
                else
                {
                    return new ErrorResponse($"No Avatar found in FBX: {fbxPath}");
                }
            }

            return new SuccessResponse($"Configured Animator on '{animator.gameObject.name}': Avatar={avatar?.name ?? "unchanged"}",
                new JObject
                {
                    ["gameObject"] = animator.gameObject.name,
                    ["avatar"] = avatar != null ? avatar.name : "unchanged",
                    ["instanceID"] = animator.gameObject.GetInstanceID()
                });
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        // ─── HELPERS ───────────────────────────────────────────────────

        private static GameObject FindGameObject(string nameOrId)
        {
            if (int.TryParse(nameOrId, out int id))
            {
                return EditorUtility.InstanceIDToObject(id) as GameObject;
            }
            return GameObject.Find(nameOrId);
        }
    }
}