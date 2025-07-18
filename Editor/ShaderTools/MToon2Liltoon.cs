using UnityEngine;
using UnityEditor;

// 还没写完

namespace MToon2Liltoon
{
    /// <summary>
    /// 将VRM材质批量转换为lilToon材质，并设置相关参数
    /// </summary>
    [InitializeOnLoad]
    public class VrmToLilToonConverter : EditorWindow
    {
        private string lilToonShaderName = "lilToon";
        private Color mainColor = new Color(0.607f, 0.607f, 0.607f, 1f); // #9B9B9B
        private float shadowMaskStrength = 0.5f;
        private float brightnessLimit = 1.2f;
        private bool enableOutline = true;
        private float outlineWidth = 0.01f;
        private Color outlineColor = Color.black;

        [MenuItem("VRoidTools/VRM转lilToon材质")]
        public static void ShowWindow()
        {
            GetWindow<VrmToLilToonConverter>("VRM转lilToon材质");
        }

        private void OnGUI()
        {
            GUILayout.Label("将VRM材质批量转换为lilToon并设置参数", EditorStyles.boldLabel);

            // 通过名称查找Shader
            lilToonShaderName = EditorGUILayout.TextField("lilToon Shader 名称", lilToonShaderName);

            // 显示当前Shader状态
            Shader testShader = Shader.Find(lilToonShaderName);
            GUILayout.Label($"Shader状态: {(testShader != null ? "已找到" : "未找到")}",
                testShader != null ? EditorStyles.label : EditorStyles.boldLabel);

            // 刷新按钮
            if (GUILayout.Button("刷新 Shader 数据库"))
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("提示", "已刷新 Shader 列表，请重试", "确定");
            }

            EditorGUILayout.Space();
            GUILayout.Label("材质参数设置", EditorStyles.boldLabel);

            // 颜色和参数设置
            mainColor = EditorGUILayout.ColorField("Main Color", mainColor);
            shadowMaskStrength = EditorGUILayout.Slider("Shadow Mask Strength", shadowMaskStrength, 0f, 1f);
            brightnessLimit = EditorGUILayout.Slider("Upper Brightness Limit", brightnessLimit, 0f, 2f);

            // 轮廓设置
            enableOutline = EditorGUILayout.Toggle("启用轮廓", enableOutline);
            if (enableOutline)
            {
                outlineWidth = EditorGUILayout.Slider("轮廓宽度", outlineWidth, 0f, 0.1f);
                outlineColor = EditorGUILayout.ColorField("轮廓颜色", outlineColor);
            }

            // 处理按钮
            if (GUILayout.Button("处理选中的VRM模型"))
            {
                ProcessSelectedModels();
            }
        }

        private void ProcessSelectedModels()
        {
            Shader lilToon = Shader.Find(lilToonShaderName);
            if (lilToon == null)
            {
                EditorUtility.DisplayDialog("错误", $"未找到名为 '{lilToonShaderName}' 的Shader", "确定");
                return;
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("错误", "请选择至少一个VRM模型", "确定");
                return;
            }

            foreach (GameObject model in selectedObjects)
            {
                ProcessModel(model, lilToon);
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("完成", $"已处理 {selectedObjects.Length} 个模型", "确定");
        }

        private void ProcessModel(GameObject model, Shader lilToon)
        {
            // 获取所有SkinnedMeshRenderer组件
            SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                // 使用sharedMaterials避免材质泄漏
                Material[] materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material originalMat = materials[i];

                    // 创建新的lilToon材质
                    Material newMat = new Material(lilToon);
                    newMat.name = originalMat.name + "_lilToon";

                    // 1. 获取渲染模式
                    string renderingMode = GetMToonRenderingMode(originalMat);

                    // 2. 设置lilToon渲染模式（修复TransparentWithZWrite）
                    SetLilToonRenderingMode(newMat, renderingMode);

                    // 3. 设置主色（保留原始Alpha值）
                    Color originalColor = originalMat.HasProperty("_Color") ? originalMat.color : Color.white;
                    newMat.SetColor("_Color", new Color(mainColor.r, mainColor.g, mainColor.b, originalColor.a));

                    // 4. 复制基础纹理
                    CopyTextures(originalMat, newMat);

                    // 5. 阴影设置
                    newMat.SetFloat("_ShadeEnable", 1f);  // 启用阴影
                    newMat.SetFloat("_ShadeShift", shadowMaskStrength);  // 阴影遮罩强度

                    // 6. 光照上限设置
                    newMat.SetFloat("_HighlightUpperLimit", brightnessLimit);

                    // 7. 轮廓设置
                    if (enableOutline)
                    {
                        newMat.SetFloat("_Outline", 1f);  // 启用轮廓
                        newMat.SetFloat("_OutlineWidth", outlineWidth);
                        newMat.SetColor("_OutlineColor", outlineColor);
                    }

                    // 8. 其他lilToon特定设置
                    newMat.SetFloat("_ReceiveShadow", 1f);  // 接收阴影
                    newMat.SetFloat("_ShadeMultiply", 0.5f); // 阴影强度

                    // 替换材质
                    materials[i] = newMat;
                }

                // 应用修改后的材质数组
                renderer.sharedMaterials = materials;
            }
        }

        private string GetMToonRenderingMode(Material mat)
        {
            // 检查是否为MToon Shader
            if (mat.shader.name.Contains("MToon"))
            {
                // 优先通过_RenderMode属性判断
                if (mat.HasProperty("_RenderMode"))
                {
                    float renderMode = mat.GetFloat("_RenderMode");
                    switch ((int)renderMode)
                    {
                        case 0: return "Opaque";
                        case 1: return "Cutout";
                        case 2: return "TransparentWithZWrite";
                        case 3: return "Transparent";
                    }
                }
                // 回退到渲染队列判断
                else
                {
                    int queue = mat.renderQueue;
                    if (queue == 2000) return "Opaque";
                    if (queue == 2450) return "Cutout";
                    if (queue == 2500) return "TransparentWithZWrite";
                    if (queue >= 3000) return "Transparent";
                }
            }

            // 默认使用Cutout
            return "Cutout";
        }

        private void SetLilToonRenderingMode(Material mat, string mode)
        {
            switch (mode)
            {
                case "Opaque":
                    mat.SetFloat("_BlendMode", 0f);
                    mat.SetFloat("_ZWrite", 1f);
                    mat.renderQueue = 2000;
                    break;

                case "Cutout":
                    mat.SetFloat("_BlendMode", 1f);
                    mat.SetFloat("_ZWrite", 1f);
                    mat.renderQueue = 2450;
                    mat.SetFloat("_Cutoff", 0.5f);
                    break;

                case "TransparentWithZWrite":
                    // 关键修复：使用Two Pass Transparent模式
                    mat.SetFloat("_BlendMode", 2f);        // Alpha Blend
                    mat.SetFloat("_ZWrite", 1f);           // 启用Z写入
                    mat.SetFloat("_TwoPass", 1f);          // 启用Two Pass
                    mat.SetFloat("_TwoPassCullMode", 1f);  // Front面先渲染
                    mat.renderQueue = 2500;
                    break;

                case "Transparent":
                    mat.SetFloat("_BlendMode", 2f);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.renderQueue = 3000;
                    break;
            }
        }

        private void CopyTextures(Material srcMat, Material dstMat)
        {
            // 复制基础纹理
            if (srcMat.HasProperty("_MainTex"))
                dstMat.SetTexture("_MainTex", srcMat.GetTexture("_MainTex"));

            // 复制法线贴图
            if (srcMat.HasProperty("_BumpMap"))
                dstMat.SetTexture("_NormalMap", srcMat.GetTexture("_BumpMap"));

            // 复制阴影纹理
            if (srcMat.HasProperty("_ShadeTexture"))
                dstMat.SetTexture("_ShadeTex", srcMat.GetTexture("_ShadeTexture"));

            // 复制其他可能的纹理
            if (srcMat.HasProperty("_EmissionMap"))
                dstMat.SetTexture("_EmissionMap", srcMat.GetTexture("_EmissionMap"));
        }
    }
}