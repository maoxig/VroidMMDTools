using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ModelProcessorWindow : EditorWindow
{
    private GameObject selectedObject;
    private List<SkinnedMeshRenderer> skinnedRenderers = new List<SkinnedMeshRenderer>();
    private Dictionary<SkinnedMeshRenderer, bool> rendererCheckStates = new Dictionary<SkinnedMeshRenderer, bool>();
    private Vector2 scrollPosition;

    // MMD日语形态键相关字符（aiueo对应的日语字符）
    private string[] mmdBlendShapeNames = new string[] { "あ", "い", "う", "え", "お", "ア", "イ", "ウ", "エ", "オ" };

    [MenuItem("VRoidTools/模型形态键处理器")]
    public static void ShowWindow()
    {
        GetWindow<ModelProcessorWindow>("模型形态键处理器");
    }

    private void OnEnable()
    {
        // 注册选择变更事件
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        // 取消注册选择变更事件
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        if (Selection.activeGameObject != null)
        {
            selectedObject = Selection.activeGameObject;
            FindSkinnedMeshRenderers(selectedObject);
        }
        else
        {
            selectedObject = null;
            skinnedRenderers.Clear();
            rendererCheckStates.Clear();
        }
        Repaint();
    }

    private void FindSkinnedMeshRenderers(GameObject root)
    {
        skinnedRenderers.Clear();
        rendererCheckStates.Clear();

        // 查找所有SkinnedMeshRenderer组件，包括隐藏的
        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        skinnedRenderers.AddRange(renderers);

        // 初始化勾选状态
        foreach (var renderer in skinnedRenderers)
        {
            rendererCheckStates[renderer] = true;
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("模型处理器", EditorStyles.boldLabel);

        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("请在层级面板中选择一个模型", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("选中的模型:", selectedObject.name);
        EditorGUILayout.Space();

        GUILayout.Label("Skinned Mesh Renderers (层级结构):", EditorStyles.boldLabel);

        if (skinnedRenderers.Count == 0)
        {
            EditorGUILayout.HelpBox("未找到SkinnedMeshRenderer组件", MessageType.Info);
        }
        else
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // 只显示包含SkinnedMeshRenderer的层级结构
            DisplayRelevantHierarchy(selectedObject.transform);

            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();

        // 展平处理按钮
        if (GUILayout.Button("处理选中的组件 (展平)"))
        {
            ProcessSelectedRenderers();
        }

        // 重命名Body按钮
        if (GUILayout.Button("重命名日语形态键对象为Body"))
        {
            RenameMMDBlendShapeObjects();
        }

        EditorGUILayout.EndHorizontal();
    }

    // 只显示包含SkinnedMeshRenderer的层级结构
    private void DisplayRelevantHierarchy(Transform parent)
    {
        // 检查当前节点是否有SkinnedMeshRenderer或其下有相关节点
        bool hasRelevantChild = HasSkinnedMeshRendererInHierarchy(parent);

        if (!hasRelevantChild)
            return;

        foreach (Transform child in parent)
        {
            SkinnedMeshRenderer renderer = child.GetComponent<SkinnedMeshRenderer>();
            bool isRelevant = renderer != null && skinnedRenderers.Contains(renderer);
            bool hasRelevantChildren = HasSkinnedMeshRendererInHierarchy(child);

            // 只处理有相关组件或有相关子节点的对象
            if (isRelevant || hasRelevantChildren)
            {
                EditorGUILayout.BeginHorizontal();

                // 缩进显示层级
                int indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = GetDepth(child);

                if (isRelevant)
                {
                    // 显示勾选框和名称（对于有SkinnedMeshRenderer的对象）
                    rendererCheckStates[renderer] = EditorGUILayout.ToggleLeft(
                        child.name,
                        rendererCheckStates[renderer]
                    );
                }
                else
                {
                    // 显示没有SkinnedMeshRenderer但有相关子节点的父对象
                    EditorGUILayout.LabelField(child.name);
                }

                EditorGUI.indentLevel = indentLevel;
                EditorGUILayout.EndHorizontal();

                // 递归显示子对象
                if (child.childCount > 0)
                {
                    DisplayRelevantHierarchy(child);
                }
            }
        }
    }

    // 检查该节点或其子节点是否包含SkinnedMeshRenderer
    private bool HasSkinnedMeshRendererInHierarchy(Transform transform)
    {
        // 检查自身是否有SkinnedMeshRenderer
        if (transform.GetComponent<SkinnedMeshRenderer>() != null &&
            skinnedRenderers.Contains(transform.GetComponent<SkinnedMeshRenderer>()))
        {
            return true;
        }

        // 检查子节点
        foreach (Transform child in transform)
        {
            if (HasSkinnedMeshRendererInHierarchy(child))
            {
                return true;
            }
        }

        return false;
    }

    private int GetDepth(Transform transform)
    {
        int depth = 0;
        Transform current = transform.parent;
        while (current != selectedObject.transform && current != null)
        {
            depth++;
            current = current.parent;
        }
        return depth;
    }

    private void ProcessSelectedRenderers()
    {
        if (selectedObject == null) return;

        // 收集所有被勾选的渲染器
        List<SkinnedMeshRenderer> selectedRenderers = skinnedRenderers
            .Where(r => rendererCheckStates[r])
            .ToList();

        if (selectedRenderers.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请至少勾选一个SkinnedMeshRenderer组件", "确定");
            return;
        }

        // 记录撤销点
        Undo.SetCurrentGroupName("展平模型组件");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var renderer in selectedRenderers)
        {
            GameObject originalObject = renderer.gameObject;

            // 创建新对象，放在根目录
            GameObject newObject = new GameObject(originalObject.name);
            newObject.transform.SetParent(selectedObject.transform);

            // 复制变换信息
            newObject.transform.localPosition = originalObject.transform.localPosition;
            newObject.transform.localRotation = originalObject.transform.localRotation;
            newObject.transform.localScale = originalObject.transform.localScale;

            // 记录新对象的创建，支持撤销
            Undo.RegisterCreatedObjectUndo(newObject, "创建展平对象");

            // 复制所有组件
            CopyComponents(originalObject, newObject);

            // 删除旧对象
            Undo.DestroyObjectImmediate(originalObject);
        }

        // 完成撤销组
        Undo.CollapseUndoOperations(undoGroup);

        // 刷新视图
        OnSelectionChanged();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完成", "模型展平处理已完成", "确定");
    }

    private void CopyComponents(GameObject source, GameObject destination)
    {
        // 复制所有组件
        Component[] components = source.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null) continue;

            // 跳过Transform组件，因为我们已经设置了
            if (component is Transform) continue;

            // 添加相同类型的组件
            Component newComponent = destination.AddComponent(component.GetType());

            // 复制组件值（类似Copy Component和Paste Component Values）
            EditorUtility.CopySerialized(component, newComponent);
        }
    }

    private void RenameMMDBlendShapeObjects()
    {
        if (selectedObject == null) return;

        List<GameObject> renamedObjects = new List<GameObject>();

        // 记录撤销点
        Undo.SetCurrentGroupName("重命名MMD形态键对象");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var renderer in skinnedRenderers)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null) continue;

            // 检查是否包含任何日语形态键
            bool hasMMDBlendShapes = false;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                if (mmdBlendShapeNames.Any(name => blendShapeName.Contains(name)))
                {
                    hasMMDBlendShapes = true;
                    break;
                }
            }

            if (hasMMDBlendShapes && renderer.gameObject.name != "Body")
            {
                // 重命名对象
                Undo.RecordObject(renderer.gameObject, "重命名为Body");
                renderer.gameObject.name = "Body";
                renamedObjects.Add(renderer.gameObject);
            }
        }

        // 完成撤销组
        Undo.CollapseUndoOperations(undoGroup);

        if (renamedObjects.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "完成",
                $"已将 {renamedObjects.Count} 个包含日语形态键的对象重命名为 'Body'",
                "确定"
            );
        }
        else
        {
            EditorUtility.DisplayDialog(
                "提示",
                "未找到包含日语形态键的对象",
                "确定"
            );
        }

        // 刷新视图
        Repaint();
    }
}
