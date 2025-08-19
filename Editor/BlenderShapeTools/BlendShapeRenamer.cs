using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UniGLTF;
using VRM;
using MeshExtensionsForCopy = UniGLTF.MeshUtility.MeshExtensions;
using System;


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
        private bool isVRMModel = false;

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
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            isVRMModel = false;
            targetSmr = null;

            if (Selection.activeGameObject != null)
            {
                // 检查是否为VRM模型（从根对象查找VRMBlendShapeProxy组件）
                var root = Selection.activeGameObject.transform.root.gameObject;
                var vrmComponent = root.GetComponent<VRMBlendShapeProxy>();
                if (vrmComponent != null)
                {
                    isVRMModel = true;
                    // 优先使用当前选中对象的 SkinnedMeshRenderer
                    targetSmr = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (targetSmr == null)
                    {
                        targetSmr = Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>();
                    }
                    // 如果当前对象没有，再尝试从 VRM 根对象获取
                    if (targetSmr == null)
                    {
                        targetSmr = vrmComponent.GetComponent<SkinnedMeshRenderer>();
                        if (targetSmr == null)
                        {
                            targetSmr = vrmComponent.GetComponentInChildren<SkinnedMeshRenderer>();
                        }
                    }
                }
                else
                {
                    // 非VRM模型处理
                    targetSmr = Selection.activeGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (targetSmr == null)
                    {
                        targetSmr = Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>();
                    }
                }
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
                if (isVRMModel)
                {
                    EditorGUILayout.LabelField("[VRM模型]", EditorStyles.miniLabel);
                }
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

            // 预设1: 去除编号前缀
            if (GUILayout.Button("1. 去除编号前缀 (如 '1. 啊' -> '啊')", GUILayout.Height(25)))
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

            // 预设2: 添加编号前缀
            EditorGUILayout.BeginHorizontal();


            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("2. 添加编号前缀 (如 '啊' -> 'No.1 啊')", GUILayout.Height(25)))
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

            // 额外选项
            trimWhitespace = EditorGUILayout.Toggle("处理后去除首尾空格", trimWhitespace);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            GUILayout.Label("精细化编辑", EditorStyles.boldLabel);

            // 显示形态键列表
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

                    GUILayout.Label($"[{item.index}]", GUILayout.Width(40));
                    GUILayout.Label(item.originalName, GUILayout.Width(150));
                    GUILayout.Label("→", GUILayout.Width(20));
                    item.newName = EditorGUILayout.TextField(item.newName);

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
                if (isVRMModel)
                {
                    // 提示用户VRM模型处理注意事项
                    if (!EditorUtility.DisplayDialog("VRM模型处理",
                        "处理VRM模型可能需要重新导出VRM。\n是否继续？",
                        "继续", "取消"))
                    {
                        return;
                    }
                }
                ApplyRename();
            }

            // 帮助信息
            showHelpInfo = EditorGUILayout.Foldout(showHelpInfo, "使用帮助");
            if (showHelpInfo)
            {
                EditorGUILayout.HelpBox(
                    "1. 选择带有SkinnedMeshRenderer组件的对象\n" +
                    "2. 工具会自动加载所有形态键\n" +
                    "3. 可使用批量处理预设快速修改或手动编辑\n" +
                    "4. 点击\"应用重命名\"按钮保存修改\n\n" +
                    "VRM模型注意事项:\n" +
                    "- 处理后建议重新导出VRM格式\n" +
                    "- 确保UniVRM插件已正确导入",
                    MessageType.Info);
            }
        }

        // 预设1: 去除编号前缀
        private void RemoveNumberPrefixes()
        {
            // 增强版正则表达式，处理更多格式
            Regex regex = new Regex(@"^(\d+[.\s_-]*)\s*");

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

        // 预设2: 添加编号前缀
        private void AddNumberPrefixes()
        {
            foreach (var item in blendShapeItems)
            {
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

            try
            {
                Mesh originalMesh = targetSmr.sharedMesh;
                Mesh newMesh = CreateMeshCopy(originalMesh);
                newMesh.name = originalMesh.name + "_renamed";

                // 清除原始形态键
                ClearBlendShapes(newMesh);

                // 重新添加带有新名称的形态键
                foreach (var item in blendShapeItems)
                {
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

                // 对于VRM模型，使用UniVRM的工具确保兼容性
                if (isVRMModel)
                {
                    VrmMeshUtility.OptimizeForVRM(newMesh);
                }

                // 保存新网格
                SaveAndReplaceMesh(targetSmr, originalMesh, newMesh);

                // 刷新列表
                OnSelectionChanged();

                EditorUtility.DisplayDialog("完成", $"已成功重命名 {blendShapeItems.Count} 个形态键", "确定");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("错误", $"处理过程中发生错误: {ex.Message}", "确定");
                Debug.LogError($"BlendShapeRenamer error: {ex}");
            }
            finally
            {
                isProcessing = false;
            }
        }

        // 清除网格中的所有形态键（兼容所有Unity版本）
        private void ClearBlendShapes(Mesh mesh)
        {
            // 使用反射调用内部方法，如果失败则使用兼容方法
            try
            {
                var method = typeof(Mesh).GetMethod("ClearBlendShapes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(mesh, null);
                    return;
                }
            }
            catch { }

            // 兼容方法：创建新网格并复制除形态键外的所有数据
            Mesh tempMesh = CreateMeshCopy(mesh);
            mesh.Clear();

            // 复制回非形态键数据
            mesh.vertices = tempMesh.vertices;
            mesh.normals = tempMesh.normals;
            mesh.tangents = tempMesh.tangents;
            mesh.colors = tempMesh.colors;
            mesh.colors32 = tempMesh.colors32;
            mesh.uv = tempMesh.uv;
            mesh.uv2 = tempMesh.uv2;
            mesh.uv3 = tempMesh.uv3;
            mesh.uv4 = tempMesh.uv4;
            mesh.uv5 = tempMesh.uv5;
            mesh.uv6 = tempMesh.uv6;
            mesh.uv7 = tempMesh.uv7;
            mesh.uv8 = tempMesh.uv8;
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

        // 完整复制网格数据（使用UniGLTF的工具确保兼容性）
        private Mesh CreateMeshCopy(Mesh original)
        {
            if (isVRMModel)
            {
                // Debug，检测到VRM模型
                Debug.Log("Detected VRM model, using UniGLTF mesh copy utility");
                // 对于VRM模型，使用UniGLTF的网格复制工具
                return UniGLTF.MeshUtility.MeshExtensions.Copy(original, false);
            }
            else
            {
                // 标准网格复制
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

            // 保存新网格
            AssetDatabase.CreateAsset(newMesh, path);

            // 替换渲染器的网格引用
            Undo.RecordObject(smr, "Rename blend shapes");
            smr.sharedMesh = newMesh;

            // 刷新资源
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 替换 SaveAndReplaceMesh 中的 VRM 处理部分
            if (isVRMModel)
            {
                // 找到 VRM 根对象的 BlendShapeProxy
                var root = smr.transform.root.gameObject;
                var blendShapeProxy = root.GetComponent<VRMBlendShapeProxy>();
                if (blendShapeProxy != null)
                {
                    // 强制刷新形态键数据（兼容所有版本的通用方法）
                    var tempMesh = smr.sharedMesh;
                    smr.sharedMesh = null;
                    smr.sharedMesh = tempMesh;

                    // 刷新 Inspector 显示
                    EditorUtility.SetDirty(blendShapeProxy);
                    EditorUtility.SetDirty(smr);
                    AssetDatabase.Refresh();
                }

                EditorUtility.DisplayDialog("提示", "VRM模型形态键已更新，建议重新导出VRM以确保兼容性", "确定");
            }
        }
    }

    // VRM网格处理工具类
    public static class VrmMeshUtility
    {
        // 优化网格以确保VRM兼容性
        public static void OptimizeForVRM(Mesh mesh)
        {
            // 确保索引格式正确
            if (mesh.indexFormat != UnityEngine.Rendering.IndexFormat.UInt32)
            {
                int[] triangles = mesh.triangles;
                if (triangles != null && triangles.Length > 0)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.triangles = triangles;
                }
            }

            // 确保子网格设置正确
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var subMesh = mesh.GetSubMesh(i);
                subMesh.topology = MeshTopology.Triangles;
                mesh.SetSubMesh(i, subMesh);
            }

            // 重新计算边界
            mesh.RecalculateBounds();
        }
    }
}
