using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Linq;

public class SymmetrySyncWindow : EditorWindow
{
    // 源物体和目标物体
    private Transform sourceTransform;
    private Transform targetTransform;

    // 对称设置
    private SymmetryAxis symmetryAxis = SymmetryAxis.X;
    private List<SymmetryKeywordPair> keywordPairs = new List<SymmetryKeywordPair>
    {
        new SymmetryKeywordPair ("Left", "Right"),
        new SymmetryKeywordPair ("左", "右"),
        new SymmetryKeywordPair ("L_", "R_"),
        new SymmetryKeywordPair ("左側", "右側"),
        new SymmetryKeywordPair ("左の", "右の")
    };
    private bool ignorePrefixNumbers = true;
    private bool showArraySettings;
    private List<string> recursiveArrayTypes = new List<string>
{
    "SphereCollider" // 默认包含VRoid碰撞体类
};
    // 匹配模式枚举
    public enum MatchMode
    {
        Mixed,       // 混合匹配：先骨骼匹配，失败再名称匹配
        NameOnly,    // 名称匹配：仅使用原关键词逻辑
        BoneOnly     // 骨骼匹配：仅基于骨骼层级与结构匹配
    }

    // 2. 添加Transform同步控制选项
    private bool modifyTransform = false; // 是否修改Transform
    private bool modifyComponents = true; // 是否修改组件

    private bool autoCreateMissingComponents = true; // 是否自动创建缺失组件

    private List<Type> excludedComponentTypes = new List<Type> // 排除的组件类型
{
    typeof(Transform),
    typeof(Animator),
    typeof(SkinnedMeshRenderer),
    typeof(MeshFilter),
    typeof(MeshRenderer)
};

    // 骨骼匹配相关字段
    private MatchMode currentMatchMode = MatchMode.Mixed;

    // Humanoid骨骼对称映射表
    private static Dictionary<HumanBodyBones, HumanBodyBones> symmetricBoneMap = new Dictionary<HumanBodyBones, HumanBodyBones>()
{
    { HumanBodyBones.Hips, HumanBodyBones.Hips }, // 中轴骨骼对称到自身
    { HumanBodyBones.Spine, HumanBodyBones.Spine },
    { HumanBodyBones.Chest, HumanBodyBones.Chest },
    { HumanBodyBones.UpperChest, HumanBodyBones.UpperChest },
    { HumanBodyBones.Neck, HumanBodyBones.Neck },
    { HumanBodyBones.Head, HumanBodyBones.Head },

    { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder },
    { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm },
    { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm },
    { HumanBodyBones.LeftHand, HumanBodyBones.RightHand },
    { HumanBodyBones.LeftThumbProximal, HumanBodyBones.RightThumbProximal },
    { HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.RightThumbIntermediate },
    { HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal },
    { HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal },
    { HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate },
    { HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal },
    { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal },
    { HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate },
    { HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal },
    { HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal },
    { HumanBodyBones.LeftRingIntermediate, HumanBodyBones.RightRingIntermediate },
    { HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal },
    { HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal },
    { HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.RightLittleIntermediate },
    { HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal },

    { HumanBodyBones.RightShoulder, HumanBodyBones.LeftShoulder },
    { HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperArm },
    { HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm },
    { HumanBodyBones.RightHand, HumanBodyBones.LeftHand },
    { HumanBodyBones.RightThumbProximal, HumanBodyBones.LeftThumbProximal },
    { HumanBodyBones.RightThumbIntermediate, HumanBodyBones.LeftThumbIntermediate },
    { HumanBodyBones.RightThumbDistal, HumanBodyBones.LeftThumbDistal },
    { HumanBodyBones.RightIndexProximal, HumanBodyBones.LeftIndexProximal },
    { HumanBodyBones.RightIndexIntermediate, HumanBodyBones.LeftIndexIntermediate },
    { HumanBodyBones.RightIndexDistal, HumanBodyBones.LeftIndexDistal },
    { HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftMiddleProximal },
    { HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.LeftMiddleIntermediate },
    { HumanBodyBones.RightMiddleDistal, HumanBodyBones.LeftMiddleDistal },
    { HumanBodyBones.RightRingProximal, HumanBodyBones.LeftRingProximal },
    { HumanBodyBones.RightRingIntermediate, HumanBodyBones.LeftRingIntermediate },
    { HumanBodyBones.RightRingDistal, HumanBodyBones.LeftRingDistal },
    { HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftLittleProximal },
    { HumanBodyBones.RightLittleIntermediate, HumanBodyBones.LeftLittleIntermediate },
    { HumanBodyBones.RightLittleDistal, HumanBodyBones.LeftLittleDistal },

    { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg },
    { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg },
    { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot },
    { HumanBodyBones.LeftToes, HumanBodyBones.RightToes },

    { HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftUpperLeg },
    { HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftLowerLeg },
    { HumanBodyBones.RightFoot, HumanBodyBones.LeftFoot },
    { HumanBodyBones.RightToes, HumanBodyBones.LeftToes }
};

    // 面板状态

    private bool showAdvancedSettings;
    private Vector2 mainScrollPosition;

    [MenuItem("VRoidTools/对称同步工具")]
    public static void ShowWindow()
    {
        SymmetrySyncWindow window = GetWindow<SymmetrySyncWindow>("对称同步工具");
        window.minSize = new Vector2(300, 500); // 设置最小尺寸
        window.Show();

        // 初始时检查当前选择
        if (Selection.activeTransform != null && Selection.transforms.Length == 1)
        {
            window.sourceTransform = Selection.activeTransform;
            window.AutoFindSymmetricTransform();
        }
    }
    private void OnEnable()
    {
        // 监听选择变化
        Selection.selectionChanged += OnSelectionChanged;
        excludedComponentTypes.Add(typeof(Camera));
        excludedComponentTypes.Add(typeof(Light));
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        // 当选择单个物体时自动设置为源物体
        if (Selection.activeTransform != null && Selection.transforms.Length == 1)
        {
            // 只有当选择的物体与当前源物体不同时才更新
            if (sourceTransform != Selection.activeTransform)
            {
                sourceTransform = Selection.activeTransform;
                // 自动查找对称物体
                bool found = AutoFindSymmetricTransform();

                // 显示查找结果提示
                if (found)
                {
                    ShowNotification(new GUIContent($"已找到对称物体: {targetTransform.name}"));
                }
                else
                {
                    ShowNotification(new GUIContent("未找到对称物体，请手动指定"));
                }

                Repaint(); // 立即刷新窗口
            }
        }
    }
    private void OnGUI()
    {
        mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);
        GUILayout.Label("对称同步控制", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 源物体选择
        EditorGUILayout.LabelField("源物体", EditorStyles.label);
        sourceTransform = (Transform)EditorGUILayout.ObjectField(
            sourceTransform, typeof(Transform), true);

        // 目标物体选择
        EditorGUILayout.LabelField("对称目标物体", EditorStyles.label);
        targetTransform = (Transform)EditorGUILayout.ObjectField(
            targetTransform, typeof(Transform), true);

        // 自动查找按钮
        if (GUILayout.Button("自动查找对称物体") && sourceTransform != null)
        {
            AutoFindSymmetricTransform();
        }

        EditorGUILayout.Space();

        // 对称设置
        symmetryAxis = (SymmetryAxis)EditorGUILayout.EnumPopup("对称轴", symmetryAxis);
        // 匹配模式选择
        currentMatchMode = (MatchMode)EditorGUILayout.EnumPopup("匹配模式", currentMatchMode);
        switch (currentMatchMode)
        {
            case MatchMode.Mixed:
                EditorGUILayout.HelpBox(
                    "混合匹配：优先尝试基于Humanoid骨骼映射查找对称物体，若失败则回退到名称关键词匹配。\n" +
                    "请确保模型有Humanoid类型的Animator组件，且骨骼已正确映射。",
                    MessageType.Info);
                break;
            case MatchMode.BoneOnly:
                EditorGUILayout.HelpBox(
                    "骨骼匹配：仅基于Humanoid骨骼映射查找对称物体。\n" +
                    "必须有Humanoid类型的Animator组件，且骨骼已正确映射，否则无法自动查找。",
                    MessageType.Warning);
                break;
            case MatchMode.NameOnly:
                EditorGUILayout.HelpBox(
                    "名称匹配：仅通过关键词对替换查找对称物体。\n" +
                    "适用于非Humanoid骨架或自定义命名结构。",
                    MessageType.Info);
                break;
        }

        ignorePrefixNumbers = EditorGUILayout.Toggle("忽略前缀数字", ignorePrefixNumbers);

        // 同步内容控制选项
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("同步内容设置", EditorStyles.boldLabel);
        modifyTransform = EditorGUILayout.Toggle("同步Transform（位置/旋转/缩放）", modifyTransform);
        modifyComponents = EditorGUILayout.Toggle("同步组件属性", modifyComponents);
        // 新增选项：自动创建缺失组件
        if (modifyComponents)
        {
            EditorGUI.indentLevel++;
            autoCreateMissingComponents = EditorGUILayout.Toggle(
                "自动创建缺失组件", autoCreateMissingComponents);

            // 显示排除的组件提示
            EditorGUILayout.HelpBox(
                "系统会自动排除基础渲染和变换组件，避免创建冲突",
                MessageType.Info, true);
            EditorGUI.indentLevel--;
        }

        // 关键词对设置
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "对称关键词对设置");
        if (showAdvancedSettings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 显示现有关键词对
            for (int i = 0; i < keywordPairs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                keywordPairs[i].a = EditorGUILayout.TextField(keywordPairs[i].a);
                keywordPairs[i].b = EditorGUILayout.TextField(keywordPairs[i].b);

                if (GUILayout.Button("-", GUILayout.Width(20)))
                {
                    keywordPairs.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            // 添加新关键词对
            if (GUILayout.Button("添加关键词对"))
            {
                keywordPairs.Add(new SymmetryKeywordPair("", ""));
            }

            EditorGUILayout.EndVertical();
        }
        // 数组处理设置
        showArraySettings = EditorGUILayout.Foldout(showArraySettings, "数组类型处理设置");
        if (showArraySettings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("需要递归处理的数组元素类型:", EditorStyles.miniLabel);

            // 显示现有数组类型
            for (int i = 0; i < recursiveArrayTypes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                recursiveArrayTypes[i] = EditorGUILayout.TextField(recursiveArrayTypes[i]);

                if (GUILayout.Button("-", GUILayout.Width(20)))
                {
                    recursiveArrayTypes.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            // 添加新数组类型
            if (GUILayout.Button("添加类型"))
            {
                recursiveArrayTypes.Add("");
            }

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.Space();
        EditorGUILayout.Separator();
        EditorGUILayout.Space();

        // 同步控制
        EditorGUILayout.BeginHorizontal();



        // 同步按钮
        GUI.enabled = CanSync();
        if (GUILayout.Button("执行对称同步") && CanSync())
        {
            PerformSymmetrySync();
            ShowNotification(new GUIContent("同步完成!"));
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
    }

    // 检查是否可以执行同步
    private bool CanSync()
    {
        return sourceTransform != null && targetTransform != null &&
               sourceTransform != targetTransform;
    }

    // 自动查找对称物体
    // 自动查找对称物体（重构版）
    private bool AutoFindSymmetricTransform()
    {
        if (sourceTransform == null) return false;

        // 1. 根据当前匹配模式选择查找策略
        switch (currentMatchMode)
        {
            case MatchMode.Mixed:
                // 混合模式：先试骨骼匹配，失败再试名称匹配
                if (TryBoneBasedFind()) return true;
                return TryNameBasedFind();

            case MatchMode.BoneOnly:
                // 仅骨骼匹配
                return TryBoneBasedFind();

            case MatchMode.NameOnly:
                // 仅名称匹配（原逻辑）
                return TryNameBasedFind();

            default:
                return TryNameBasedFind();
        }
    }
    // 骨骼匹配核心方法
    private bool TryBoneBasedFind()
    {
        // 步骤1：查找关联的Animator并验证是否为Humanoid类型
        Animator animator = GetAssociatedHumanoidAnimator();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            ShowNotification(new GUIContent("未检测到Humanoid类型的Animator组件"));
            return false;
        }

        // 步骤2：确定源骨骼对应的HumanBodyBones
        HumanBodyBones sourceBoneType = GetHumanBodyBoneType(animator, sourceTransform);
        if (sourceBoneType == HumanBodyBones.LastBone)
        {
            ShowNotification(new GUIContent("所选物体不是已映射的Humanoid骨骼"));
            return false;
        }

        // 步骤3：获取对称骨骼类型
        if (!symmetricBoneMap.TryGetValue(sourceBoneType, out HumanBodyBones targetBoneType))
        {
            ShowNotification(new GUIContent("该骨骼没有对称骨骼"));
            return false;
        }

        // 步骤4：获取对称骨骼的Transform
        Transform symmetricBone = animator.GetBoneTransform(targetBoneType);
        if (symmetricBone != null && symmetricBone != sourceTransform)
        {
            targetTransform = symmetricBone;
            ShowNotification(new GUIContent(
                $"骨骼映射匹配成功: {sourceBoneType} → {targetBoneType}"));
            return true;
        }

        ShowNotification(new GUIContent("未找到对应的对称骨骼"));
        return false;
    }


    // 获取关联的Humanoid类型Animator
    private Animator GetAssociatedHumanoidAnimator()
    {
        // 从源物体向上查找Animator
        Transform current = sourceTransform;
        while (current != null)
        {
            Animator anim = current.GetComponent<Animator>();
            if (anim != null)
            {
                return anim;
            }
            current = current.parent;
        }

        // 从场景中查找引用该骨骼的Humanoid Animator
        var allAnimators = UnityEngine.Object.FindObjectsOfType<Animator>();
        foreach (var anim in allAnimators)
        {
            if (anim.avatar != null && anim.avatar.isHuman)
            {
                // 检查该Animator是否包含源骨骼
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = anim.GetBoneTransform((HumanBodyBones)i);
                    if (bone == sourceTransform)
                    {
                        return anim;
                    }
                }
            }
        }

        return null;
    }

    // 确定Transform对应的HumanBodyBones类型
    private HumanBodyBones GetHumanBodyBoneType(Animator animator, Transform bone)
    {
        if (animator == null || !animator.avatar.isHuman || bone == null)
            return HumanBodyBones.LastBone;

        // 遍历所有可能的骨骼类型查找匹配
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            HumanBodyBones boneType = (HumanBodyBones)i;
            if (animator.GetBoneTransform(boneType) == bone)
            {
                return boneType;
            }
        }

        return HumanBodyBones.LastBone;
    }
    // 名称匹配逻辑（提取为独立方法）
    private bool TryNameBasedFind()
    {
        string sourceName = sourceTransform.name;
        if (ignorePrefixNumbers)
        {
            sourceName = RemovePrefixNumbers(sourceName);
        }

        // 尝试用每个关键词对查找对称对象（原逻辑）
        foreach (var pair in keywordPairs)
        {
            if (string.IsNullOrEmpty(pair.a) || string.IsNullOrEmpty(pair.b))
                continue;

            string targetName;
            bool isASide = sourceName.IndexOf(pair.a, StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBSide = sourceName.IndexOf(pair.b, StringComparison.OrdinalIgnoreCase) >= 0;

            if (isASide && !isBSide)
            {
                targetName = ReplaceFirstOccurrence(sourceName, pair.a, pair.b, StringComparison.OrdinalIgnoreCase);
            }
            else if (isBSide && !isASide)
            {
                targetName = ReplaceFirstOccurrence(sourceName, pair.b, pair.a, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                continue;
            }

            // 在同一层级查找
            if (sourceTransform.parent != null)
            {
                Transform found = FindInSameHierarchy(sourceTransform.parent, targetName);
                if (found != null)
                {
                    targetTransform = found;
                    return true;
                }
            }

            // 在整个场景查找
            Transform sceneFound = FindInScene(targetName);
            if (sceneFound != null && sceneFound != sourceTransform)
            {
                targetTransform = sceneFound;
                return true;
            }
        }

        // 名称匹配失败
        targetTransform = null;
        return false;
    }



    // 执行对称同步
    private void PerformSymmetrySync()
    {
        if (!CanSync()) return;

        Undo.RecordObject(targetTransform, "对称同步变换");
        // 仅在需要修改Transform时执行
        if (modifyTransform)
        {
            Undo.RecordObject(targetTransform, "对称同步变换");
            targetTransform.localPosition = GetSymmetricVector(sourceTransform.localPosition);
            targetTransform.localRotation = GetSymmetricQuaternion(sourceTransform.localRotation);
            targetTransform.localScale = sourceTransform.localScale;
        }

        // 增强的组件同步逻辑
        if (modifyComponents)
        {
            Component[] sourceComponents = sourceTransform.GetComponents<Component>();
            foreach (var sourceComp in sourceComponents)
            {
                if (sourceComp == null) continue;

                Type compType = sourceComp.GetType();

                // 跳过排除的组件类型
                if (excludedComponentTypes.Contains(compType))
                    continue;

                // 查找或创建目标组件
                Component targetComp = targetTransform.GetComponent(compType);
                bool isNewComponent = false;

                // 如果需要自动创建且组件不存在
                if (targetComp == null && autoCreateMissingComponents)
                {
                    // 记录撤销操作
                    Undo.AddComponent(targetTransform.gameObject, compType);
                    targetComp = targetTransform.GetComponent(compType);
                    isNewComponent = true;
                }

                // 只处理存在的目标组件（无论是已有的还是新创建的）
                if (targetComp != null)
                {
                    if (!isNewComponent)
                    {
                        Undo.RecordObject(targetComp, $"对称同步 {compType.Name}");
                    }
                    SyncComponentProperties(sourceComp, targetComp);
                }
            }
        }
        // 刷新场景视图
        SceneView.RepaintAll();
    }

    // 同步组件属性
    private void SyncComponentProperties(Component source, Component target)
    {
        Type type = source.GetType();

        // 忽略编辑器组件
        if (type.Assembly == typeof(Editor).Assembly)
            return;

        // 获取所有可读写的公共属性
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsSpecialName &&
                       p.Name != "name" && p.Name != "gameObject").ToArray();

        // 获取公共字段
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsSpecialName && f.Name != "m_Name" && f.Name != "m_GameObject").ToArray();

        // 同步属性
        foreach (var prop in properties)
        {
            SyncProperty(source, target, prop);
        }

        // 同步字段
        foreach (var field in fields)
        {
            SyncField(source, target, field);
        }
    }

    // 同步单个属性
    private void SyncProperty(Component source, Component target, PropertyInfo prop)
    {
        try
        {
            object value = prop.GetValue(source);
            object symmetricValue = GetSymmetricValue(value);
            prop.SetValue(target, symmetricValue);
        }
        catch
        {
            // 忽略无法访问的属性
        }
    }

    // 同步单个字段
    private void SyncField(Component source, Component target, FieldInfo field)
    {
        try
        {
            object value = field.GetValue(source);
            object symmetricValue = GetSymmetricValue(value);
            field.SetValue(target, symmetricValue);
        }
        catch
        {
            // 忽略无法访问的字段
        }
    }

    // 检查组件变化（用于预览）
    private void CheckComponentChanges(Component source, Component target, PreviewData preview)
    {
        Type type = source.GetType();

        if (type.Assembly == typeof(Editor).Assembly)
            return;

        // 检查属性变化
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsSpecialName &&
                       p.Name != "name" && p.Name != "gameObject").ToArray();

        foreach (var prop in properties)
        {
            try
            {
                object originalValue = prop.GetValue(source);
                object targetValue = prop.GetValue(target);
                object symmetricValue = GetSymmetricValue(originalValue);

                if (!AreObjectsEqual(targetValue, symmetricValue))
                {
                    preview.componentChanges.Add(new ComponentChangeInfo
                    {
                        componentType = type.Name,
                        propertyName = prop.Name,
                        originalValue = originalValue,
                        symmetricValue = symmetricValue
                    });
                }
            }
            catch
            {
                // 忽略无法访问的属性
            }
        }

        // 检查字段变化
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsSpecialName && f.Name != "m_Name" && f.Name != "m_GameObject").ToArray();

        foreach (var field in fields)
        {
            try
            {
                object originalValue = field.GetValue(source);
                object targetValue = field.GetValue(target);
                object symmetricValue = GetSymmetricValue(originalValue);

                if (!AreObjectsEqual(targetValue, symmetricValue))
                {
                    preview.componentChanges.Add(new ComponentChangeInfo
                    {
                        componentType = type.Name,
                        propertyName = field.Name,
                        originalValue = originalValue,
                        symmetricValue = symmetricValue
                    });
                }
            }
            catch
            {
                // 忽略无法访问的字段
            }
        }
    }
    // 获取对称值
    private object GetSymmetricValue(object value)
    {
        if (value == null) return null;

        if (value is Vector3 vector3)
            return GetSymmetricVector(vector3);
        else if (value is Quaternion quaternion)
            return GetSymmetricQuaternion(quaternion);
        else if (value is Vector2 vector2)
        {
            if (symmetryAxis == SymmetryAxis.X)
                return new Vector2(-vector2.x, vector2.y);
            else if (symmetryAxis == SymmetryAxis.Y)
                return new Vector2(vector2.x, -vector2.y);
        }
        else if (value is Vector4 vector4)
        {
            if (symmetryAxis == SymmetryAxis.X)
                return new Vector4(-vector4.x, vector4.y, vector4.z, vector4.w);
            else if (symmetryAxis == SymmetryAxis.Y)
                return new Vector4(vector4.x, -vector4.y, vector4.z, vector4.w);
            else if (symmetryAxis == SymmetryAxis.Z)
                return new Vector4(vector4.x, vector4.y, -vector4.z, vector4.w);
        }
        // 处理自定义类数组
        else if (value.GetType().IsArray)
        {
            Type elementType = value.GetType().GetElementType();
            // 检查是否需要递归处理
            bool needRecursive = recursiveArrayTypes.Exists(t =>
                string.Equals(t, elementType.Name, StringComparison.OrdinalIgnoreCase));

            if (!needRecursive)
                return value; // 不需要递归处理的数组直接复制

            // 递归处理数组元素
            Array sourceArray = (Array)value;
            Array targetArray = Array.CreateInstance(elementType, sourceArray.Length);

            for (int i = 0; i < sourceArray.Length; i++)
            {
                targetArray.SetValue(GetSymmetricValue(sourceArray.GetValue(i)), i);
            }
            return targetArray;
        }
        // 处理自定义类（如SphereCollider）
        else if (!value.GetType().IsPrimitive && !value.GetType().IsEnum)
        {
            object symmetricObject = Activator.CreateInstance(value.GetType());
            FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                object fieldValue = field.GetValue(value);
                field.SetValue(symmetricObject, GetSymmetricValue(fieldValue));
            }
            return symmetricObject;
        }

        return value;
    }
    // 添加对象比较辅助方法
    private bool AreObjectsEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // 处理数组比较
        if (a.GetType().IsArray && b.GetType().IsArray)
        {
            Array arrA = (Array)a;
            Array arrB = (Array)b;

            if (arrA.Length != arrB.Length) return false;

            for (int i = 0; i < arrA.Length; i++)
            {
                if (!AreObjectsEqual(arrA.GetValue(i), arrB.GetValue(i)))
                    return false;
            }
            return true;
        }

        // 处理Vector等Unity类型
        if (a is Vector3 vecA && b is Vector3 vecB)
            return Vector3.Equals(vecA, vecB);
        if (a is Quaternion quatA && b is Quaternion quatB)
            return Quaternion.Equals(quatA, quatB);
        if (a is Vector2 vec2A && b is Vector2 vec2B)
            return Vector2.Equals(vec2A, vec2B);

        // 基本类型比较
        return a.Equals(b);
    }
    // 计算对称向量
    private Vector3 GetSymmetricVector(Vector3 original)
    {
        switch (symmetryAxis)
        {
            case SymmetryAxis.X:
                return new Vector3(-original.x, original.y, original.z);
            case SymmetryAxis.Y:
                return new Vector3(original.x, -original.y, original.z);
            case SymmetryAxis.Z:
                return new Vector3(original.x, original.y, -original.z);
            default:
                return original;
        }
    }

    // 计算对称四元数
    private Quaternion GetSymmetricQuaternion(Quaternion original)
    {
        switch (symmetryAxis)
        {
            case SymmetryAxis.X:
                return new Quaternion(-original.x, original.y, original.z, -original.w);
            case SymmetryAxis.Y:
                return new Quaternion(original.x, -original.y, original.z, -original.w);
            case SymmetryAxis.Z:
                return new Quaternion(original.x, original.y, -original.z, -original.w);
            default:
                return original;
        }
    }

    // 去除前缀数字
    private string RemovePrefixNumbers(string originalName)
    {
        if (string.IsNullOrEmpty(originalName))
            return originalName;

        return Regex.Replace(originalName, @"^\d+[.!_]", "", RegexOptions.IgnoreCase);
    }

    // 替换首次出现的字符串
    private string ReplaceFirstOccurrence(string source, string oldValue, string newValue, StringComparison comparisonType)
    {
        int index = source.IndexOf(oldValue, comparisonType);
        if (index < 0)
            return source;

        return source.Remove(index, oldValue.Length).Insert(index, newValue);
    }

    // 在同一层级查找物体
    private Transform FindInSameHierarchy(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            string cleanName = ignorePrefixNumbers ? RemovePrefixNumbers(child.name) : child.name;
            if (cleanName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        return null;
    }

    // 在场景中查找物体
    private Transform FindInScene(string name)
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform found = FindInChildren(root.transform, name);
            if (found != null)
                return found;
        }
        return null;
    }

    // 在子物体中查找
    private Transform FindInChildren(Transform parent, string name)
    {
        string cleanParentName = ignorePrefixNumbers ? RemovePrefixNumbers(parent.name) : parent.name;
        if (cleanParentName.Equals(name, StringComparison.OrdinalIgnoreCase))
            return parent;

        foreach (Transform child in parent)
        {
            Transform found = FindInChildren(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    // 对称轴枚举
    public enum SymmetryAxis
    {
        X, Y, Z
    }

    // 关键词对类
    [Serializable]
    public class SymmetryKeywordPair
    {
        public string a;
        public string b;

        public SymmetryKeywordPair(string a, string b)
        {
            this.a = a;
            this.b = b;
        }
    }

    // 预览数据类
    private class PreviewData
    {
        public Vector3 originalPosition;
        public Quaternion originalRotation;
        public Vector3 originalScale;

        public Vector3 symmetricPosition;
        public Quaternion symmetricRotation;
        public Vector3 symmetricScale;

        public List<ComponentChangeInfo> componentChanges = new List<ComponentChangeInfo>();
    }

    // 组件变化信息类
    private class ComponentChangeInfo
    {
        public string componentType;
        public string propertyName;
        public object originalValue;
        public object symmetricValue;
    }
}
