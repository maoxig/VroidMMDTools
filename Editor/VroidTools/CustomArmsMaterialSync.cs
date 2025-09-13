#if USE_VROID_MOD
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRoidTools
{
    public class CustomArmsMaterialSync : EditorWindow
    {
        private GameObject selectedModel;
        private GameObject customArms;
        private bool isProcessing = false;

        [MenuItem("VRoidTools/Custom Arms Material Sync")]
        public static void ShowWindow()
        {
            GetWindow<CustomArmsMaterialSync>("Custom Arms Material Sync");
        }

        private void OnEnable()
        {
            // 监听选择变化
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            // 移除监听
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            // 检查是否选中了一个模型
            if (Selection.activeGameObject != null)
            {
                selectedModel = Selection.activeGameObject;

                // 查找根层级下的CustomArms
                customArms = FindCustomArmsInRoot(selectedModel.transform.root.gameObject);

                // 如果找到则自动同步
                if (customArms != null)
                {
                    SyncMaterials();
                }
            }
            else
            {
                selectedModel = null;
                customArms = null;
            }

            Repaint();
        }

        private GameObject FindCustomArmsInRoot(GameObject root)
        {
            // 在根物体的直接子物体中查找CustomArms
            foreach (Transform child in root.transform)
            {
                if (child.name == "CustomArms")
                {
                    return child.gameObject;
                }
            }
            return null;
        }

        private void SyncMaterials()
        {
            if (selectedModel == null || customArms == null) return;

            isProcessing = true;

            // 获取根层级下的所有物体（排除CustomArms）
            Dictionary<string, SkinnedMeshRenderer> rootRenderers = new Dictionary<string, SkinnedMeshRenderer>();
            CollectSkinnedMeshRenderers(selectedModel.transform.root.gameObject, rootRenderers, true);

            // 获取CustomArms下的所有物体
            Dictionary<string, SkinnedMeshRenderer> customArmsRenderers = new Dictionary<string, SkinnedMeshRenderer>();
            CollectSkinnedMeshRenderers(customArms, customArmsRenderers, false);

            // 同步材质（使用共享材质）
            int syncedCount = 0;
            foreach (var pair in customArmsRenderers)
            {
                string objectName = pair.Key;
                SkinnedMeshRenderer customRenderer = pair.Value;

                if (rootRenderers.TryGetValue(objectName, out SkinnedMeshRenderer rootRenderer))
                {
                    // 关键修改：使用sharedMaterials替代materials
                    // 这样会共享材质引用而不是创建新实例
                    customRenderer.sharedMaterials = rootRenderer.sharedMaterials;
                    syncedCount++;
                }
            }

            if (syncedCount > 0)
            {
                Debug.Log($"成功同步了 {syncedCount} 个物体的共享材质");
            }

            isProcessing = false;
        }

        private void CollectSkinnedMeshRenderers(GameObject target, Dictionary<string, SkinnedMeshRenderer> renderers, bool excludeCustomArms)
        {
            // 检查是否需要排除CustomArms
            if (excludeCustomArms && target.name == "CustomArms")
            {
                return;
            }

            // 检查当前物体是否有SkinnedMeshRenderer组件
            SkinnedMeshRenderer smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && !renderers.ContainsKey(target.name))
            {
                renderers.Add(target.name, smr);
            }

            // 递归检查子物体
            foreach (Transform child in target.transform)
            {
                CollectSkinnedMeshRenderers(child.gameObject, renderers, excludeCustomArms);
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Custom Arms 材质同步工具", EditorStyles.boldLabel);

            if (selectedModel != null)
            {
                GUILayout.Label($"当前选中模型: {selectedModel.name}");

                if (customArms != null)
                {
                    GUILayout.Label("找到 CustomArms 层级");

                    if (GUILayout.Button("手动同步材质") && !isProcessing)
                    {
                        SyncMaterials();
                    }
                }
                else
                {
                    GUILayout.Label("未找到 CustomArms 层级");
                }
            }
            else
            {
                GUILayout.Label("请选择一个模型");
            }
        }
    }
}
#endif