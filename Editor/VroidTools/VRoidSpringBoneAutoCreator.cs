#define USE_VROID_MOD

#if USE_VROID_MOD
using VRoidModSpringBones;  // 仅在VROID模式下引用
using MySpringBoneColliderGroup = VRoidModSpringBones.VRoidSpringBoneColliderGroup;
using MySpringBone = VRoidModSpringBones.VRoidSpringBone;
#elif USE_VRM
using VRM;  // 仅在VRM模式下引用
using MySpringBoneColliderGroup = VRM.VRMSpringBoneColliderGroup;
using MySpringBone = VRM.VRMSpringBone;
#else
// 未定义符号时提示用户配置
#error 请定义 USE_VROID_MOD 或 USE_VRM 符号
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;


namespace VRMSpringAutoCreator
{
    // 配置数据类（存储各部位碰撞体/弹簧骨骼参数）
    [System.Serializable]
    public class BoneColliderConfig
    {
        public bool enable = true; // 是否启用该部位
        public float baseRadius = 0.1f; // 基础半径
        public int count = 1; // 碰撞体/弹簧骨骼数量
        public List<float> yOffsets = new List<float> { 0f }; // Y轴偏移列表
        public List<float> radiusScales = new List<float> { 1f }; // 半径缩放列表
    }

    // 全局配置类
    [System.Serializable]
    public class AutoSpringBoneConfig
    {
        public RecognitionMode recognitionMode = RecognitionMode.Hybrid; // 识别模式
        public Dictionary<string, BoneColliderConfig> colliderConfigs = new Dictionary<string, BoneColliderConfig>(); // 碰撞体配置
        public Dictionary<string, BoneColliderConfig> springBoneConfigs = new Dictionary<string, BoneColliderConfig>(); // 弹簧骨骼配置
    }

    // 识别模式枚举
    public enum RecognitionMode
    {
        NameMatching, // 名称匹配模式
        HumanoidAvatar, // 类人骨骼映射模式
        Hybrid // 混合模式
    }

    // 配置面板窗口
    public class VRoidAutoSpringBoneWindow : EditorWindow
    {
        private AutoSpringBoneConfig config = new AutoSpringBoneConfig();
        private Vector2 scrollPos;

        [MenuItem("VRoidTools/自动弹簧骨骼配置")]
        public static void ShowWindow()
        {
            GetWindow<VRoidAutoSpringBoneWindow>("自动弹簧骨骼配置");
        }

        private void OnEnable()
        {
            if (config.colliderConfigs.Count == 0 && config.springBoneConfigs.Count == 0)
            {
                var defaultColliderParts = new Dictionary<string, (float radius, int count, List<float> offsets, List<float> scales)>
                {
                    { "head", (0.15f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "neck", (0.08f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "shoulder", (0.1f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "torso", (0.1f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "hip", (0.08f, 2, new List<float> { 0f, -0.15f }, new List<float> { 1f, 1.2f }) },
                    { "knee", (0.07f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "arm", (0.07f, 2, new List<float> { 0f, 0.1f }, new List<float> { 1f, 1f }) },
                    { "elbow", (0.06f, 1, new List<float> { 0f }, new List<float> { 1f }) } ,


                };

                var defaultSpringParts = new Dictionary<string, (float radius, int count, List<float> offsets, List<float> scales)>
                {
                    { "hair", (0.08f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "skirt", (0.1f, 1, new List<float> { 0f }, new List<float> { 1f }) },
                    { "chest", (0.1f, 2, new List<float> { 0f, 0.05f }, new List<float> { 1f, 1.1f }) }, // 胸部弹簧骨骼（2段）
                    { "tail", (0.07f, 3, new List<float> { 0f, 0.1f, 0.2f }, new List<float> { 1f, 0.9f, 0.8f }) } // 尾巴弹簧骨骼（3段）

                };

                foreach (var (part, (radius, count, offsets, scales)) in defaultColliderParts)
                {
                    config.colliderConfigs[part] = new BoneColliderConfig
                    {
                        enable = true,
                        baseRadius = radius,
                        count = count,
                        yOffsets = new List<float>(offsets),
                        radiusScales = new List<float>(scales)
                    };
                }

                foreach (var (part, (radius, count, offsets, scales)) in defaultSpringParts)
                {
                    config.springBoneConfigs[part] = new BoneColliderConfig
                    {
                        enable = true,
                        baseRadius = radius,
                        count = count,
                        yOffsets = new List<float>(offsets),
                        radiusScales = new List<float>(scales)
                    };
                }
            }
        }

        private void OnGUI()
        {
            Undo.RecordObject(this, "Spring Bone Config Change");
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // 识别模式选择
            config.recognitionMode = (RecognitionMode)EditorGUILayout.EnumPopup("识别模式", config.recognitionMode);
            EditorGUILayout.HelpBox("名称匹配：通过骨骼名称关键词识别\n类人骨骼：通过Humanoid Avatar骨骼映射识别\n混合模式：优先Humanoid映射，头发和裙子使用名称匹配", MessageType.Info);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("碰撞体配置", EditorStyles.boldLabel);
            var colliderParts = new List<string>(config.colliderConfigs.Keys);
            foreach (var part in colliderParts)
            {
                EditorGUILayout.Space(5);
                var partConfig = config.colliderConfigs[part];
                partConfig.enable = EditorGUILayout.Foldout(partConfig.enable, char.ToUpper(part[0]) + part.Substring(1));
                if (partConfig.enable)
                {
                    EditorGUI.indentLevel++;
                    partConfig.baseRadius = EditorGUILayout.FloatField("基础半径", partConfig.baseRadius);
                    partConfig.count = EditorGUILayout.IntField("碰撞体数量", Mathf.Max(1, partConfig.count));

                    while (partConfig.yOffsets.Count < partConfig.count)
                        partConfig.yOffsets.Add(0f);
                    while (partConfig.yOffsets.Count > partConfig.count)
                        partConfig.yOffsets.RemoveAt(partConfig.yOffsets.Count - 1);
                    while (partConfig.radiusScales.Count < partConfig.count)
                        partConfig.radiusScales.Add(1f);
                    while (partConfig.radiusScales.Count > partConfig.count)
                        partConfig.radiusScales.RemoveAt(partConfig.radiusScales.Count - 1);

                    for (int i = 0; i < partConfig.count; i++)
                    {
                        EditorGUILayout.LabelField($"碰撞体 {i + 1}");
                        EditorGUI.indentLevel++;
                        partConfig.yOffsets[i] = EditorGUILayout.FloatField("X或Y轴偏移", partConfig.yOffsets[i]);
                        partConfig.radiusScales[i] = EditorGUILayout.FloatField("半径缩放", partConfig.radiusScales[i]);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("弹簧骨骼配置", EditorStyles.boldLabel);
            var springParts = new List<string>(config.springBoneConfigs.Keys);
            foreach (var part in springParts)
            {
                EditorGUILayout.Space(5);
                var partConfig = config.springBoneConfigs[part];
                partConfig.enable = EditorGUILayout.Foldout(partConfig.enable, char.ToUpper(part[0]) + part.Substring(1));
                if (partConfig.enable)
                {
                    EditorGUI.indentLevel++;
                    partConfig.baseRadius = EditorGUILayout.FloatField("基础半径", partConfig.baseRadius);
                    partConfig.count = EditorGUILayout.IntField("弹簧骨骼数量", Mathf.Max(1, partConfig.count));

                    while (partConfig.yOffsets.Count < partConfig.count)
                        partConfig.yOffsets.Add(0f);
                    while (partConfig.yOffsets.Count > partConfig.count)
                        partConfig.yOffsets.RemoveAt(partConfig.yOffsets.Count - 1);
                    while (partConfig.radiusScales.Count < partConfig.count)
                        partConfig.radiusScales.Add(1f);
                    while (partConfig.radiusScales.Count > partConfig.count)
                        partConfig.radiusScales.RemoveAt(partConfig.radiusScales.Count - 1);

                    for (int i = 0; i < partConfig.count; i++)
                    {
                        EditorGUILayout.LabelField($"弹簧骨骼 {i + 1}");
                        EditorGUI.indentLevel++;
                        partConfig.yOffsets[i] = EditorGUILayout.FloatField("Y轴偏移", partConfig.yOffsets[i]);
                        partConfig.radiusScales[i] = EditorGUILayout.FloatField("半径缩放", partConfig.radiusScales[i]);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(20);
            if (GUILayout.Button("保存配置", GUILayout.Height(30)))
            {
                string path = EditorUtility.SaveFilePanel("保存配置", "", "SpringBoneConfig.json", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    System.IO.File.WriteAllText(path, JsonUtility.ToJson(config));
                }
            }
            if (GUILayout.Button("加载配置", GUILayout.Height(30)))
            {
                string path = EditorUtility.OpenFilePanel("加载配置", "", "json");
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    config = JsonUtility.FromJson<AutoSpringBoneConfig>(System.IO.File.ReadAllText(path));
                    Repaint();
                }
            }
            if (GUILayout.Button("预览骨骼识别结果", GUILayout.Height(30)))
            {
                if (VRoidAutoSpringBoneSetup.AutoSetupValidation())
                {
                    var root = Selection.activeGameObject;
                    var animator = root.GetComponent<Animator>();
                    var allBones = root.GetComponentsInChildren<Transform>(true).ToList();
                    string preview = "识别的骨骼：\n";
                    foreach (var part in config.colliderConfigs.Keys.Concat(config.springBoneConfigs.Keys))
                    {
                        var bones = VRoidAutoSpringBoneSetup.GetTargetBones(part, allBones, animator, config);
                        preview += $"{part}: {string.Join(", ", bones.Select(b => b.name))}\n";
                    }
                    EditorUtility.DisplayDialog("骨骼预览", preview, "确定");
                }
                else
                {
                    EditorUtility.DisplayDialog("错误", "请先选中带有Animator组件的模型根对象", "确定");
                }
            }
            if (GUILayout.Button("应用配置并自动设置", GUILayout.Height(30)))
            {
                if (VRoidAutoSpringBoneSetup.AutoSetupValidation())
                {
                    VRoidAutoSpringBoneSetup.AutoSetupSpringBonesAndColliders(config);
                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog("错误", "请先选中带有Animator组件的模型根对象", "确定");
                }
            }
            if (GUILayout.Button("批量应用配置", GUILayout.Height(30)))
            {
                foreach (var obj in Selection.gameObjects)
                {
                    if (obj.GetComponent<Animator>() != null)
                    {
                        Selection.activeGameObject = obj;
                        VRoidAutoSpringBoneSetup.AutoSetupSpringBonesAndColliders(config);
                    }
                }
                EditorUtility.DisplayDialog("成功", "批量设置完成！", "确定");
            }

            EditorGUILayout.EndScrollView();
        }
    }

    internal static class VRoidAutoSpringBoneSetup
    {
        private static readonly Dictionary<string, List<string>> _boneKeywordMap = new Dictionary<string, List<string>>
        {
            { "hair", new List<string> {
                "hair", "kami", "毛", "头发", "かみ", "ヘア", "hair_",
                "kaminoke", "hairfront", "hairback", "hairside", "hairtail",
                "kami_", "ヘアー", "髪", "毛髪", "後髪", "前髪", "サイドヘア",
                "kaminoke_", "maegami", "ushirogami", "sidehair", "ponytail" } },
            { "skirt", new List<string> { "skirt", "sukāto", "スカート", "裙子", "裙", "skirt_", "skrit", "ミニスカート", "miniskirt", "miniskrit" } }, // 添加skrit兼容
            { "hip", new List<string> { "hip", "koshi", "腰", "大腿", "ひざ", "ヒップ" } },
            { "torso", new List<string> { "torso", "mune", "胴", "躯干", "胸部", "トルソ" } },
            { "neck", new List<string> { "neck", "kubi", "首", "脖子", "くび", "ネック" } },
            { "shoulder", new List<string> { "shoulder", "kata", "肩", "肩膀", "かた", "ショルダー" } },
            { "head", new List<string> { "head", "atama", "頭", "头", "あたま", "ヘッド" } },
            { "knee", new List<string> { "knee", "hiza", "膝", "膝盖", "ひざ", "ニー" } },
            { "arm", new List<string> { "arm", "ude", "腕", "手臂", "うで", "アーム" } },
            { "elbow", new List<string> { "elbow", "hiji", "肘", "手肘", "ひじ", "エルボー" } },
                        // 在 _boneKeywordMap 中更新胸部和尾巴的关键词
            { "chest", new List<string> { "chest", "mune", "胸部", "胸", "breast", "oppai", "むね", "チェスト" } }, // 增加胸部相关关键词
            { "tail", new List<string> { "tail", "shippo", "尾巴", "尾", "尻尾", "しっぽ", "テール", "tail_" } }, // 增加尾巴关键词
        };

        private static readonly Dictionary<string, List<HumanBodyBones>> _humanBoneMap = new Dictionary<string, List<HumanBodyBones>>
        {
            { "head", new List<HumanBodyBones> { HumanBodyBones.Head } },
            { "neck", new List<HumanBodyBones> { HumanBodyBones.Neck } },
            { "shoulder", new List<HumanBodyBones> { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder } },
            { "torso", new List<HumanBodyBones> { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine } },
            { "hip", new List<HumanBodyBones> { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg } },
            { "knee", new List<HumanBodyBones> { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg } },
            { "arm", new List<HumanBodyBones> { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm } },
            { "elbow", new List<HumanBodyBones> { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm } }
        };

        public static bool AutoSetupValidation()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<Animator>() != null;
        }

        public static void AutoSetupSpringBonesAndColliders(AutoSpringBoneConfig config)
        {
            var rootObj = Selection.activeGameObject;
            if (rootObj == null) return;

            var animator = rootObj.GetComponent<Animator>();
            if (animator == null)
            {
                EditorUtility.DisplayDialog("错误", "选中的对象没有Animator组件", "确定");
                return;
            }

            if (config.recognitionMode == RecognitionMode.HumanoidAvatar &&
                (animator.avatar == null || !animator.avatar.isHuman))
            {
                EditorUtility.DisplayDialog("警告", $"模型 {rootObj.name} 没有有效的Humanoid Avatar，头发和裙子无法识别，已切换为混合模式。", "确定");
                config.recognitionMode = RecognitionMode.Hybrid;
            }

            try
            {
                var allBones = rootObj.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != rootObj.transform && !IsInCustomArms(t, rootObj.transform))
                    .ToList();

                var potentialHairOrSkirt = allBones
                    .Where(b => b.name.Contains("hair", StringComparison.OrdinalIgnoreCase) ||
                                b.name.Contains("skirt", StringComparison.OrdinalIgnoreCase) ||
                                b.name.Contains("skrit", StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.name)
                    .Distinct()
                    .ToList();
                if (potentialHairOrSkirt.Any() && config.recognitionMode == RecognitionMode.HumanoidAvatar)
                {
                    EditorUtility.DisplayDialog("提示", $"检测到可能的头发或裙子骨骼：{string.Join(", ", potentialHairOrSkirt)}\n建议使用混合模式以正确识别", "确定");
                }

                var springManager = GetOrCreateSpringManager(rootObj.transform);
                ClearExistingSpringBones(springManager);

                var colliderGroups = CreateColliders(rootObj.transform, allBones, animator, config.colliderConfigs);
                CreateSpringBones(springManager, allBones, colliderGroups, animator, config.springBoneConfigs);

                EditorUtility.DisplayDialog("成功", "自动设置完成！", "确定");
            }
            catch (Exception e)
            {
                Debug.LogError($"自动设置失败: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"自动设置失败: {e.Message}", "确定");
            }
        }

        private static bool IsInCustomArms(Transform bone, Transform root)
        {
            var current = bone;
            while (current != root && current != null)
            {
                if (current.name.IndexOf("CustomArms", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static GameObject GetOrCreateSpringManager(Transform root)
        {
#if USE_VROID_MOD
            string managerName = "SpringManager"; // VROID模式：用原来的SpringManager
#elif USE_VRM
    string managerName = "secondary";    // VRM模式：用VRM常见的secondary节点
#else
            string managerName = "secondary";
#endif

            var manager = root.Find(managerName)?.gameObject;
            if (manager == null)
            {
                manager = new GameObject(managerName);
                manager.transform.SetParent(root, false);
                manager.transform.localPosition = Vector3.zero;
                manager.transform.localRotation = Quaternion.identity;
                Undo.RegisterCreatedObjectUndo(manager, $"Create {managerName}");
            }
            return manager;
        }

        private static void ClearExistingSpringBones(GameObject springManager)
        {
            foreach (var springBone in springManager.GetComponents<MySpringBone>())
                Undo.DestroyObjectImmediate(springBone);
        }

        private static Dictionary<string, MySpringBoneColliderGroup> CreateColliders(
            Transform root, List<Transform> allBones, Animator animator, Dictionary<string, BoneColliderConfig> colliderConfigs)
        {
            var colliderGroups = new Dictionary<string, MySpringBoneColliderGroup>();

            foreach (var (part, partConfig) in colliderConfigs)
            {
                if (!partConfig.enable) continue;

                var targetBones = GetTargetBones(part, allBones, animator, new AutoSpringBoneConfig { recognitionMode = RecognitionMode.Hybrid });
                targetBones = targetBones
                    .Distinct()
                    .Where(b => b != null && !IsInCustomArms(b, root))
                    .ToList();

                if (!targetBones.Any()) continue;

                foreach (var bone in targetBones)
                {
                    var colliderGroup = bone.GetComponent<MySpringBoneColliderGroup>();
                    bool isNewComponent = colliderGroup == null;
                    if (isNewComponent)
                    {
                        colliderGroup = bone.gameObject.AddComponent<MySpringBoneColliderGroup>();
                        Undo.RegisterCreatedObjectUndo(colliderGroup, "Add Collider Group");
                    }

                    float scaleFactor = Mathf.Max(bone.lossyScale.x, bone.lossyScale.y, bone.lossyScale.z);
                    float baseRadius = partConfig.baseRadius * scaleFactor;

                    var spheres = new List<MySpringBoneColliderGroup.SphereCollider>();
                    // 仅手臂碰撞体使用±X偏移（按左右区分）
                    if (part == "arm")
                    {
                        // 判断左右手臂（通过骨骼名称含"Left"/"Right"）
                        bool isLeftArm = bone.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isRightArm = bone.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
                        float xSign = isLeftArm ? -1f : (isRightArm ? 1f : 0f); // 左负右正

                        for (int i = 0; i < partConfig.count; i++)
                        {
                            // 第二个碰撞体偏移±X=0.1，其他碰撞体偏移0
                            float xOffset = (i == 1) ? 0.1f * xSign : 0f;
                            float radiusScale = i < partConfig.radiusScales.Count ? partConfig.radiusScales[i] : 1f;
                            spheres.Add(new MySpringBoneColliderGroup.SphereCollider
                            {
                                Offset = new Vector3(xOffset, 0f, 0f), // 手臂横向偏移（X轴）
                                Radius = baseRadius * radiusScale
                            });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < partConfig.count; i++)
                        {
                            float yOffset = i < partConfig.yOffsets.Count ? partConfig.yOffsets[i] : 0f;
                            float radiusScale = i < partConfig.radiusScales.Count ? partConfig.radiusScales[i] : 1f;
                            spheres.Add(new MySpringBoneColliderGroup.SphereCollider
                            {
                                Offset = new Vector3(0, yOffset, 0),
                                Radius = baseRadius * radiusScale
                            });
                        }
                    }

                    // 【关键修复】统一设置碰撞体数据并添加到碰撞体组（arm和其他部位都执行）
                    colliderGroup.Colliders = spheres.ToArray();

                    var path = GetRelativePath(root, bone);
                    if (!colliderGroups.ContainsKey(path))
                        colliderGroups[path] = colliderGroup;

                    Debug.Log(isNewComponent ? $"已在 {path} 添加碰撞体组件" : $"已更新 {path} 的碰撞体参数", bone);
                }
            }

            return colliderGroups;
        }

        private static void CreateSpringBones(GameObject springManager, List<Transform> allBones,
            Dictionary<string, MySpringBoneColliderGroup> colliderGroups, Animator animator, Dictionary<string, BoneColliderConfig> springBoneConfigs)
        {

            foreach (var (part, partConfig) in springBoneConfigs)
            {
                if (!partConfig.enable) continue;

                var targetBones = GetTargetBones(part, allBones, animator, new AutoSpringBoneConfig { recognitionMode = RecognitionMode.Hybrid });
                var rootBones = GetRootBones(targetBones);
                if (!rootBones.Any()) continue;

                var springBone = springManager.AddComponent<MySpringBone>();
                Undo.RegisterCreatedObjectUndo(springBone, "Add Spring Bone");
                springBone.RootBones = rootBones.ToList();
                //springBone.name = $"{char.ToUpper(part[0]) + part.Substring(1)}Spring"; // 仅设置组件名称
                // 按部位设置参数（胸部更柔软，尾巴有拖拽感）
                switch (part)
                {
                    case "chest":
                        springBone.m_stiffnessForce = 2.2f; // 刚度稍低，更柔软
                        springBone.m_gravityPower = 0.04f; // 重力稍大，模拟下垂
                        springBone.m_dragForce = 0.55f; // 阻力适中
                        break;
                    case "tail":
                        springBone.m_stiffnessForce = 2.8f; // 刚度中等
                        springBone.m_gravityPower = 0.03f; // 重力适中
                        springBone.m_dragForce = 0.65f; // 阻力稍大，避免过度晃动
                        break;
                    case "hair":
                        springBone.m_stiffnessForce = 2.5f;
                        springBone.m_gravityPower = 0.03f;
                        springBone.m_dragForce = 0.6f;
                        break;
                    case "skirt":
                        springBone.m_stiffnessForce = 3.4f;
                        springBone.m_gravityPower = 0.025f;
                        springBone.m_dragForce = 0.58f;
                        break;
                }


                springBone.m_hitRadius = 0.02f;

                springBone.m_gravityDir = new Vector3(0, -1, 0);
                // 设置弹簧中心为根骨头（所有骨头的顶层父节点）
                //springBone.m_center = rootBones.First().root; // 取第一个根骨头的根节点作为中心
                // rootBones.OrderBy(b => b.position.y).LastOrDefault();

                springBone.ColliderGroups = GetRelevantColliders(colliderGroups, new[] { "head", "neck", "shoulder", "torso", "hip", "knee", "arm", "elbow" })
                    .Distinct()
                    .ToArray();
            }
        }

        public static List<Transform> GetTargetBones(string part, List<Transform> allBones, Animator animator, AutoSpringBoneConfig config)
        {
            if (config.recognitionMode == RecognitionMode.Hybrid)
            {
                if (part == "hair" || part == "skirt")
                    return GetBonesByName(allBones, part);
                var humanoidBones = GetBonesByHumanoid(animator, part);
                return humanoidBones.Any() ? humanoidBones : GetBonesByName(allBones, part);
            }
            return config.recognitionMode == RecognitionMode.NameMatching
                ? GetBonesByName(allBones, part)
                : GetBonesByHumanoid(animator, part);
        }

        private static List<Transform> GetBonesByName(List<Transform> allBones, string part)
        {
            if (!_boneKeywordMap.TryGetValue(part, out var keywords))
                return new List<Transform>();

            return allBones
                .Where(b => keywords.Any(keyword =>
                            b.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            b.name.IndexOf(keyword + "_", StringComparison.OrdinalIgnoreCase) >= 0)
                            && !b.name.Contains("ik", StringComparison.OrdinalIgnoreCase)
                            && (part != "hip" && part != "knee" ||
                                b.name.EndsWith("D", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private static List<Transform> GetBonesByHumanoid(Animator animator, string part)
        {
            if (!_humanBoneMap.TryGetValue(part, out var humanBones))
                return new List<Transform>();

            var bones = new List<Transform>();
            foreach (var humanBone in humanBones)
            {
                var boneTransform = animator.GetBoneTransform(humanBone);
                if (boneTransform != null)
                    bones.Add(boneTransform);
            }
            return bones;
        }

        private static IEnumerable<MySpringBoneColliderGroup> GetRelevantColliders(
            Dictionary<string, MySpringBoneColliderGroup> colliderGroups, string[] relevantParts)
        {
            foreach (var (path, group) in colliderGroups)
            {
                if (relevantParts.Any(part => _boneKeywordMap[part].Any(kw => path.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)))
                    yield return group;
            }
        }

        private static List<Transform> GetRootBones(List<Transform> bones)
        {
            if (!bones.Any()) return new List<Transform>();

            var boneSet = new HashSet<Transform>(bones);
            var rootBones = new List<Transform>();

            foreach (var bone in bones)
            {
                bool isChildOfAnother = false;
                var current = bone.parent;
                while (current != null)
                {
                    if (boneSet.Contains(current))
                    {
                        isChildOfAnother = true;
                        break;
                    }
                    current = current.parent;
                }
                if (!isChildOfAnother)
                    rootBones.Add(bone);
            }
            return rootBones;
        }

        private static float EstimateBoneLength(Transform bone)
        {
            float length = 0.1f;
            var children = bone.GetComponentsInChildren<Transform>();
            if (children.Length > 1)
            {
                length = Vector3.Distance(bone.position, children[1].position);
            }
            return length;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var path = target.name;
            var current = target.parent;
            while (current != root && current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }
            return path;
        }
    }
}