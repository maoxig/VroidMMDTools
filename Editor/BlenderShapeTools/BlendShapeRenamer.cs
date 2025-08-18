using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRoidTools
{
    public class BlendShapeRenamer : EditorWindow
    {
        private SkinnedMeshRenderer targetSmr;
        private List<BlendShapeItem> blendShapeItems = new List<BlendShapeItem>();
        private Vector2 scrollPosition;
        private bool showHelpInfo = false;
        private bool isProcessing = false;

        // 批量处理选项

        private bool trimWhitespace = true;

        // 用于存储单个形态键信息
        private class BlendShapeItem
        {
            public int index;
            public string originalName;
            public string newName;
        }

        [MenuItem("VRoidTools/BlendShape Renamer")]
        public static void ShowWindow()
        {
            GetWindow<BlendShapeRenamer>("形态键批量重命名工具");
        }

        private void OnEnable()
        {
            // 选中对象变化时更新
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeGameObject != null)
            {
                // 尝试获取选中对象或其子对象中的SkinnedMeshRenderer
                targetSmr = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
                if (targetSmr == null)
                {
                    targetSmr = Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>();
                }
            }
            else
            {
                targetSmr = null;
            }

            RefreshBlendShapeList();
        }

        private void RefreshBlendShapeList()
        {
            blendShapeItems.Clear();

            if (targetSmr != null && targetSmr.sharedMesh != null)
            {
                Mesh mesh = targetSmr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    blendShapeItems.Add(new BlendShapeItem
                    {
                        index = i,
                        originalName = mesh.GetBlendShapeName(i),
                        newName = mesh.GetBlendShapeName(i)
                    });
                }
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (isProcessing)
            {
                GUILayout.Label("处理中，请稍候...", EditorStyles.boldLabel);
                return;
            }

            GUILayout.Label("形态键批量重命名工具", EditorStyles.boldLabel);
            GUILayout.Label("用于精细化、批量化重命名选中对象的形态键", EditorStyles.helpBox);

            // 显示当前选中的对象信息
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("当前目标: ", EditorStyles.label);
            if (targetSmr != null)
            {
                GUILayout.Label($"{targetSmr.gameObject.name} (形态键数量: {blendShapeItems.Count})", EditorStyles.boldLabel);
            }
            else
            {
                GUILayout.Label("未选中带有SkinnedMeshRenderer的对象", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("刷新形态键列表", GUILayout.Height(25)))
            {
                OnSelectionChanged();
            }

            EditorGUILayout.Space();
            GUILayout.Label("批量处理预设", EditorStyles.boldLabel);

            // 批量处理选项区域
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 预设1: 去除编号前缀 (如 "1. あ" -> "あ")
            if (GUILayout.Button("1. 去除编号前缀 (如 '1.あ' -> 'あ')", GUILayout.Height(25)))
            {
                if (targetSmr != null && blendShapeItems.Count > 0)
                {
                    RemoveNumberPrefixes();
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "请先选择带有形态键的对象", "确定");
                }
            }

            // 预设2: 添加编号前缀 (如 "あ" -> "1.あ")
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("前缀文本: ", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("2. 添加编号前缀 (如 'あ' -> '1.あ')", GUILayout.Height(25)))
            {
                if (targetSmr != null && blendShapeItems.Count > 0)
                {
                    AddNumberPrefixes();
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "请先选择带有形态键的对象", "确定");
                }
            }

            // 额外选项: 去除空格
            trimWhitespace = EditorGUILayout.Toggle("处理后去除首尾空格", trimWhitespace);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            GUILayout.Label("精细化编辑", EditorStyles.boldLabel);

            // 显示形态键列表，允许单独编辑
            if (blendShapeItems.Count == 0)
            {
                GUILayout.Label("没有可编辑的形态键，请选择带有SkinnedMeshRenderer的对象", EditorStyles.helpBox);
            }
            else
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                for (int i = 0; i < blendShapeItems.Count; i++)
                {
                    var item = blendShapeItems[i];
                    EditorGUILayout.BeginHorizontal();

                    // 显示索引
                    GUILayout.Label($"[{item.index}]", GUILayout.Width(40));

                    // 显示原始名称（不可编辑）
                    GUILayout.Label(item.originalName, GUILayout.Width(150));

                    // 显示箭头分隔符
                    GUILayout.Label("→", GUILayout.Width(20));

                    // 新名称编辑框
                    item.newName = EditorGUILayout.TextField(item.newName);

                    // 重置按钮
                    if (GUILayout.Button("重置", GUILayout.Width(60)))
                    {
                        item.newName = item.originalName;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }

            // 应用按钮
            if (GUILayout.Button("应用重命名", GUILayout.Height(30)))
            {
                ApplyRename();
            }

            // 帮助信息
            showHelpInfo = EditorGUILayout.Foldout(showHelpInfo, "使用帮助");
            if (showHelpInfo)
            {
                EditorGUILayout.HelpBox(
                    "1. 选择带有SkinnedMeshRenderer组件的对象\n" +
                    "2. 工具会自动加载所有形态键\n" +
                    "3. 可使用批量处理预设快速修改:\n" +
                    "   - 预设1: 去除数字前缀(如\"1.あ\"→\"あ\")\n" +
                    "   - 预设2: 添加数字前缀\n" +
                    "4. 也可在列表中手动修改每个形态键的新名称\n" +
                    "5. 点击\"应用重命名\"按钮保存修改\n\n" +
                    "注意: 操作会创建新的网格资源，原资源不会被修改",
                    MessageType.Info);
            }
        }

        // 预设1: 去除编号前缀
        private void RemoveNumberPrefixes()
        {
            // 正则表达式匹配: 数字开头，可能包含点号和空格
            // 如: "1. 啊", "2 哦", "3额" 等形式
            Regex regex = new Regex(@"^(\d+[\.\s]*)\s*");

            foreach (var item in blendShapeItems)
            {
                string processed = regex.Replace(item.originalName, "");
                if (trimWhitespace)
                {
                    processed = processed.Trim();
                }
                item.newName = processed;
            }

            Repaint();
        }


        private void AddNumberPrefixes()
        {
            foreach (var item in blendShapeItems)
            {
                // 索引从1开始计数
                string newName = $"{item.index + 1}.{item.originalName}";
                if (trimWhitespace)
                {
                    newName = newName.Trim();
                }
                item.newName = newName;
            }

            Repaint();
        }

        // 应用所有重命名
        private void ApplyRename()
        {
            if (targetSmr == null || targetSmr.sharedMesh == null || blendShapeItems.Count == 0)
            {
                EditorUtility.DisplayDialog("错误", "没有有效的目标对象或形态键", "确定");
                return;
            }

            // 检查是否有重复的新名称
            var newNames = blendShapeItems.Select(item => item.newName).ToList();
            if (newNames.Distinct().Count() != newNames.Count)
            {
                EditorUtility.DisplayDialog("错误", "新名称中存在重复项，请修改后再试", "确定");
                return;
            }

            // 检查是否有空白名称
            if (newNames.Any(string.IsNullOrWhiteSpace))
            {
                EditorUtility.DisplayDialog("错误", "新名称不能为空白", "确定");
                return;
            }

            isProcessing = true;

            Mesh originalMesh = targetSmr.sharedMesh;
            Mesh newMesh = CreateMeshCopy(originalMesh);
            newMesh.name = originalMesh.name + "_renamed";

            // 应用新名称
            foreach (var item in blendShapeItems)
            {
                // 先移除原始形态键
                // 注意：这里需要重新构建所有形态键，因为无法直接重命名
                // 所以我们先清除所有形态键
                if (item.index == 0)
                {
                    ClearBlendShapes(newMesh);
                }

                // 重新添加带有新名称的形态键
                for (int frame = 0; frame < originalMesh.GetBlendShapeFrameCount(item.index); frame++)
                {
                    Vector3[] vertices = new Vector3[originalMesh.vertexCount];
                    Vector3[] normals = new Vector3[originalMesh.vertexCount];
                    Vector3[] tangents = new Vector3[originalMesh.vertexCount];

                    originalMesh.GetBlendShapeFrameVertices(item.index, frame, vertices, normals, tangents);
                    float weight = originalMesh.GetBlendShapeFrameWeight(item.index, frame);

                    newMesh.AddBlendShapeFrame(item.newName, weight, vertices, normals, tangents);
                }
            }

            // 保存新网格
            SaveAndReplaceMesh(targetSmr, originalMesh, newMesh);

            // 刷新列表以显示新名称
            OnSelectionChanged();

            isProcessing = false;
            EditorUtility.DisplayDialog("完成", $"已成功重命名 {blendShapeItems.Count} 个形态键", "确定");
        }

        // 清除网格中的所有形态键
        private void ClearBlendShapes(Mesh mesh)
        {
            // 没有直接清除的方法，所以我们创建一个新的临时网格并复制除形态键外的所有数据
            Mesh tempMesh = CreateMeshCopy(mesh);

            // 复制临时网格数据回原网格（这样就清除了所有形态键）
            mesh.vertices = tempMesh.vertices;
            mesh.triangles = tempMesh.triangles;
            mesh.normals = tempMesh.normals;
            mesh.uv = tempMesh.uv;
            mesh.uv2 = tempMesh.uv2;
            mesh.uv3 = tempMesh.uv3;
            mesh.uv4 = tempMesh.uv4;
            mesh.colors = tempMesh.colors;
            mesh.tangents = tempMesh.tangents;
            mesh.bindposes = tempMesh.bindposes;
            mesh.boneWeights = tempMesh.boneWeights;

            // 复制子网格
            mesh.subMeshCount = tempMesh.subMeshCount;
            for (int i = 0; i < tempMesh.subMeshCount; i++)
            {
                mesh.SetTriangles(tempMesh.GetTriangles(i), i);
                mesh.SetSubMesh(i, tempMesh.GetSubMesh(i));
            }
        }

        // 完整复制网格数据
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

        // 保存新网格并替换引用
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
            Undo.RecordObject(smr, "Rename blend shapes");
            smr.sharedMesh = newMesh;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
