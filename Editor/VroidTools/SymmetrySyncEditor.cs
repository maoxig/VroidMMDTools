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
        new SymmetryKeywordPair ("L", "R"),
        new SymmetryKeywordPair ("左側", "右側"),
        new SymmetryKeywordPair ("左の", "右の")
    };
    private bool ignorePrefixNumbers = true;
    private bool showArraySettings;
    private List<string> recursiveArrayTypes = new List<string>
{
    "SphereCollider" // 默认包含VRoid碰撞体类
};
    // 面板状态
    private Vector2 scrollPosition;
    private bool showAdvancedSettings;
    private bool showSyncPreview;

    // 预览数据
    private PreviewData previewData;

    [MenuItem("VRoidTools/对称同步工具")]
    public static void ShowWindow()
    {
        SymmetrySyncWindow window = GetWindow<SymmetrySyncWindow>("对称同步工具");
        window.minSize = new Vector2(300, 400); // 设置最小尺寸
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

                // 清除预览
                previewData = null;
                showSyncPreview = false;
                Repaint(); // 立即刷新窗口
            }
        }
    }

    private void OnGUI()
    {
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
        ignorePrefixNumbers = EditorGUILayout.Toggle("忽略前缀数字", ignorePrefixNumbers);

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

        // 预览按钮
        if (GUILayout.Button("预览同步效果") && CanSync())
        {
            GenerateSyncPreview();
            showSyncPreview = true;
        }

        // 同步按钮
        GUI.enabled = CanSync();
        if (GUILayout.Button("执行对称同步") && CanSync())
        {
            PerformSymmetrySync();
            ShowNotification(new GUIContent("同步完成!"));
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // 显示预览
        if (showSyncPreview && previewData != null)
        {
            EditorGUILayout.Space();
            GUILayout.Label("同步预览", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // 显示变换预览
            EditorGUILayout.LabelField("变换同步:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"位置: {previewData.originalPosition} → {previewData.symmetricPosition}");
            EditorGUILayout.LabelField($"旋转: {previewData.originalRotation.eulerAngles} → {previewData.symmetricRotation.eulerAngles}");
            EditorGUILayout.LabelField($"缩放: {previewData.originalScale} → {previewData.symmetricScale}");

            // 显示组件同步预览
            if (previewData.componentChanges.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("组件变化:", EditorStyles.boldLabel);

                foreach (var change in previewData.componentChanges)
                {
                    EditorGUILayout.LabelField($"{change.componentType}: {change.propertyName}");
                    EditorGUILayout.HelpBox($"从 {change.originalValue} 变为 {change.symmetricValue}", MessageType.Info);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    // 检查是否可以执行同步
    private bool CanSync()
    {
        return sourceTransform != null && targetTransform != null &&
               sourceTransform != targetTransform;
    }

    // 自动查找对称物体
    private bool AutoFindSymmetricTransform()
    {
        if (sourceTransform == null) return false;

        string sourceName = sourceTransform.name;
        if (ignorePrefixNumbers)
        {
            sourceName = RemovePrefixNumbers(sourceName);
        }

        // 尝试用每个关键词对查找对称对象
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

        // 如果没找到，清除目标
        targetTransform = null;
        return false;
    }

    // 生成同步预览
    private void GenerateSyncPreview()
    {
        if (!CanSync()) return;

        previewData = new PreviewData();

        // 记录变换预览
        previewData.originalPosition = sourceTransform.localPosition;
        previewData.originalRotation = sourceTransform.localRotation;
        previewData.originalScale = sourceTransform.localScale;

        previewData.symmetricPosition = GetSymmetricVector(sourceTransform.localPosition);
        previewData.symmetricRotation = GetSymmetricQuaternion(sourceTransform.localRotation);
        previewData.symmetricScale = sourceTransform.localScale; // 缩放通常不翻转

        // 记录组件变化预览
        Component[] sourceComponents = sourceTransform.GetComponents<Component>();
        foreach (var sourceComp in sourceComponents)
        {
            if (sourceComp == null || sourceComp is Transform) continue;

            var targetComp = targetTransform.GetComponent(sourceComp.GetType());
            if (targetComp == null) continue;

            // 检查组件属性变化
            CheckComponentChanges(sourceComp, targetComp, previewData);
        }
    }

    // 执行对称同步
    private void PerformSymmetrySync()
    {
        if (!CanSync()) return;

        Undo.RecordObject(targetTransform, "对称同步变换");

        // 同步变换
        targetTransform.localPosition = GetSymmetricVector(sourceTransform.localPosition);
        targetTransform.localRotation = GetSymmetricQuaternion(sourceTransform.localRotation);
        targetTransform.localScale = sourceTransform.localScale;

        // 同步组件
        Component[] sourceComponents = sourceTransform.GetComponents<Component>();
        foreach (var sourceComp in sourceComponents)
        {
            if (sourceComp == null || sourceComp is Transform) continue;

            var targetComp = targetTransform.GetComponent(sourceComp.GetType());
            if (targetComp == null) continue;

            Undo.RecordObject(targetComp, $"对称同步 {sourceComp.GetType().Name}");
            SyncComponentProperties(sourceComp, targetComp);
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
