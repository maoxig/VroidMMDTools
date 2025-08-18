using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace MMDShader2MToon
{
    /// <summary>
    /// 扩展Material类，提供常用的属性获取方法
    /// </summary>
public static class MaterialExtension
{
    public static float GetFloat(this Material mat, string propName, float defaultValue = 0f)
        => mat.HasProperty(propName) ? mat.GetFloat(propName) : defaultValue;

    public static Color GetColor(this Material mat, string propName, Color defaultValue = default)
        => mat.HasProperty(propName) ? mat.GetColor(propName) : defaultValue;

    public static int GetInt(this Material mat, string propName, int defaultValue = 0)
        => mat.HasProperty(propName) ? (int)mat.GetFloat(propName) : defaultValue;

    public static Texture GetTexture(this Material mat, string propName, Texture defaultValue = null)
        => mat.HasProperty(propName) ? mat.GetTexture(propName) : defaultValue;

    public static Vector4 GetVector(this Material mat, string propName, Vector4 defaultValue = default)
        => mat.HasProperty(propName) ? mat.GetVector(propName) : defaultValue;
}

    public class MMDToMTOONConverter : EditorWindow
    {
        private const int BLENDMODE_OPAQUE = 0;
        private const int BLENDMODE_CUTOUT = 1;
        private const int BLENDMODE_TRANSPARENT = 2;
        private const int BLENDMODE_TRANSPARENT_WITH_ZWRITE = 3;

        private static Shader mtoonShader;
        private static int processedCount = 0;
        private static int totalCount = 0;
        private static ConversionSettings settings = new ConversionSettings();
        private Vector2 scrollPosition;

        [System.Serializable]
        private class ConversionSettings
        {
            public float defaultOutlineWidth = 0.1f;
            public float outlineWidthScaleTransparent = 0.7f;
            public float outlineWidthScaleOpaque = 0.5f;
            public float shadeToony = 0.9f;
            public float shadeShift = 0.0f;
            public float indirectLightIntensity = 0.1f;
            public float receiveShadowRate = 1.0f;
            public float cutoffThreshold = 0.5f;
            public float shininessScale = 1.0f;
            public float ambientToDiffuseScale = 5.0f;
            public float toonToneScale = 1.0f;
            public float shadowLumScale = 1.0f;
            public Vector4 defaultToonTone = new Vector4(1.0f, 0.5f, 0.5f, 0.0f);
            public bool handleNoShadowCasting = true;
            public bool overrideCullMode = false;
            public int defaultCullMode = 2; // Back
        }

        [MenuItem("ShaderTools/MMD to MToon Converter")]
        private static void ShowWindow()
        {
            var window = GetWindow<MMDToMTOONConverter>("MMD to MToon Converter");
            window.minSize = new Vector2(400, 550);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("MMD to MToon Conversion Settings", EditorStyles.boldLabel);

            settings.defaultOutlineWidth = EditorGUILayout.FloatField("Default Outline Width", settings.defaultOutlineWidth);
            settings.outlineWidthScaleTransparent = EditorGUILayout.FloatField("Outline Scale (Transparent)", settings.outlineWidthScaleTransparent);
            settings.outlineWidthScaleOpaque = EditorGUILayout.FloatField("Outline Scale (Opaque)", settings.outlineWidthScaleOpaque);
            settings.shadeToony = EditorGUILayout.Slider("Shade Toony", settings.shadeToony, 0f, 1f);
            settings.shadeShift = EditorGUILayout.Slider("Shade Shift", settings.shadeShift, -1f, 1f);
            settings.indirectLightIntensity = EditorGUILayout.Slider("Indirect Light Intensity", settings.indirectLightIntensity, 0f, 1f);
            settings.receiveShadowRate = EditorGUILayout.Slider("Receive Shadow Rate", settings.receiveShadowRate, 0f, 1f);
            settings.cutoffThreshold = EditorGUILayout.Slider("Cutoff Threshold", settings.cutoffThreshold, 0f, 1f);
            settings.shininessScale = EditorGUILayout.FloatField("Shininess Scale", settings.shininessScale);
            settings.ambientToDiffuseScale = EditorGUILayout.FloatField("Ambient to Diffuse Scale", settings.ambientToDiffuseScale);
            settings.toonToneScale = EditorGUILayout.FloatField("Toon Tone Scale", settings.toonToneScale);
            settings.shadowLumScale = EditorGUILayout.FloatField("Shadow Luminance Scale", settings.shadowLumScale);
            GUILayout.Label("Default Toon Tone (x: Intensity, y: Color Scale, z: Shift)", EditorStyles.boldLabel);
            settings.defaultToonTone = EditorGUILayout.Vector4Field("Default Toon Tone", settings.defaultToonTone);
            settings.handleNoShadowCasting = EditorGUILayout.Toggle("Handle NoShadowCasting", settings.handleNoShadowCasting);
            settings.overrideCullMode = EditorGUILayout.Toggle("Override Cull Mode", settings.overrideCullMode);
            if (settings.overrideCullMode)
            {
                settings.defaultCullMode = EditorGUILayout.Popup("Default Cull Mode", settings.defaultCullMode, new[] { "Off", "Front", "Back" });
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Convert Selected Materials"))
            {
                ConvertMaterialsMenu();
            }

            EditorGUILayout.EndScrollView();
        }

        private static void ConvertMaterialsMenu()
        {
            mtoonShader = FindMTOONShader();
            if (mtoonShader == null)
            {
                Debug.LogError("MToon Shader not found! Please ensure UniVRM is correctly imported.");
                return;
            }

            ConvertSelectedMaterials();
        }

        private static Shader FindMTOONShader()
        {
            Shader[] candidatePaths = {
            Shader.Find("VRM/MToon"),
            Shader.Find("UniVRM/MToon"),
            Shader.Find("MToon"),
        };

            foreach (var shader in candidatePaths)
            {
                if (shader != null)
                {
                    Debug.Log($"Found MToon Shader: {shader.name}");
                    return shader;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:Shader MToon");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && shader.name.Contains("MToon"))
                {
                    Debug.Log($"Found MToon Shader: {shader.name}");
                    return shader;
                }
            }

            return null;
        }

        private static void ConvertSelectedMaterials()
        {
            var selected = Selection.objects;
            var materials = new List<Material>();

            foreach (var obj in selected)
            {
                if (obj is GameObject go)
                    materials.AddRange(CollectMaterialsFromGameObject(go));
                else if (obj is Material mat)
                    materials.Add(mat);
                else if (obj is ModelImporter modelImporter)
                    materials.AddRange(GetMaterialsFromModelImporter(modelImporter));
                else if (PrefabUtility.IsPartOfPrefabAsset(obj) && obj is GameObject prefabGo)
                    materials.AddRange(CollectMaterialsFromGameObject(prefabGo));
            }

            materials = materials.Distinct().ToList();
            if (materials.Count == 0)
            {
                Debug.Log("No convertible materials detected.");
                return;
            }

            processedCount = 0;
            totalCount = materials.Count;
            EditorUtility.DisplayProgressBar("Converting Materials", "Preparing...", 0);

            try
            {
                foreach (var material in materials)
                {
                    EditorUtility.DisplayProgressBar("Converting Materials",
                        $"Processing {processedCount + 1}/{totalCount}: {material.name}",
                        (float)processedCount / totalCount);

                    ConvertSingleMaterial(material);
                    processedCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Successfully converted {processedCount} materials to MToon Shader.");
        }

        private static IEnumerable<Material> CollectMaterialsFromGameObject(GameObject go)
        {
            var materials = new List<Material>();
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials != null)
                    materials.AddRange(renderer.sharedMaterials.Where(m => m != null));
            }
            return materials;
        }

        private static IEnumerable<Material> GetMaterialsFromModelImporter(ModelImporter importer)
        {
            var materials = new List<Material>();

#if UNITY_2021_1_OR_NEWER
            try
            {
                var assetPath = AssetDatabase.GetAssetPath(importer);
                var dependencies = AssetDatabase.GetDependencies(assetPath, true);

                foreach (var depPath in dependencies)
                {
                    var depObj = AssetDatabase.LoadAssetAtPath<Object>(depPath);
                    if (depObj is Material mat)
                        materials.Add(mat);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to get materials via new method: {e.Message}");
            }
#endif

            try
            {
                var method = importer.GetType().GetMethod("GetMaterials");
                if (method != null)
                {
                    var result = method.Invoke(importer, null);
                    if (result is Material[] matArray)
                        materials.AddRange(matArray.Where(m => m != null));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to get materials via reflection: {e.Message}");
            }

            return materials;
        }

        private static void ConvertSingleMaterial(Material material)
        {
            if (material == null || material.shader == mtoonShader || material.shader.name.Contains("MMDLit-Dummy"))
            {
                Debug.Log($"Skipped material: {material?.name ?? "null"} (Dummy or already MToon)");
                return;
            }

            try
            {
                string originalShaderName = material.shader.name;
                var features = ParseMMDShaderFeatures(originalShaderName, material);

                // Preserve MMD material properties
                Color mainColor = material.GetColor("_Color", Color.white);
                float alpha = mainColor.a;
                Color specularColor = material.GetColor("_Specular", Color.white);
                Color ambientColor = material.GetColor("_Ambient", Color.white);
                float shininess = material.GetFloat("_Shininess", 0f);
                float shadowLum = material.GetFloat("_ShadowLum", 1.5f);
                float ambientToDiffuse = material.GetFloat("_AmbientToDiffuse", settings.ambientToDiffuseScale);
                Color edgeColor = material.GetColor("_EdgeColor", Color.black);
                float edgeScale = material.GetFloat("_EdgeScale", settings.defaultOutlineWidth);
                float edgeSize = material.GetFloat("_EdgeSize", settings.defaultOutlineWidth);
                Color emissiveColor = material.GetColor("_Emissive", Color.black);
                Vector4 toonTone = material.GetVector("_ToonTone", settings.defaultToonTone);
                float noShadowCasting = material.GetFloat("_NoShadowCasting", 0f);
                Texture mainTex = material.GetTexture("_MainTex");
                Texture toonTex = material.GetTexture("_ToonTex");
                Texture sphereCube = material.GetTexture("_SphereCube");
                float addLightToonCen = material.GetFloat("_AddLightToonCen", -0.1f);
                float addLightToonMin = material.GetFloat("_AddLightToonMin", 0.5f);
                float alPower = material.GetFloat("_ALPower", 0f);

                // Log unmapped properties
                if (material.HasProperty("_AddLightToonCen") || material.HasProperty("_AddLightToonMin") || material.HasProperty("_ALPower"))
                {
                    Debug.LogWarning($"Material {material.name}: Properties _AddLightToonCen ({addLightToonCen}), _AddLightToonMin ({addLightToonMin}), and _ALPower ({alPower}) are not directly mapped to MToon. Using _ShadeToony to approximate.");
                }

                // Log if sphereCube is a Cube texture
                if (sphereCube != null && sphereCube is Cubemap)
                {
                    Debug.LogWarning($"Material {material.name}: Cannot assign Cube texture '_SphereCube' to MToon '_SphereAdd' (2D texture required). Skipping.");
                }

                // Determine alpha and blend mode
                int blendMode = BLENDMODE_OPAQUE;
                if (features.isTransparent || (material.HasProperty("_Color") && alpha < 0.99f))
                {
                    if (material.HasProperty("_Cutoff") && material.GetFloat("_Cutoff", settings.cutoffThreshold) > 0.01f)
                    {
                        blendMode = BLENDMODE_CUTOUT;
                    }
                    else
                    {
                        blendMode = alpha <= 0.01f ? BLENDMODE_TRANSPARENT : BLENDMODE_TRANSPARENT_WITH_ZWRITE;
                    }
                }
                else if (material.HasProperty("_Cutoff") && material.GetFloat("_Cutoff", settings.cutoffThreshold) > 0.01f)
                {
                    blendMode = BLENDMODE_CUTOUT;
                }

                // Replace shader
                material.shader = mtoonShader;

                // Set blend mode parameters
                SetBlendModeParameters(material, blendMode);

                // Set main color and alpha
                material.SetColor("_Color", mainColor);

                // Map textures
                MapTexture(material, "_MainTex", mainTex);
                MapTexture(material, "_ShadeTexture", toonTex);
                if (sphereCube != null && !(sphereCube is Cubemap))
                {
                    MapTexture(material, "_SphereAdd", sphereCube);
                }

                // Map toon shading
                float shadeShift = material.HasProperty("_ToonTone") ? toonTone.z * settings.toonToneScale : settings.shadeShift;
                material.SetFloat("_ShadeToony", Mathf.Clamp01(toonTone.x * settings.toonToneScale));
                material.SetFloat("_ShadeShift", Mathf.Clamp(shadeShift, -1f, 1f));
                Color shadeColor = Color.Lerp(mainColor * toonTone.y, ambientColor, ambientToDiffuse / 10f);
                material.SetColor("_ShadeColor", shadeColor);

                // Map specular to rim lighting
                material.SetColor("_RimColor", specularColor);
                material.SetFloat("_RimFresnelPower", shininess * settings.shininessScale);
                material.SetFloat("_RimLightingMix", Mathf.Clamp01(shininess / 10f));

                // Approximate additional lighting properties
                if (material.HasProperty("_AddLightToonCen") && material.HasProperty("_AddLightToonMin"))
                {
                    material.SetFloat("_LightColorAttenuation", Mathf.Clamp01((addLightToonCen + addLightToonMin) / 2f));
                }

                // Map emission
                material.SetColor("_EmissionColor", emissiveColor);

                // Map outline
                if (features.hasOutline)
                {
                    material.SetInt("_OutlineWidthMode", 1); // World space outline
                    material.SetFloat("_OutlineWidth", (edgeScale + edgeSize) * 0.5f * (features.isTransparent ? settings.outlineWidthScaleTransparent : settings.outlineWidthScaleOpaque));
                    material.SetColor("_OutlineColor", edgeColor);
                    material.SetInt("_OutlineCullMode", features.isBothFaces ? 0 : 1);
                    material.SetFloat("_OutlineLightingMix", 0.5f);
                }
                else
                {
                    material.SetInt("_OutlineWidthMode", 0);
                }

                // Map lighting and shadows
                float indirectLight = material.HasProperty("_Ambient") ? settings.indirectLightIntensity * Mathf.Clamp01(ambientColor.grayscale) : settings.indirectLightIntensity;
                material.SetFloat("_IndirectLightIntensity", indirectLight);
                material.SetFloat("_ReceiveShadowRate", settings.handleNoShadowCasting && (features.noShadowCasting || noShadowCasting > 0f) ? 0f : settings.receiveShadowRate * shadowLum * settings.shadowLumScale);

                // Set culling mode
                material.SetInt("_CullMode", settings.overrideCullMode ? settings.defaultCullMode : (features.isBothFaces ? 0 : 2));

                if (AssetDatabase.Contains(material))
                {
                    EditorUtility.SetDirty(material);
                }

                Debug.Log($"Converted material: {material.name} | BlendMode: {GetBlendModeName(blendMode)} | Alpha: {alpha} | CullMode: {(features.isBothFaces ? "Off" : "Back")} | NoShadowCasting: {features.noShadowCasting} | ShadeShift: {shadeShift} | IndirectLight: {indirectLight}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to convert material {material.name}: {e.Message}");
            }
        }

        private static void SetBlendModeParameters(Material material, int blendMode)
        {
            switch (blendMode)
            {
                case BLENDMODE_OPAQUE:
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    material.SetOverrideTag("RenderType", "Opaque");
                    break;

                case BLENDMODE_CUTOUT:
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.SetFloat("_Cutoff", material.GetFloat("_Cutoff", settings.cutoffThreshold));
                    material.SetInt("_AlphaToMask", 1);
                    break;

                case BLENDMODE_TRANSPARENT:
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_AlphaToMask", 0);
                    break;

                case BLENDMODE_TRANSPARENT_WITH_ZWRITE:
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_AlphaToMask", 0);
                    break;
            }
        }

        private static string GetBlendModeName(int blendMode)
        {
            return blendMode switch
            {
                BLENDMODE_OPAQUE => "Opaque",
                BLENDMODE_CUTOUT => "Cutout",
                BLENDMODE_TRANSPARENT => "Transparent",
                BLENDMODE_TRANSPARENT_WITH_ZWRITE => "TransparentWithZWrite",
                _ => "Unknown"
            };
        }

        private static void MapTexture(Material material, string propertyName, Texture texture)
        {
            if (texture != null && material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, texture);
                material.SetTextureScale(propertyName, material.GetTextureScale(propertyName));
                material.SetTextureOffset(propertyName, material.GetTextureOffset(propertyName));
            }
            else if (texture != null)
            {
                Debug.LogWarning($"Material {material.name}: Property {propertyName} not found in MToon shader. Skipping texture assignment.");
            }
        }

        private static (bool isTransparent, bool isBothFaces, bool hasOutline, bool noShadowCasting)
            ParseMMDShaderFeatures(string shaderName, Material material)
        {
            shaderName = shaderName.ToLower();
            bool isTransparent = shaderName.Contains("transparent") || material.GetTag("RenderType", false, "") == "Transparent";
            bool isBothFaces = shaderName.Contains("bothfaces") || shaderName.Contains("doublesided") || shaderName.Contains("twosided");
            bool hasOutline = shaderName.Contains("edge") || shaderName.Contains("outline");
            bool noShadowCasting = shaderName.Contains("noshadowcasting") || material.GetFloat("_NoShadowCasting", 0f) > 0f || material.GetTag("ForceNoShadowCasting", false, "") == "True";

            return (isTransparent, isBothFaces, hasOutline, noShadowCasting);
        }
    }
}