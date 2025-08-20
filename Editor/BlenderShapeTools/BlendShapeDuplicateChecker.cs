using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VroidMMDTools
{
    public class DuplicateBlendShapeInfo
    {
        public string name;
        public List<int> indices = new List<int>();
        public List<bool> toDelete = new List<bool>();
    }

    // 用于重命名窗口的临时数据
    public class RenameItem
    {
        public int index;
        public string originalName;
        public string newName;
    }

    public class BlendShapeDuplicateChecker : EditorWindow
    {
        private Dictionary<SkinnedMeshRenderer, List<DuplicateBlendShapeInfo>> duplicateBlendShapes
            = new Dictionary<SkinnedMeshRenderer, List<DuplicateBlendShapeInfo>>();
        private Vector2 scrollPosition;
        private bool showHelpInfo = false;
        private bool isProcessing = false;

        // 重命名窗口相关
        private bool isRenaming = false;
        private SkinnedMeshRenderer renameTargetSmr;
        private List<RenameItem> renameItems = new List<RenameItem>();
        private Vector2 renameScrollPosition;

        [MenuItem("VRoidTools/BlendShape Duplicate Checker")]
        public static void ShowWindow()
        {
            GetWindow<BlendShapeDuplicateChecker>("形态键重复检查器");
        }

        private void OnGUI()
        {
            if (isProcessing)
            {
                GUILayout.Label("处理中，请稍候...", EditorStyles.boldLabel);
                return;
            }

            GUILayout.Label("形态键重复检查器", EditorStyles.boldLabel);
            GUILayout.Label("用于检查模型中是否存在重复的形态键，避免VRM导出错误", EditorStyles.helpBox);

            // 重命名窗口模式
            if (isRenaming)
            {
                ShowAdvancedRenameUI();
                return;
            }

            // 主窗口模式
            if (GUILayout.Button("检查选中对象", GUILayout.Height(30)))
            {
                CheckSelectedObjects();
            }

            showHelpInfo = EditorGUILayout.Foldout(showHelpInfo, "使用帮助");
            if (showHelpInfo)
            {
                EditorGUILayout.HelpBox(
                    "1. 选择场景中的模型对象并点击\"检查选中对象\"\n" +
                    "2. 对于重复形态键，可以：\n" +
                    "   - 勾选要删除的具体项，点击\"删除选中项\"\n" +
                    "   - 点击\"单独重命名\"，为每个重复项指定新名称\n" +
                    "3. 所有修改会创建新网格，原资源不会被改变\n\n" +
                    "注意：操作前请备份资源",
                    MessageType.Info);
            }

            ShowResults();
        }

        // 高级重命名界面 - 允许为每个重复项单独指定名称
        private void ShowAdvancedRenameUI()
        {
            GUILayout.Label("形态键单独重命名", EditorStyles.boldLabel);
            GUILayout.Label($"目标组件: {renameTargetSmr?.name}", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("为每个重复的形态键指定新名称（不可为空且不能重复）", MessageType.Info);

            renameScrollPosition = EditorGUILayout.BeginScrollView(renameScrollPosition);

            foreach (var item in renameItems)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"索引 {item.index}:", GUILayout.Width(60));
                GUILayout.Label(item.originalName, GUILayout.Width(100));
                item.newName = EditorGUILayout.TextField(item.newName);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("确认重命名"))
            {
                // 验证新名称
                var newNames = renameItems.Select(i => i.newName).ToList();

                // 检查空名称
                if (newNames.Any(string.IsNullOrEmpty))
                {
                    EditorUtility.DisplayDialog("错误", "新名称不能为空", "确定");
                    return;
                }

                // 检查重复名称
                if (newNames.Distinct().Count() != newNames.Count)
                {
                    EditorUtility.DisplayDialog("错误", "新名称不能重复", "确定");
                    return;
                }

                // 检查与其他形态键名称冲突
                if (renameTargetSmr != null && renameTargetSmr.sharedMesh != null)
                {
                    var existingNames = new HashSet<string>();
                    for (int i = 0; i < renameTargetSmr.sharedMesh.blendShapeCount; i++)
                    {
                        // 排除当前正在重命名的形态键
                        if (!renameItems.Any(item => item.index == i))
                        {
                            existingNames.Add(renameTargetSmr.sharedMesh.GetBlendShapeName(i));
                        }
                    }

                    var conflicts = newNames.Where(n => existingNames.Contains(n)).ToList();
                    if (conflicts.Count > 0)
                    {
                        EditorUtility.DisplayDialog("错误",
                            $"新名称与其他形态键冲突: {string.Join(", ", conflicts)}", "确定");
                        return;
                    }
                }

                // 执行重命名
                isProcessing = true;
                PerformAdvancedRename();
                isRenaming = false;
                renameTargetSmr = null;
                renameItems.Clear();
                isProcessing = false;
            }

            if (GUILayout.Button("取消"))
            {
                isRenaming = false;
                renameTargetSmr = null;
                renameItems.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CheckSelectedObjects()
        {
            duplicateBlendShapes.Clear();

            if (Selection.objects == null || Selection.objects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个或多个模型对象", "确定");
                return;
            }

            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject gameObject)
                {
                    CheckGameObject(gameObject);
                }
            }

            if (duplicateBlendShapes.Count == 0)
            {
                EditorUtility.DisplayDialog("检查完成", "未发现重复的形态键", "确定");
            }
            else
            {
                int totalDuplicates = duplicateBlendShapes.Sum(kvp => kvp.Value.Sum(info => info.indices.Count - 1));
                EditorUtility.DisplayDialog("检查完成", $"发现 {duplicateBlendShapes.Count} 个组件包含重复形态键，共 {totalDuplicates} 个重复项", "确定");
            }

            Repaint();
        }

        private void CheckGameObject(GameObject gameObject)
        {
            var skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var smr in skinnedMeshRenderers)
            {
                CheckSkinnedMeshRenderer(smr);
            }
        }

        private void CheckSkinnedMeshRenderer(SkinnedMeshRenderer smr)
        {
            if (smr.sharedMesh == null) return;

            int blendShapeCount = smr.sharedMesh.blendShapeCount;
            if (blendShapeCount == 0) return;

            Dictionary<string, List<int>> blendShapeMap = new Dictionary<string, List<int>>();

            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = smr.sharedMesh.GetBlendShapeName(i);
                if (!blendShapeMap.ContainsKey(name))
                {
                    blendShapeMap[name] = new List<int>();
                }
                blendShapeMap[name].Add(i);
            }

            var duplicates = blendShapeMap.Where(kvp => kvp.Value.Count > 1)
                                         .Select(kvp => new DuplicateBlendShapeInfo
                                         {
                                             name = kvp.Key,
                                             indices = kvp.Value,
                                             toDelete = kvp.Value.Select((_, i) => i > 0).ToList()
                                         })
                                         .ToList();

            if (duplicates.Count > 0)
            {
                duplicateBlendShapes[smr] = duplicates;
            }
        }

        private void ShowResults()
        {
            EditorGUILayout.Space();
            GUILayout.Label("检查结果:", EditorStyles.boldLabel);

            if (duplicateBlendShapes.Count == 0)
            {
                GUILayout.Label("未发现重复的形态键", EditorStyles.label);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var kvp in duplicateBlendShapes.ToList())
            {
                SkinnedMeshRenderer smr = kvp.Key;
                List<DuplicateBlendShapeInfo> duplicates = kvp.Value;

                if (smr == null || duplicates == null)
                {
                    duplicateBlendShapes.Remove(smr);
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"组件: {smr.name}", EditorStyles.boldLabel);
                GUILayout.Label($"所在对象: {smr.gameObject.name}", EditorStyles.miniLabel);
                GUILayout.Label($"总形态键数量: {smr.sharedMesh.blendShapeCount}", EditorStyles.miniLabel);

                foreach (var info in duplicates.ToList())
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"形态键名称: '{info.name}'", EditorStyles.boldLabel);
                    GUILayout.Label($"重复次数: {info.indices.Count} 次", EditorStyles.miniLabel);

                    // 显示所有重复项
                    EditorGUILayout.LabelField("重复项列表 (可选择删除):");
                    for (int i = 0; i < info.indices.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        bool isLastItem = i == info.indices.Count - 1;
                        bool canDelete = !isLastItem;

                        // 保护机制：至少保留一个
                        if (isLastItem && info.toDelete.Take(i).All(x => x))
                        {
                            info.toDelete[i] = false;
                            GUI.enabled = false;
                        }

                        info.toDelete[i] = EditorGUILayout.ToggleLeft(
                            $"索引 {info.indices[i]} (帧数量: {smr.sharedMesh.GetBlendShapeFrameCount(info.indices[i])})",
                            info.toDelete[i],
                            GUILayout.Width(350));

                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("单独重命名", GUILayout.Width(120)))
                    {
                        // 准备重命名数据
                        renameTargetSmr = smr;
                        renameItems.Clear();
                        foreach (var index in info.indices)
                        {
                            renameItems.Add(new RenameItem
                            {
                                index = index,
                                originalName = info.name,
                                newName = $"{info.name}_{index}" // 默认建议名称
                            });
                        }
                        isRenaming = true;
                    }

                    if (GUILayout.Button("删除选中项", GUILayout.Width(100)))
                    {
                        if (info.toDelete.All(x => x))
                        {
                            EditorUtility.DisplayDialog("错误", "至少需要保留一个形态键", "确定");
                        }
                        else
                        {
                            isProcessing = true;
                            RemoveSelectedBlendShapes(smr, info);
                            isProcessing = false;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }

        // 执行精细重命名
        private void PerformAdvancedRename()
        {
            if (renameTargetSmr == null || renameTargetSmr.sharedMesh == null || renameItems.Count == 0)
                return;

            Mesh originalMesh = renameTargetSmr.sharedMesh;
            Mesh newMesh = CreateMeshCopy(originalMesh);
            newMesh.name = originalMesh.name + "_renamed";

            // 创建重命名映射表
            var renameMap = renameItems.ToDictionary(item => item.index, item => item.newName);

            // 复制所有形态键，对目标索引应用新名称
            for (int i = 0; i < originalMesh.blendShapeCount; i++)
            {
                string blendShapeName = originalMesh.GetBlendShapeName(i);

                // 如果是需要重命名的索引，使用新名称
                if (renameMap.ContainsKey(i))
                {
                    blendShapeName = renameMap[i];
                }

                // 复制帧数据
                for (int frame = 0; frame < originalMesh.GetBlendShapeFrameCount(i); frame++)
                {
                    Vector3[] vertices = new Vector3[originalMesh.vertexCount];
                    Vector3[] normals = new Vector3[originalMesh.vertexCount];
                    Vector3[] tangents = new Vector3[originalMesh.vertexCount];

                    originalMesh.GetBlendShapeFrameVertices(i, frame, vertices, normals, tangents);
                    float weight = originalMesh.GetBlendShapeFrameWeight(i, frame);

                    newMesh.AddBlendShapeFrame(blendShapeName, weight, vertices, normals, tangents);
                }
            }

            SaveAndReplaceMesh(renameTargetSmr, originalMesh, newMesh);
            CheckSelectedObjects();
            EditorUtility.DisplayDialog("完成", $"已成功重命名 {renameItems.Count} 个形态键", "确定");
        }

        // 删除选中的形态键
        private void RemoveSelectedBlendShapes(SkinnedMeshRenderer smr, DuplicateBlendShapeInfo info)
        {
            if (smr == null || smr.sharedMesh == null || info == null) return;

            Mesh originalMesh = smr.sharedMesh;
            Mesh newMesh = CreateMeshCopy(originalMesh);
            newMesh.name = originalMesh.name + "_fixed";

            // 记录需要删除的索引
            HashSet<int> indicesToRemove = new HashSet<int>();
            for (int i = 0; i < info.indices.Count; i++)
            {
                if (info.toDelete[i])
                {
                    indicesToRemove.Add(info.indices[i]);
                }
            }

            // 复制形态键，跳过需要删除的
            int removeCount = 0;
            for (int i = 0; i < originalMesh.blendShapeCount; i++)
            {
                if (indicesToRemove.Contains(i))
                {
                    removeCount++;
                    continue;
                }

                string blendShapeName = originalMesh.GetBlendShapeName(i);

                for (int frame = 0; frame < originalMesh.GetBlendShapeFrameCount(i); frame++)
                {
                    Vector3[] vertices = new Vector3[originalMesh.vertexCount];
                    Vector3[] normals = new Vector3[originalMesh.vertexCount];
                    Vector3[] tangents = new Vector3[originalMesh.vertexCount];

                    originalMesh.GetBlendShapeFrameVertices(i, frame, vertices, normals, tangents);
                    float weight = originalMesh.GetBlendShapeFrameWeight(i, frame);

                    newMesh.AddBlendShapeFrame(blendShapeName, weight, vertices, normals, tangents);
                }
            }

            SaveAndReplaceMesh(smr, originalMesh, newMesh);
            CheckSelectedObjects();
            EditorUtility.DisplayDialog("完成", $"已删除 {removeCount} 个选中的形态键", "确定");
        }

        private Mesh CreateMeshCopy(Mesh original)
        {
            Mesh copy = new Mesh();
            copy.name = original.name;

            // 基础网格数据
            copy.vertices = original.vertices;
            copy.normals = original.normals;
            copy.tangents = original.tangents;
            copy.colors = original.colors;
            copy.colors32 = original.colors32;

            // UV通道
            copy.uv = original.uv;
            copy.uv2 = original.uv2;
            copy.uv3 = original.uv3;
            copy.uv4 = original.uv4;
            copy.uv5 = original.uv5;
            copy.uv6 = original.uv6;
            copy.uv7 = original.uv7;
            copy.uv8 = original.uv8;

            // 骨骼相关数据
            copy.bindposes = original.bindposes;
            copy.boneWeights = original.boneWeights;

            // 子网格数据
            copy.subMeshCount = original.subMeshCount;
            for (int i = 0; i < original.subMeshCount; i++)
            {
                copy.SetTriangles(original.GetTriangles(i), i);
                copy.SetSubMesh(i, original.GetSubMesh(i));
            }

            // 其他属性
            copy.bounds = original.bounds;
            copy.indexFormat = original.indexFormat;

            return copy;
        }

        private void SaveAndReplaceMesh(SkinnedMeshRenderer smr, Mesh originalMesh, Mesh newMesh)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存修改后的网格",
                newMesh.name,
                "asset",
                "请保存修改后的网格资源");

            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(newMesh);
                return;
            }

            AssetDatabase.CreateAsset(newMesh, path);
            Undo.RecordObject(smr, "Replace mesh with modified version");
            smr.sharedMesh = newMesh;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
