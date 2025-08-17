using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

[CustomEditor(typeof(SymmetrySync))]
public class SymmetrySyncEditor : Editor
{
    private static List<SymmetrySync> activeSymmetryObjects = new List<SymmetrySync>();
    private static bool isListening = false;
    private static bool isProcessingChange = false; // 防止循环同步

    private SymmetrySync symmetrySync;

    private void OnEnable()
    {
        symmetrySync = (SymmetrySync)target;
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();

        base.OnInspectorGUI();

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }
    }

    public static void RegisterSymmetryObject(SymmetrySync obj)
    {
        if (obj == null) return;

        if (!activeSymmetryObjects.Contains(obj))
        {
            activeSymmetryObjects.Add(obj);
            StartListening();
        }
    }

    public static void UnregisterSymmetryObject(SymmetrySync obj)
    {
        if (obj != null && activeSymmetryObjects.Contains(obj))
        {
            activeSymmetryObjects.Remove(obj);
            if (activeSymmetryObjects.Count == 0)
            {
                StopListening();
            }
        }
    }

    private static void StartListening()
    {
        if (!isListening)
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update += CheckForChanges;
            isListening = true;
        }
    }

    private static void StopListening()
    {
        if (isListening)
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.update -= CheckForChanges;
            isListening = false;
            previousStates.Clear();
        }
    }

    private static void OnHierarchyChanged()
    {
        // 层级变化时清理状态缓存
        previousStates.Clear();
    }

    private static Dictionary<Transform, TransformState> previousStates = new Dictionary<Transform, TransformState>();

    private static void CheckForChanges()
    {
        if (isProcessingChange) return;

        foreach (var symmetryObj in activeSymmetryObjects.ToArray())
        {
            if (symmetryObj == null) continue;

            // 检查所有子对象的变化
            CheckTransformHierarchy(symmetryObj.transform, symmetryObj);
        }
    }

    private static void CheckTransformHierarchy(Transform parent, SymmetrySync symmetrySync)
    {
        if (parent == null || symmetrySync == null) return;

        CheckAndSyncTransform(parent, symmetrySync);

        // 递归检查子对象
        foreach (Transform child in parent)
        {
            CheckTransformHierarchy(child, symmetrySync);
        }
    }

    private static void CheckAndSyncTransform(Transform target, SymmetrySync symmetrySync)
    {
        if (target == null || symmetrySync == null || !symmetrySync.enableSymmetry) return;

        // 保存当前状态
        TransformState currentState = new TransformState(target);

        // 检查是否有变化
        if (previousStates.TryGetValue(target, out TransformState previousState))
        {
            if (!previousState.Equals(currentState))
            {
                // 有变化，尝试同步到对称对象
                SyncToSymmetricTransform(target, symmetrySync);
            }
        }
        else
        {
            // 首次记录状态
            previousStates[target] = currentState;
        }
    }

    private static void SyncToSymmetricTransform(Transform target, SymmetrySync symmetrySync)
    {
        if (isProcessingChange) return;

        isProcessingChange = true;

        try
        {
            Transform symmetricTransform = symmetrySync.FindSymmetricTransform(target);
            if (symmetricTransform == null) return;

            // 记录对称对象当前状态，防止立即触发反向同步
            previousStates[symmetricTransform] = new TransformState(symmetricTransform);

            // 同步Transform属性（位置、旋转、缩放）
            Undo.RecordObject(symmetricTransform, "Symmetry Sync Transform");

            // 位置对称
            symmetricTransform.localPosition = symmetrySync.GetSymmetricVector(target.localPosition);

            // 旋转对称
            symmetricTransform.localRotation = symmetrySync.GetSymmetricQuaternion(target.localRotation);

            // 缩放通常不需要对称翻转，直接复制
            symmetricTransform.localScale = target.localScale;

            // 同步所有组件的公共属性
            SyncComponents(target, symmetricTransform, symmetrySync);
        }
        finally
        {
            // 更新源对象状态
            previousStates[target] = new TransformState(target);
            isProcessingChange = false;
        }
    }

    private static void SyncComponents(Transform source, Transform target, SymmetrySync symmetrySync)
    {
        Component[] sourceComponents = source.GetComponents<Component>();

        foreach (var sourceComp in sourceComponents)
        {
            if (sourceComp == null || sourceComp is Transform) continue;

            // 查找目标上的相同类型组件
            var targetComp = target.GetComponent(sourceComp.GetType());
            if (targetComp == null) continue;

            // 同步属性
            SyncComponentProperties(sourceComp, targetComp, symmetrySync);
        }
    }

    private static void SyncComponentProperties(Component source, Component target, SymmetrySync symmetrySync)
    {
        Type type = source.GetType();

        // 忽略编辑器组件
        if (type.Assembly == typeof(Editor).Assembly)
            return;

        // 获取所有公共属性和字段
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsSpecialName).ToArray();

        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsSpecialName).ToArray();

        if (properties.Length == 0 && fields.Length == 0)
            return;

        Undo.RecordObject(target, $"Symmetry Sync {type.Name}");

        // 同步属性
        foreach (var prop in properties)
        {
            SyncProperty(source, target, prop, symmetrySync);
        }

        // 同步字段
        foreach (var field in fields)
        {
            SyncField(source, target, field, symmetrySync);
        }
    }

    private static void SyncProperty(Component source, Component target, PropertyInfo prop, SymmetrySync symmetrySync)
    {
        try
        {
            object value = prop.GetValue(source);
            object symmetricValue = GetSymmetricValue(value, symmetrySync);
            prop.SetValue(target, symmetricValue);
        }
        catch
        {
            // 忽略无法访问的属性
        }
    }

    private static void SyncField(Component source, Component target, FieldInfo field, SymmetrySync symmetrySync)
    {
        try
        {
            object value = field.GetValue(source);
            object symmetricValue = GetSymmetricValue(value, symmetrySync);
            field.SetValue(target, symmetricValue);
        }
        catch
        {
            // 忽略无法访问的字段
        }
    }

    private static object GetSymmetricValue(object value, SymmetrySync symmetrySync)
    {
        if (value == null) return null;

        // 处理Vector3类型，进行对称转换
        if (value is Vector3 vector3)
        {
            return symmetrySync.GetSymmetricVector(vector3);
        }
        // 处理Quaternion类型，进行对称转换
        else if (value is Quaternion quaternion)
        {
            return symmetrySync.GetSymmetricQuaternion(quaternion);
        }
        // 处理Vector2类型
        else if (value is Vector2 vector2)
        {
            if (symmetrySync.symmetryAxis == SymmetrySync.SymmetryAxis.X)
                return new Vector2(-vector2.x, vector2.y);
            else if (symmetrySync.symmetryAxis == SymmetrySync.SymmetryAxis.Y)
                return new Vector2(vector2.x, -vector2.y);
        }
        // 处理Vector4类型
        else if (value is Vector4 vector4)
        {
            if (symmetrySync.symmetryAxis == SymmetrySync.SymmetryAxis.X)
                return new Vector4(-vector4.x, vector4.y, vector4.z, vector4.w);
            else if (symmetrySync.symmetryAxis == SymmetrySync.SymmetryAxis.Y)
                return new Vector4(vector4.x, -vector4.y, vector4.z, vector4.w);
            else if (symmetrySync.symmetryAxis == SymmetrySync.SymmetryAxis.Z)
                return new Vector4(vector4.x, vector4.y, -vector4.z, vector4.w);
        }
        // 其他类型直接复制
        return value;
    }

    // 用于存储和比较Transform状态的辅助类
    private class TransformState
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;

        public TransformState(Transform transform)
        {
            localPosition = transform.localPosition;
            localRotation = transform.localRotation;
            localScale = transform.localScale;
        }

        public bool Equals(TransformState other)
        {
            if (other == null) return false;
            return Approximately(localPosition, other.localPosition) &&
                   Approximately(localRotation, other.localRotation) &&
                   Approximately(localScale, other.localScale);
        }

        // 近似比较，考虑浮点数精度问题
        private bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y) &&
                   Mathf.Approximately(a.z, b.z);
        }

        private bool Approximately(Quaternion a, Quaternion b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y) &&
                   Mathf.Approximately(a.z, b.z) &&
                   Mathf.Approximately(a.w, b.w);
        }
    }
}
