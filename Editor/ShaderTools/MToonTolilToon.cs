// 这个脚本来自于 https://booth.pm/ja/items/6902717
// 我做了一些翻译工作
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System;
using UnityEngine.Rendering;

public class MToonTolilToon : EditorWindow
{
    public enum MToon10AlphaMode
    {
        Opaque = 0,
        Cutoff = 1,
        Transparent = 2
    }

    private static List<Material> createdMaterials = new List<Material>();
    private static GameObject copiedObject;

    [MenuItem("ShaderTools/「MToon⇔lilToon」")]
    public static void ShowWindow()
    {
        GetWindow<MToonTolilToon>("MToon ⇔ lilToon Converter");
    }

    private void OnGUI()
    {
        // 使用说明（中文）
        GUILayout.Label("材质转换工具「MToon⇔lilToon」 Ver.0.5", EditorStyles.boldLabel);
        GUILayout.Space(10);
        GUILayout.Label("本工具会复制所选的Avatar对象，并在MToon与lilToon材质之间进行相互转换。" +
                        "转换后的材质会保存在原材质同一文件夹下的“lilToon”或“MToon”子文件夹中。", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);
        GUILayout.Label("使用方法：", EditorStyles.boldLabel);
        GUILayout.Label("① 在Hierarchy中选择Avatar的GameObject。\n" +
                        "※请先在项目中导入MToon和lilToon着色器。\n" +
                        "② 点击下方按钮选择转换方向（MToon→lilToon或lilToon→MToon）。\n" +
                        "※原始Avatar和材质不会被修改。", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);
        GUILayout.Label("※建议在操作前备份项目。", EditorStyles.wordWrappedMiniLabel);
        GUILayout.Label("※从lilToon转换到MToon时，建议使用lilToon材质自带的按钮，转换更准确且功能更强大。", EditorStyles.wordWrappedMiniLabel);
        GUILayout.Space(20);

        if (GUILayout.Button("复制所选Avatar并转换为lilToon"))
        {
            ConvertToLilToonMaterials();
        }

        if (GUILayout.Button("复制所选Avatar并转换为MToon"))
        {
            ConvertToMToonMaterials();
        }

        if (GUILayout.Button("撤销（删除转换后的材质和Avatar）"))
        {
            DeleteConvertedAssets();
        }
    }

    // 将MToon材质转换为lilToon材质
    private void ConvertToLilToonMaterials()
    {
        // 清除之前的记录
        createdMaterials.Clear();
        copiedObject = null;

        // 获取当前选中的GameObject
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            Debug.LogError("未选择GameObject！");
            return;
        }

        // 检查lilToon着色器
        Shader lilToonShader = Shader.Find("lilToon");
        if (lilToonShader == null)
        {
            Debug.LogError("未找到lilToon着色器！请先导入lilToon。");
            return;
        }

        // 复制GameObject
        copiedObject = UnityEngine.Object.Instantiate(selectedObject);
        copiedObject.name = selectedObject.name + "_lilToon";
        Undo.RegisterCreatedObjectUndo(copiedObject, "转换为lilToon");

        // 将复制的对象在X轴上移动-1米
        Undo.RecordObject(copiedObject.transform, "移动复制对象");
        copiedObject.transform.position += new Vector3(-1.0f, 0, 0);

        // 获取复制对象及其所有子对象上的Renderer
        Renderer[] renderers = copiedObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError("复制的GameObject或其子对象未找到Renderer！");
            UnityEngine.Object.DestroyImmediate(copiedObject);
            return;
        }
        // 用于保存材质的备用文件夹
        string fallbackFolder = "Assets/Materials/lilToon";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        if (!AssetDatabase.IsValidFolder(fallbackFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "lilToon");
        }

        foreach (Renderer renderer in renderers)
        {
            // 获取当前的材质
            Material[] materials = renderer.sharedMaterials;
            bool modified = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material originalMaterial = materials[i];
                if (originalMaterial == null)
                {
                    continue;
                }

                string shaderName = originalMaterial.shader.name;
                if (!shaderName.Contains("VRM/MToon") && !shaderName.Contains("VRM10/MToon10"))
                {
                    continue;
                }

                // 复制材质
                Material newMaterial = new Material(originalMaterial)
                {
                    name = originalMaterial.name + "_lilToon"
                };

                // 切换为lilToon着色器（后续会设置为合适的着色器）
                newMaterial.shader = lilToonShader;

                // 应用lilToon特有的默认设置
                ApplyLilToonDefaultSettings(newMaterial);

                // 映射并复制属性
                if (shaderName.Contains("VRM/MToon"))
                {
                    MapAndCopyPropertiesToLilToon(originalMaterial, newMaterial);
                }
                else if (shaderName.Contains("VRM10/MToon10"))
                {
                    MapAndCopyPropertiesToLilToonFromMToon10(originalMaterial, newMaterial);
                }

                // 获取原材质的路径
                string originalPath = AssetDatabase.GetAssetPath(originalMaterial);
                string targetFolder = fallbackFolder;
                if (!string.IsNullOrEmpty(originalPath))
                {
                    string parentFolder = Path.GetDirectoryName(originalPath).Replace("\\", "/");
                    targetFolder = $"{parentFolder}/lilToon";
                    if (!AssetDatabase.IsValidFolder(targetFolder))
                    {
                        AssetDatabase.CreateFolder(parentFolder, "lilToon");
                        Debug.Log($"已创建lilToon文件夹: {targetFolder}");
                    }
                }
                else
                {
                    Debug.LogWarning($"原材质 {originalMaterial.name} 未作为资源保存。使用备用文件夹: {fallbackFolder}");
                }

                // 清理材质名（去除无效字符）
                string cleanMaterialName = newMaterial.name.Replace("/", "_").Replace("\\", "_").Replace(":", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
                string path = $"{targetFolder}/{cleanMaterialName}.mat";
                string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
                Debug.Log($"保存材质: {uniquePath}");

                // 将材质保存为资源
                AssetDatabase.CreateAsset(newMaterial, uniquePath);
                Undo.RegisterCreatedObjectUndo(newMaterial, "转换为lilToon");

                // 记录生成的材质
                createdMaterials.Add(newMaterial);

                // 更新Renderer的材质
                Undo.RecordObject(renderer, "转换为lilToon");
                materials[i] = newMaterial;
                modified = true;
            }

            if (modified)
            {
                renderer.sharedMaterials = materials;
            }
        }
        // 更新资产数据库
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"MToon/MToon10→lilToon材质转换已完成！复制的GameObject: {copiedObject.name}");
    }
    // 将lilToon材质转换为MToon材质
    private void ConvertToMToonMaterials()
    {
        // 清除之前的记录
        createdMaterials.Clear();
        copiedObject = null;

        // 获取当前选中的GameObject
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            Debug.LogError("未选择GameObject！");
            return;
        }

        // 检查MToon着色器（优先使用MToon10）
        Shader mToon10Shader = Shader.Find("VRM10/MToon10");
        Shader mToonShader = Shader.Find("VRM/MToon");
        if (mToon10Shader == null && mToonShader == null)
        {
            Debug.LogError("未找到MToon或MToon10着色器！请先导入所需着色器。");
            return;
        }
        Shader targetMToonShader = mToon10Shader != null ? mToon10Shader : mToonShader;
        Debug.Log($"使用的着色器: {targetMToonShader.name}");

        // 复制GameObject
        copiedObject = UnityEngine.Object.Instantiate(selectedObject);
        copiedObject.name = selectedObject.name + "_MToon";
        Undo.RegisterCreatedObjectUndo(copiedObject, "转换为MToon");

        // 将复制的对象在X轴上移动-1米
        Undo.RecordObject(copiedObject.transform, "移动复制对象");
        copiedObject.transform.position += new Vector3(-1.0f, 0, 0);

        // 获取复制对象及其所有子对象上的Renderer
        Renderer[] renderers = copiedObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError("复制的GameObject或其子对象未找到Renderer！");
            UnityEngine.Object.DestroyImmediate(copiedObject);
            return;
        }

        // 用于保存材质的备用文件夹
        string fallbackFolder = "Assets/Materials/MToon";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        if (!AssetDatabase.IsValidFolder(fallbackFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "MToon");
        }

        foreach (Renderer renderer in renderers)
        {
            // 获取当前的材质
            Material[] materials = renderer.sharedMaterials;
            bool modified = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material originalMaterial = materials[i];
                if (originalMaterial == null || !originalMaterial.shader.name.Contains("lilToon"))
                {
                    continue;
                }

                // 复制材质
                Material newMaterial = new Material(originalMaterial)
                {
                    name = originalMaterial.name + "_MToon"
                };

                // 切换为MToon着色器
                newMaterial.shader = targetMToonShader;

                // 映射并复制属性
                if (targetMToonShader.name == "VRM10/MToon10")
                {
                    MapAndCopyPropertiesToMToon10FromLilToon(originalMaterial, newMaterial);
                }
                else
                {
                    MapAndCopyPropertiesToMToon(originalMaterial, newMaterial);
                }

                // 获取原材质的路径
                string originalPath = AssetDatabase.GetAssetPath(originalMaterial);
                string targetFolder = fallbackFolder;
                if (!string.IsNullOrEmpty(originalPath))
                {
                    string parentFolder = Path.GetDirectoryName(originalPath).Replace("\\", "/");
                    targetFolder = $"{parentFolder}/MToon";
                    if (!AssetDatabase.IsValidFolder(targetFolder))
                    {
                        AssetDatabase.CreateFolder(parentFolder, "MToon");
                        Debug.Log($"已创建MToon文件夹: {targetFolder}");
                    }
                }
                else
                {
                    Debug.LogWarning($"原材质 {originalMaterial.name} 未作为资源保存。使用备用文件夹: {fallbackFolder}");
                }

                // 清理材质名（去除无效字符）
                string cleanMaterialName = newMaterial.name.Replace("/", "_").Replace("\\", "_").Replace(":", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
                string path = $"{targetFolder}/{cleanMaterialName}.mat";
                string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
                Debug.Log($"保存材质: {uniquePath}");

                // 将材质保存为资源
                AssetDatabase.CreateAsset(newMaterial, uniquePath);
                Undo.RegisterCreatedObjectUndo(newMaterial, "转换为MToon");

                // 记录生成的材质
                createdMaterials.Add(newMaterial);

                // 更新Renderer的材质
                Undo.RecordObject(renderer, "转换为MToon");
                materials[i] = newMaterial;
                modified = true;
            }

            if (modified)
            {
                renderer.sharedMaterials = materials;
            }
        }

        // 更新资产数据库
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"lilToon→MToon/MToon10材质转换已完成！复制的GameObject: {copiedObject.name}");
    }
    // 删除转换后的资产（复制的GameObject和生成的材质）
    private void DeleteConvertedAssets()
    {
        if (copiedObject != null)
        {
            UnityEngine.Object.DestroyImmediate(copiedObject);
            Debug.Log($"已删除复制的GameObject: {copiedObject.name}");
            copiedObject = null;
        }

        foreach (var material in createdMaterials)
        {
            if (material != null)
            {
                string path = AssetDatabase.GetAssetPath(material);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"已删除转换后的材质: {path}");
                }
            }
        }
        createdMaterials.Clear();

        // 更新资产数据库
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("已删除所有转换后的资产。");
    }
    // 新增：复制通用属性
    // 将源材质的所有通用属性复制到目标材质（仅复制同名且类型相同的属性）
    private void CopyCommonProperties(Material source, Material target)
    {
        // 1. 直接复制同名属性
        var propertyCount = ShaderUtil.GetPropertyCount(source.shader);
        for (int i = 0; i < propertyCount; i++)
        {
            string propName = ShaderUtil.GetPropertyName(source.shader, i);
            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(source.shader, i);

            if (target.HasProperty(propName))
            {
                try
                {
                    switch (propType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            target.SetColor(propName, source.GetColor(propName));
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            target.SetVector(propName, source.GetVector(propName));
                            break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            target.SetFloat(propName, source.GetFloat(propName));
                            break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            target.SetTexture(propName, source.GetTexture(propName));
                            target.SetTextureOffset(propName, source.GetTextureOffset(propName));
                            target.SetTextureScale(propName, source.GetTextureScale(propName));
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"复制属性 {propName} 时失败: {e.Message}");
                }
            }
        }
    }

    // 将MToon材质属性映射并复制到lilToon材质
    private void MapAndCopyPropertiesToLilToon(Material source, Material target)
    {
        // 法线贴图、边缘光、阴影、MatCap、描边的启用/禁用标志
        bool hasNormalMap = source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null;
        bool hasRimLight = (source.HasProperty("_RimColor") && source.GetColor("_RimColor") != Color.black) ||
                           (source.HasProperty("_RimTexture") && source.GetTexture("_RimTexture") != null);
        bool hasShadow = (source.HasProperty("_ShadeColor") && source.GetColor("_ShadeColor") != Color.black) ||
                         (source.HasProperty("_ShadeTexture") && source.GetTexture("_ShadeTexture") != null);
        bool hasMatCap = source.HasProperty("_SphereAdd") && source.GetTexture("_SphereAdd") != null;
        bool hasOutline = source.HasProperty("_OutlineWidthMode") && source.GetFloat("_OutlineWidthMode") > 0.0f;
        bool hasEmission = source.HasProperty("_EmissionColor") && source.GetColor("_EmissionColor") != Color.black ||
                           source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null;

        // 复制通用属性
        CopyCommonProperties(source, target);

        // 2. 明确映射MToon和lilToon的属性
        // 主颜色和贴图
        if (source.HasProperty("_Color") && target.HasProperty("_BaseColor"))
        {
            target.SetColor("_BaseColor", source.GetColor("_Color"));
        }
        if (source.HasProperty("_MainTex") && target.HasProperty("_BaseMap"))
        {
            target.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            target.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
        }

        // 阴影
        if (source.HasProperty("_ShadeColor") && target.HasProperty("_Shadow2ndColor"))
        {
            Color shadeColor = source.GetColor("_ShadeColor");
            target.SetColor("_Shadow2ndColor", shadeColor);
            if (target.HasProperty("_ShadowColor"))
            {
                target.SetColor("_ShadowColor", Color.Lerp(Color.white, shadeColor, 0.5f)); // 白色和阴影色的中间色
            }
        }
        if (source.HasProperty("_ShadeTexture") && target.HasProperty("_Shadow1stColorTex"))
        {
            target.SetTexture("_Shadow1stColorTex", source.GetTexture("_ShadeTexture"));
        }
        if (source.HasProperty("_ShadeShift") && target.HasProperty("_ShadowBorder"))
        {
            float shift = source.GetFloat("_ShadeShift");
            target.SetFloat("_ShadowBorder", Mathf.Clamp01(0.5f + shift * 0.5f));
        }
        if (target.HasProperty("_UseShadow"))
        {
            target.SetFloat("_UseShadow", hasShadow ? 1.0f : 0.0f);
        }

        // 法线贴图
        if (source.HasProperty("_BumpMap") && target.HasProperty("_BumpMap"))
        {
            target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
        }
        if (source.HasProperty("_BumpScale") && target.HasProperty("_BumpScale"))
        {
            target.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
        }
        if (target.HasProperty("_UseBumpMap"))
        {
            target.SetFloat("_UseBumpMap", hasNormalMap ? 1.0f : 0.0f);
        }

        // 边缘光
        if (source.HasProperty("_RimColor") && target.HasProperty("_RimColor"))
        {
            target.SetColor("_RimColor", source.GetColor("_RimColor"));
        }
        if (source.HasProperty("_RimTexture") && target.HasProperty("_RimColorTex"))
        {
            target.SetTexture("_RimColorTex", source.GetTexture("_RimTexture"));
        }
        if (source.HasProperty("_RimLightingMix") && target.HasProperty("_RimStrength"))
        {
            target.SetFloat("_RimStrength", source.GetFloat("_RimLightingMix"));
        }
        if (source.HasProperty("_RimFresnelPower") && target.HasProperty("_RimFresnelPower"))
        {
            target.SetFloat("_RimFresnelPower", source.GetFloat("_RimFresnelPower"));
        }
        if (target.HasProperty("_UseRim"))
        {
            target.SetFloat("_UseRim", hasRimLight ? 1.0f : 0.0f);
        }

        // MatCap
        if (source.HasProperty("_SphereAdd") && target.HasProperty("_MatCapTex"))
        {
            target.SetTexture("_MatCapTex", source.GetTexture("_SphereAdd"));
        }
        if (source.HasProperty("_MatCapMul") && target.HasProperty("_MatCapBlend"))
        {
            target.SetFloat("_MatCapBlend", source.GetFloat("_MatCapMul"));
        }
        if (target.HasProperty("_UseMatCap"))
        {
            target.SetFloat("_UseMatCap", hasMatCap ? 1.0f : 0.0f);
        }

        // 描边
        if (source.HasProperty("_OutlineColor") && target.HasProperty("_OutlineColor"))
        {
            target.SetColor("_OutlineColor", source.GetColor("_OutlineColor"));
        }
        if (source.HasProperty("_OutlineWidth") && target.HasProperty("_OutlineWidth"))
        {
            target.SetFloat("_OutlineWidth", source.GetFloat("_OutlineWidth"));
        }
        if (target.HasProperty("_UseOutline"))
        {
            if (hasOutline)
            {
                target.SetFloat("_UseOutline", 1);
                Shader lilToonMultiOutlineShader = Shader.Find("Hidden/lilToonMultiOutline");
                if (lilToonMultiOutlineShader == null)
                {
                    Debug.LogError("未找到lilToonMultiOutline着色器！请导入lilToonMultiOutline。");
                }
                else
                {
                    target.shader = lilToonMultiOutlineShader;
                }
            }
            else
            {
                target.SetFloat("_UseOutline", 0);
            }
        }

        // 自发光
        if (hasEmission && source.HasProperty("_EmissionColor") && target.HasProperty("_EmissionColor"))
        {
            target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
        }
        if (hasEmission && source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap"))
        {
            target.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
        }
        if (target.HasProperty("_UseEmission"))
        {
            target.SetFloat("_UseEmission", hasEmission ? 1.0f : 0.0f);
        }

        // Alpha设置
        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }

        // 剔除设置
        if (source.HasProperty("_CullMode") && target.HasProperty("_Cull"))
        {
            int cullMode = source.GetInt("_CullMode");
            target.SetInt("_Cull", cullMode);
        }

        // ZWrite设置
        target.SetInt("_ZWrite", 1); // 不透明和Cutout时始终开启ZWrite

        // 渲染队列设置
        int renderQueueOffset = source.HasProperty("_RenderQueueOffset") ? source.GetInt("_RenderQueueOffset") : 0;
        bool isTransparent = source.HasProperty("_BlendMode") && source.GetFloat("_BlendMode") >= 2;
        bool isCutout = source.HasProperty("_BlendMode") && source.GetFloat("_BlendMode") == 1;
        int baseRenderQueue = isTransparent ? 3000 : isCutout ? 2450 : 2000;
        target.renderQueue = baseRenderQueue + renderQueueOffset;

        // 半透明处理
        if (isTransparent)
        {
            Shader lilToonTransparentShader = Shader.Find("Hidden/lilToonTransparent");
            if (hasOutline)
            {
                lilToonTransparentShader = Shader.Find("Hidden/lilToonTransparentOutline");
            }
            if (lilToonTransparentShader == null)
            {
                Debug.LogError("未找到lilToonTransparent相关着色器！");
            }
            else
            {
                target.shader = lilToonTransparentShader;
                target.SetFloat("_Cutoff", 0.001f);
                target.SetInt("_ZWrite", 1); // 半透明也开启ZWrite
            }
        }
        else if (isCutout)
        {
            Shader lilToonCutoutShader = Shader.Find("Hidden/lilToonCutout");
            if (hasOutline)
            {
                lilToonCutoutShader = Shader.Find("Hidden/lilToonCutoutOutline");
            }
            if (lilToonCutoutShader == null)
            {
                Debug.LogError("未找到lilToonCutout相关着色器！请导入lilToonCutout。");
            }
            else
            {
                target.shader = lilToonCutoutShader;
                target.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
                target.SetInt("_ZWrite", 1);
            }
        }
        else
        {
            // 不透明材质
            Shader targetShader = Shader.Find("lilToon");
            if (hasOutline)
            {
                targetShader = Shader.Find("Hidden/lilToonMultiOutline");
                if (targetShader == null)
                {
                    Debug.LogError("未找到lilToonMultiOutline着色器！请导入lilToonMultiOutline。");
                }
            }
            if (targetShader != null)
            {
                target.shader = targetShader;
            }
        }

        // MToon光照调整
        if (source.HasProperty("_LightColorAttenuation") && target.HasProperty("_LightMinLimit"))
        {
            float attenuation = source.GetFloat("_LightColorAttenuation");
            target.SetFloat("_LightMinLimit", Mathf.Lerp(0.0f, 0.1f, attenuation));
        }
    }

    // 将lilToon材质属性映射并复制到MToon材质
    private void MapAndCopyPropertiesToMToon(Material source, Material target)
    {
        // 法线贴图、边缘光、阴影、MatCap、描边的启用/禁用标志
        bool hasNormalMap = source.HasProperty("_NormalMap") && source.GetTexture("_NormalMap") != null;
        bool hasRimLight = (source.HasProperty("_RimColor") && source.GetColor("_RimColor") != Color.black) ||
                           (source.HasProperty("_RimColorTex") && source.GetTexture("_RimColorTex") != null);
        bool hasShadow = (source.HasProperty("_Shadow2ndColor") && source.GetColor("_Shadow2ndColor") != Color.black) ||
                         (source.HasProperty("_Shadow1stColorTex") && source.GetTexture("_Shadow1stColorTex") != null);
        bool hasMatCap = source.HasProperty("_MatCapTex") && source.GetTexture("_MatCapTex") != null && source.GetFloat("_UseMatCap") > 0;
        bool hasOutline = source.shader.name.Contains("Outline");
        bool hasEmission = source.HasProperty("_UseEmission") && source.GetFloat("_UseEmission") > 0;

        // 复制通用属性
        CopyCommonProperties(source, target);

        // 2. 明确映射lilToon和MToon的属性
        // 主颜色和贴图
        if (source.HasProperty("_BaseColor") && target.HasProperty("_Color"))
        {
            target.SetColor("_Color", source.GetColor("_BaseColor"));
        }
        if (source.HasProperty("_BaseMap") && target.HasProperty("_MainTex"))
        {
            target.SetTexture("_MainTex", source.GetTexture("_BaseMap"));
            target.SetTextureOffset("_MainTex", source.GetTextureOffset("_BaseMap"));
            target.SetTextureScale("_MainTex", source.GetTextureScale("_BaseMap"));
        }

        // 阴影
        if (source.HasProperty("_Shadow2ndColor") && target.HasProperty("_ShadeColor"))
        {
            target.SetColor("_ShadeColor", source.GetColor("_Shadow2ndColor"));
        }
        if (source.HasProperty("_Shadow1stColorTex") && target.HasProperty("_ShadeTexture"))
        {
            target.SetTexture("_ShadeTexture", source.GetTexture("_Shadow1stColorTex"));
        }
        if (source.HasProperty("_ShadowBorder") && target.HasProperty("_ShadeShift"))
        {
            float border = source.GetFloat("_ShadowBorder");
            target.SetFloat("_ShadeShift", (border - 0.5f) * 2.0f); // 近似逆变换
        }

        // 法线贴图
        if (source.HasProperty("_BumpMap") && target.HasProperty("_BumpMap"))
        {
            target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
        }
        if (source.HasProperty("_BumpScale") && target.HasProperty("_BumpScale"))
        {
            target.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
        }

        // 边缘光
        if (source.HasProperty("_RimColor") && target.HasProperty("_RimColor"))
        {
            target.SetColor("_RimColor", source.GetColor("_RimColor"));
        }
        if (source.HasProperty("_RimColorTex") && target.HasProperty("_RimTexture"))
        {
            target.SetTexture("_RimTexture", source.GetTexture("_RimColorTex"));
        }
        if (source.HasProperty("_RimStrength") && target.HasProperty("_RimLightingMix"))
        {
            target.SetFloat("_RimLightingMix", source.GetFloat("_RimStrength"));
        }
        if (source.HasProperty("_RimFresnelPower") && target.HasProperty("_RimFresnelPower"))
        {
            target.SetFloat("_RimFresnelPower", source.GetFloat("_RimFresnelPower"));
        }

        // MatCap
        if (source.HasProperty("_MatCapTex") && target.HasProperty("_SphereAdd"))
        {
            Texture matcapTexture = hasMatCap ? source.GetTexture("_MatCapTex") : null;
            target.SetTexture("_SphereAdd", matcapTexture);
        }
        if (source.HasProperty("_MatCapBlend") && target.HasProperty("_MatCapMul"))
        {
            target.SetFloat("_MatCapMul", source.GetFloat("_MatCapBlend"));
        }

        // 描边
        if (source.HasProperty("_OutlineColor") && target.HasProperty("_OutlineColor"))
        {
            target.SetColor("_OutlineColor", source.GetColor("_OutlineColor"));
        }
        if (source.HasProperty("_OutlineWidth") && target.HasProperty("_OutlineWidth"))
        {
            target.SetFloat("_OutlineWidth", source.GetFloat("_OutlineWidth"));
        }
        if (source.HasProperty("_OutlineStrength") && target.HasProperty("_OutlineLightingMix"))
        {
            target.SetFloat("_OutlineLightingMix", source.GetFloat("_OutlineStrength"));
        }
        if (target.HasProperty("_OutlineWidthMode"))
        {
            if (hasOutline)
            {
                target.EnableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
                target.EnableKeyword("MTOON_OUTLINE_COLOR_MIXED");
            }
            else
            {
                target.DisableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
                target.DisableKeyword("MTOON_OUTLINE_WIDTH_SCREEN");
                target.DisableKeyword("MTOON_OUTLINE_COLOR_MIXED");
                target.DisableKeyword("MTOON_OUTLINE_COLOR_FIXED");
            }
            float outlineMode = hasOutline ? 1.0f : 0.0f; // 世界坐标 (1.0)
            target.SetFloat("_OutlineWidthMode", outlineMode);
        }

        // 自发光
        if (hasEmission && source.HasProperty("_EmissionColor") && target.HasProperty("_EmissionColor"))
        {
            target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
        }
        else
        {
            target.SetColor("_EmissionColor", Color.black); // 未启用自发光时设为黑色
        }
        if (hasEmission && source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap"))
        {
            target.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
        }
        else
        {
            target.SetTexture("_EmissionMap", null); // 未启用自发光时清除贴图
        }

        // Alpha设置
        bool isTransparent = source.HasProperty("_TransparentMode") && source.GetInt("_TransparentMode") == (int)MToon10AlphaMode.Transparent;
        bool isCutout = (source.HasProperty("_TransparentMode") && source.GetInt("_TransparentMode") == (int)MToon10AlphaMode.Cutoff) || source.shader.name.Contains("Cutout");
        int renderQueueOffset = source.HasProperty("_RenderQueueOffset") ? source.GetInt("_RenderQueueOffset") : 0;

        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }
        else if (isCutout)
        {
            target.SetFloat("_Cutoff", 0.5f); // 默认的Cutoff值
        }

        if (source.HasProperty("_TransparentMode") && target.HasProperty("_BlendMode"))
        {
            target.SetFloat("_BlendMode", source.GetFloat("_TransparentMode"));
        }

        if (isCutout)
        {
            target.SetFloat("_BlendMode", 1.0f);
            target.SetOverrideTag("RenderType", "TransparentCutout");
            target.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            target.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            target.SetFloat("_AlphaToMask", 1.0f);
            target.EnableKeyword("_ALPHATEST_ON");
            target.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            target.SetFloat("_BlendMode", 0.0f);
            target.SetOverrideTag("RenderType", "Opaque");
            target.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            target.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            target.SetFloat("_AlphaToMask", 0.0f);
            target.renderQueue = -1;
        }

        // 渲染队列设置
        if (isTransparent)
        {
            target.renderQueue = 3000 + renderQueueOffset;
        }
        else if (isCutout)
        {
            target.renderQueue = 2450 + renderQueueOffset;
            target.SetInt("_ZWrite", 1); // Cutout时开启ZWrite
        }
        else
        {
            target.renderQueue = 2000 + renderQueueOffset;
            target.SetInt("_ZWrite", 1); // 不透明时开启ZWrite
        }

        // 光照调整
        if (source.HasProperty("_LightMinLimit") && target.HasProperty("_LightColorAttenuation"))
        {
            float minLimit = source.GetFloat("_LightMinLimit");
            target.SetFloat("_LightColorAttenuation", Mathf.Clamp01(minLimit * 10.0f)); // 近似逆变换
        }
    }

    // 将MToon10材质属性映射并复制到lilToon材质
    private void MapAndCopyPropertiesToLilToonFromMToon10(Material source, Material target)
    {
        // 法线贴图、边缘光、阴影、MatCap、描边、自发光的启用/禁用标志
        bool hasNormalMap = source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null;
        bool hasRimLight = (source.HasProperty("_RimColor") && source.GetColor("_RimColor") != Color.black) ||
                           (source.HasProperty("_RimTex") && source.GetTexture("_RimTex") != null);
        bool hasShadow = (source.HasProperty("_ShadeColor") && source.GetColor("_ShadeColor") != Color.black) ||
                         (source.HasProperty("_ShadeTex") && source.GetTexture("_ShadeTex") != null);
        bool hasMatCap = source.HasProperty("_MatcapTex") && source.GetTexture("_MatcapTex") != null &&
                         source.HasProperty("_MatcapColor") && source.GetColor("_MatcapColor") != Color.black;
        bool hasOutline = source.HasProperty("_OutlineWidthMode") && source.GetInt("_OutlineWidthMode") > 0 &&
                          source.HasProperty("_OutlineWidth") && source.GetFloat("_OutlineWidth") > 0;
        bool hasEmission = source.HasProperty("_EmissionColor") && source.GetColor("_EmissionColor") != Color.black ||
                           source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null;

        // 复制通用属性
        CopyCommonProperties(source, target);

        // 2. 明确映射MToon10和lilToon的属性
        // 渲染设置
        if (source.HasProperty("_AlphaMode") && target.HasProperty("_TransparentMode"))
        {
            int alphaMode = source.GetInt("_AlphaMode");
            target.SetInt("_TransparentMode", alphaMode);
        }
        if (source.HasProperty("_TransparentWithZWrite") && target.HasProperty("_ZWrite"))
        {
            target.SetInt("_ZWrite", source.GetInt("_TransparentWithZWrite"));
        }
        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }
        if (source.HasProperty("_DoubleSided") && target.HasProperty("_Cull"))
        {
            int doubleSided = source.GetInt("_DoubleSided");
            target.SetInt("_Cull", doubleSided == 1 ? 0 : 2);
        }

        // 主颜色和贴图
        if (source.HasProperty("_Color") && target.HasProperty("_BaseColor"))
        {
            target.SetColor("_BaseColor", source.GetColor("_Color"));
        }
        if (source.HasProperty("_MainTex") && target.HasProperty("_BaseMap"))
        {
            target.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            target.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
        }

        // 阴影
        if (source.HasProperty("_ShadeColor") && target.HasProperty("_Shadow2ndColor"))
        {
            Color shadeColor = source.GetColor("_ShadeColor");
            target.SetColor("_Shadow2ndColor", shadeColor);
            if (target.HasProperty("_ShadowColor"))
            {
                target.SetColor("_ShadowColor", Color.Lerp(Color.white, shadeColor, 0.5f));
            }
        }
        if (source.HasProperty("_ShadeTex") && target.HasProperty("_Shadow1stColorTex"))
        {
            target.SetTexture("_Shadow1stColorTex", source.GetTexture("_ShadeTex"));
        }
        if (source.HasProperty("_ShadingShiftFactor") && target.HasProperty("_ShadowBorder"))
        {
            float shift = source.GetFloat("_ShadingShiftFactor");
            target.SetFloat("_ShadowBorder", Mathf.Clamp01(0.5f + shift * 0.5f));
        }
        if (source.HasProperty("_ShadingToonyFactor") && target.HasProperty("_ShadowStrength"))
        {
            target.SetFloat("_ShadowStrength", source.GetFloat("_ShadingToonyFactor"));
        }
        if (target.HasProperty("_UseShadow"))
        {
            target.SetFloat("_UseShadow", hasShadow ? 1.0f : 0.0f);
        }

        // 法线贴图
        if (source.HasProperty("_BumpMap") && target.HasProperty("_BumpMap"))
        {
            target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
        }
        if (source.HasProperty("_BumpScale") && target.HasProperty("_BumpScale"))
        {
            target.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
        }
        if (target.HasProperty("_UseBumpMap"))
        {
            target.SetFloat("_UseBumpMap", hasNormalMap ? 1.0f : 0.0f);
        }

        // 边缘光
        if (source.HasProperty("_RimColor") && target.HasProperty("_RimColor"))
        {
            target.SetColor("_RimColor", source.GetColor("_RimColor"));
        }
        if (source.HasProperty("_RimTex") && target.HasProperty("_RimColorTex"))
        {
            target.SetTexture("_RimColorTex", source.GetTexture("_RimTex"));
        }
        if (source.HasProperty("_RimLightingMix") && target.HasProperty("_RimStrength"))
        {
            target.SetFloat("_RimStrength", source.GetFloat("_RimLightingMix"));
        }
        if (source.HasProperty("_RimFresnelPower") && target.HasProperty("_RimFresnelPower"))
        {
            target.SetFloat("_RimFresnelPower", source.GetFloat("_RimFresnelPower"));
        }
        if (target.HasProperty("_UseRim"))
        {
            target.SetFloat("_UseRim", hasRimLight ? 1.0f : 0.0f);
        }

        // MatCap
        if (hasMatCap && source.HasProperty("_MatcapTex") && target.HasProperty("_MatCapTex"))
        {
            target.SetTexture("_MatCapTex", source.GetTexture("_MatcapTex"));
        }
        if (hasMatCap && source.HasProperty("_MatcapColor") && target.HasProperty("_MatCapBlend"))
        {
            target.SetFloat("_MatCapBlend", source.GetColor("_MatcapColor").maxColorComponent);
        }
        if (target.HasProperty("_UseMatCap"))
        {
            target.SetFloat("_UseMatCap", hasMatCap ? 1.0f : 0.0f);
        }

        // 描边
        if (hasOutline && source.HasProperty("_OutlineColor") && target.HasProperty("_OutlineColor"))
        {
            target.SetColor("_OutlineColor", source.GetColor("_OutlineColor"));
        }
        if (hasOutline && source.HasProperty("_OutlineWidth") && target.HasProperty("_OutlineWidth"))
        {
            target.SetFloat("_OutlineWidth", source.GetFloat("_OutlineWidth") * 100.0f); // MToon10的取值范围需放大100倍
        }
        if (target.HasProperty("_UseOutline"))
        {
            if (hasOutline)
            {
                target.SetFloat("_UseOutline", 1);
                Shader lilToonMultiOutlineShader = Shader.Find("Hidden/lilToonMultiOutline");
                if (lilToonMultiOutlineShader == null)
                {
                    Debug.LogError("未找到lilToonMultiOutline着色器！请导入lilToonMultiOutline。");
                }
                else
                {
                    target.shader = lilToonMultiOutlineShader;
                }
            }
            else
            {
                target.SetFloat("_UseOutline", 0);
            }
        }
        // 自发光
        if (hasEmission && source.HasProperty("_EmissionColor") && target.HasProperty("_EmissionColor"))
        {
            target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
        }
        if (hasEmission && source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap"))
        {
            target.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
        }
        if (target.HasProperty("_UseEmission"))
        {
            target.SetFloat("_UseEmission", hasEmission ? 1.0f : 0.0f);
        }

        // Alpha设置（处理Cutout和透明）
        bool isTransparent = source.HasProperty("_AlphaMode") && source.GetInt("_AlphaMode") == (int)MToon10AlphaMode.Transparent;
        bool isCutout = source.HasProperty("_AlphaMode") && source.GetInt("_AlphaMode") == (int)MToon10AlphaMode.Cutoff;
        int renderQueueOffset = source.HasProperty("_RenderQueueOffset") ? source.GetInt("_RenderQueueOffset") : 0;

        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }
        else if (isCutout)
        {
            target.SetFloat("_Cutoff", 0.5f); // 默认的Cutoff值
        }

        if (isTransparent)
        {
            Shader lilToonTransparentShader = Shader.Find("Hidden/lilToonTransparent");
            if (hasOutline)
            {
            lilToonTransparentShader = Shader.Find("Hidden/lilToonTransparentOutline");
            }
            if (lilToonTransparentShader == null)
            {
            Debug.LogError("未找到lilToonTransparent相关着色器！请导入lilToonTransparent。");
            }
            else
            {
            target.shader = lilToonTransparentShader;
            target.SetFloat("_Cutoff", 0.001f);
            target.SetInt("_ZWrite", source.HasProperty("_TransparentWithZWrite") ? source.GetInt("_TransparentWithZWrite") : 1);
            target.renderQueue = 3000 + renderQueueOffset;
            }
        }
        else if (isCutout)
        {
            Shader lilToonCutoutShader = Shader.Find("Hidden/lilToonCutout");
            if (hasOutline)
            {
            lilToonCutoutShader = Shader.Find("Hidden/lilToonCutoutOutline");
            }
            if (lilToonCutoutShader == null)
            {
            Debug.LogError("未找到lilToonCutout相关着色器！请导入lilToonCutout。");
            }
            else
            {
            target.shader = lilToonCutoutShader;
            target.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
            target.SetInt("_ZWrite", 1);
            target.renderQueue = 2450 + renderQueueOffset;
            }
        }
        else
        {
            // 不透明材质
            Shader targetShader = Shader.Find("lilToon");
            if (hasOutline)
            {
            targetShader = Shader.Find("Hidden/lilToonMultiOutline");
            if (targetShader == null)
            {
                Debug.LogError("未找到lilToonMultiOutline着色器！请导入lilToonMultiOutline。");
            }
            }
            if (targetShader != null)
            {
            target.shader = targetShader;
            target.SetInt("_ZWrite", 1);
            target.renderQueue = 2000 + renderQueueOffset;
            }
        }
        }
    // 将lilToon材质属性映射并复制到MToon10材质
    private void MapAndCopyPropertiesToMToon10FromLilToon(Material source, Material target)
    {
        // 法线贴图、边缘光、阴影、MatCap、描边、自发光的启用/禁用标志
        bool hasNormalMap = source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null;
        bool hasRimLight = (source.HasProperty("_RimColor") && source.GetColor("_RimColor") != Color.black) ||
                           (source.HasProperty("_RimColorTex") && source.GetTexture("_RimColorTex") != null);
        bool hasShadow = (source.HasProperty("_Shadow2ndColor") && source.GetColor("_Shadow2ndColor") != Color.black) ||
                         (source.HasProperty("_Shadow1stColorTex") && source.GetTexture("_Shadow1stColorTex") != null);
        bool hasMatCap = source.HasProperty("_MatCapTex") && source.GetTexture("_MatCapTex") != null &&
                         source.HasProperty("_UseMatCap") && source.GetFloat("_UseMatCap") > 0;
        bool hasOutline = (source.HasProperty("_UseOutline") && source.GetFloat("_UseOutline") > 0 &&
                          source.HasProperty("_OutlineWidth") && source.GetFloat("_OutlineWidth") > 0) ||
                          source.shader.name.Contains("Outline"); // 检查是否使用Outline着色器
        bool hasEmission = source.HasProperty("_UseEmission") && source.GetFloat("_UseEmission") > 0;

        // 复制通用属性
        CopyCommonProperties(source, target);

        // 2. 明确映射lilToon和MToon10的属性
        // 渲染设置
        if (source.HasProperty("_TransparentMode") && target.HasProperty("_AlphaMode"))
        {
            int transparentMode = source.GetInt("_TransparentMode");
            if (transparentMode == 1)
            {
                target.SetInt("_AlphaMode", (int)MToon10AlphaMode.Cutoff);
            }
            else if (transparentMode == 2)
            {
                target.SetInt("_AlphaMode", (int)MToon10AlphaMode.Transparent);
            }
            else
            {
                target.SetInt("_AlphaMode", (int)MToon10AlphaMode.Opaque);
            }
        }
        if (source.HasProperty("_ZWrite") && target.HasProperty("_TransparentWithZWrite"))
        {
            target.SetInt("_TransparentWithZWrite", source.GetInt("_ZWrite"));
        }
        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }
        if (source.HasProperty("_Cull") && target.HasProperty("_DoubleSided"))
        {
            int cullMode = source.GetInt("_Cull");
            target.SetInt("_DoubleSided", cullMode == 0 ? 1 : 0);
        }

        // 主颜色和贴图
        if (source.HasProperty("_BaseColor") && target.HasProperty("_Color"))
        {
            target.SetColor("_Color", source.GetColor("_BaseColor"));
        }
        if (source.HasProperty("_BaseMap") && target.HasProperty("_MainTex"))
        {
            target.SetTexture("_MainTex", source.GetTexture("_BaseMap"));
            target.SetTextureOffset("_MainTex", source.GetTextureOffset("_BaseMap"));
            target.SetTextureScale("_MainTex", source.GetTextureScale("_BaseMap"));
        }

        // 阴影
        if (source.HasProperty("_Shadow2ndColor") && target.HasProperty("_ShadeColor"))
        {
            target.SetColor("_ShadeColor", source.GetColor("_Shadow2ndColor"));
        }
        if (source.HasProperty("_Shadow1stColorTex") && target.HasProperty("_ShadeTex"))
        {
            target.SetTexture("_ShadeTex", source.GetTexture("_Shadow1stColorTex"));
        }
        if (source.HasProperty("_ShadowBorder") && target.HasProperty("_ShadingShiftFactor"))
        {
            float border = source.GetFloat("_ShadowBorder");
            target.SetFloat("_ShadingShiftFactor", (border - 0.5f) * 2.0f);
        }
        if (source.HasProperty("_ShadowStrength") && target.HasProperty("_ShadingToonyFactor"))
        {
            target.SetFloat("_ShadingToonyFactor", source.GetFloat("_ShadowStrength"));
        }
    

        // 法线贴图
        if (source.HasProperty("_BumpMap") && target.HasProperty("_BumpMap"))
        {
            target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
        }
        if (source.HasProperty("_BumpScale") && target.HasProperty("_BumpScale"))
        {
            target.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
        }

        // 边缘光
        if (source.HasProperty("_RimColor") && target.HasProperty("_RimColor"))
        {
            target.SetColor("_RimColor", source.GetColor("_RimColor"));
        }
        if (source.HasProperty("_RimColorTex") && target.HasProperty("_RimTex"))
        {
            target.SetTexture("_RimTex", source.GetTexture("_RimColorTex"));
        }
        if (source.HasProperty("_RimStrength") && target.HasProperty("_RimLightingMix"))
        {
            target.SetFloat("_RimLightingMix", source.GetFloat("_RimStrength"));
        }
        if (source.HasProperty("_RimFresnelPower") && target.HasProperty("_RimFresnelPower"))
        {
            target.SetFloat("_RimFresnelPower", source.GetFloat("_RimFresnelPower"));
        }

        // MatCap（马特帽/环境贴图）
        if (hasMatCap && source.HasProperty("_MatCapTex") && target.HasProperty("_MatcapTex"))
        {
            target.SetTexture("_MatcapTex", source.GetTexture("_MatCapTex"));
        }
        if (hasMatCap && source.HasProperty("_MatCapBlend") && target.HasProperty("_MatcapColor"))
        {
            target.SetColor("_Invitation", new Color(source.GetFloat("_MatCapBlend"), source.GetFloat("_MatCapBlend"), source.GetFloat("_MatCapBlend"), 1.0f));
        }
        else
        {
            target.SetColor("_MatcapColor", Color.black); // 未使用MatCap时设为黑色
        }

        // 描边
        if (hasOutline && source.HasProperty("_OutlineColor") && target.HasProperty("_OutlineColor"))
        {
            target.SetColor("_OutlineColor", source.GetColor("_OutlineColor"));
        }
        if (hasOutline && source.HasProperty("_OutlineWidth") && target.HasProperty("_OutlineWidth"))
        {
            target.SetFloat("_OutlineWidth", source.GetFloat("_OutlineWidth") * 0.01f); // 将lilToon的取值范围缩小为0.01倍以适配MToon10
        }
        if (source.HasProperty("_OutlineStrength") && target.HasProperty("_OutlineLightingMix"))
        {
            target.SetFloat("_OutlineLightingMix", source.GetFloat("_OutlineStrength"));
        }
        if (target.HasProperty("_OutlineWidthMode"))
        {
            if (hasOutline)
            {
                target.EnableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
                target.EnableKeyword("MTOON_OUTLINE_COLOR_MIXED");
                target.SetInt("_OutlineWidthMode", 1); // WorldCoordinates
            }
            else
            {
                target.DisableKeyword("MTOON_OUTLINE_WIDTH_WORLD");
                target.DisableKeyword("MTOON_OUTLINE_WIDTH_SCREEN");
                target.DisableKeyword("MTOON_OUTLINE_COLOR_MIXED");
                target.DisableKeyword("MTOON_OUTLINE_COLOR_FIXED");
                target.SetInt("_OutlineWidthMode", 0);
            }
        }

        // 自发光
        if (hasEmission && source.HasProperty("_EmissionColor") && target.HasProperty("_EmissionColor"))
        {
            target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
        }
        else
        {
            target.SetColor("_EmissionColor", Color.black); // 未启用自发光时设为黑色
        }
        if (hasEmission && source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap"))
        {
            target.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
        }
        else
        {
            target.SetTexture("_EmissionMap", null); // 未启用自发光时清除贴图
        }

        // Alpha设置
        bool isTransparent = source.HasProperty("_TransparentMode") && source.GetInt("_TransparentMode") == (int)MToon10AlphaMode.Transparent;
        bool isCutout = source.HasProperty("_TransparentMode") && source.GetInt("_TransparentMode") == (int)MToon10AlphaMode.Cutoff;
        int renderQueueOffset = source.HasProperty("_RenderQueueOffset") ? source.GetInt("_RenderQueueOffset") : 0;

        if (source.HasProperty("_Cutoff") && target.HasProperty("_Cutoff"))
        {
            target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
        }
        else if (isCutout)
        {
            target.SetFloat("_Cutoff", 0.5f); // 默认的Cutoff值
        }

        // 渲染队列设置
        if (isTransparent)
        {
            target.renderQueue = 3000 + renderQueueOffset;
            target.SetInt("_TransparentWithZWrite", source.HasProperty("_ZWrite") ? source.GetInt("_ZWrite") : 1);
        }
        else if (isCutout)
        {
            target.renderQueue = 2450 + renderQueueOffset;
            target.SetInt("_TransparentWithZWrite", 1); // Cutout时开启ZWrite
        }
        else
        {
            target.renderQueue = 2000 + renderQueueOffset;
            target.SetInt("_TransparentWithZWrite", 1); // 不透明时开启ZWrite
        }
    }

    private void ApplyLilToonDefaultSettings(Material material)
    {
        // lilToon特有的默认设置（优先还原MToon的表现并做适当调整）
        if (material.HasProperty("_LightMinLimit") && !material.HasProperty("_LightColorAttenuation"))
        {
            material.SetFloat("_LightMinLimit", 0.05f); // 最小光照限制
        }
        if (material.HasProperty("_LightMaxLimit"))
        {
            material.SetFloat("_LightMaxLimit", 1.0f); // 最大光照限制
        }
        if (material.HasProperty("_ShadowStrength") && material.HasProperty("_ShadeColor"))
        {
            material.SetFloat("_ShadowStrength", 0.8f); // 阴影强度
        }
        if (material.HasProperty("_AsUnlit"))
        {
            material.SetFloat("_AsUnlit", 0.0f); // 关闭Unlit模式
        }
        if (material.HasProperty("_Cutoff"))
        {
            material.SetFloat("_Cutoff", 0.5f); // Alpha裁剪阈值
        }
        // 不要覆盖MatCap（保留已有贴图）
        if (material.HasProperty("_EmissionColor") && !material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.black); // 默认关闭自发光
        }
        if (material.HasProperty("_UseOutline") && !material.HasProperty("_OutlineColor"))
        {
            material.SetFloat("_UseOutline", 0); // 默认禁用描边
        }
    }
}