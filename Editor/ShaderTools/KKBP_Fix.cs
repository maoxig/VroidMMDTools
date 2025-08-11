using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRoidTools
{
    public class VRoidMToonMaterialSetup : EditorWindow
    {
        private string selectedFbxPath;
        private string selectedFolderPath;
        private string textureFolderName = "baked_files";
        private Shader mtoonShader;
        private Vector2 scrollPosition;
        private List<string> logMessages = new List<string>();

        // 新增选项 - 使用整数表示剔除模式（0=Off, 2=Back）
        private bool setEyeMaterialsToTransparent = true;
        private int cullMode = 0; // 默认0=Off
        private string[] cullModeOptions = new string[] { "Off (0)", "Back (2)" };
        private int[] cullModeValues = new int[] { 0, 2 };

        // 根据新提供的Shader定义修正的属性名
        private const string LitColorProperty = "_Color";               // 亮部颜色
        private const string ShadeColorProperty = "_ShadeColor";         // 暗部颜色
        private const string LitTextureProperty = "_MainTex";            // 亮部贴图
        private const string ShadeTextureProperty = "_ShadeTexture";     // 暗部贴图

        private enum TextureType
        {
            Light,  // 对应亮部贴图
            Dark    // 对应暗部贴图
        }
        // 新增描边选项
        private bool addOutlineToOpaqueMaterials = false; // 开关
        private float outlineWidth = 0.02f; // 描边宽度（默认0.02）

        [MenuItem("Assets/Tools/恋活配置为MToon", false, 10)]
        public static void SetupFromContextMenu()
        {
            var window = GetWindow<VRoidMToonMaterialSetup>("恋活配置为MToon");
            window.TryGetSelectedFbxPath();
        }

        [MenuItem("Tools/恋活配置为MToon")]
        public static void ShowWindow()
        {
            var window = GetWindow<VRoidMToonMaterialSetup>("恋活配置为MToon");
            window.TryGetSelectedFbxPath();
        }

        private void OnEnable()
        {
            LoadMToonShader();
            TryGetSelectedFbxPath();
        }

        private void OnSelectionChange()
        {
            TryGetSelectedFbxPath();
            Repaint();
        }

        private void TryGetSelectedFbxPath()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects != null && selectedObjects.Length == 1)
            {
                string assetPath = AssetDatabase.GetAssetPath(selectedObjects[0]);
                if (!string.IsNullOrEmpty(assetPath) && Path.GetExtension(assetPath).Equals(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    selectedFbxPath = assetPath;
                    selectedFolderPath = Path.GetDirectoryName(assetPath);
                    return;
                }
            }
            selectedFbxPath = null;
            selectedFolderPath = null;
        }

        private void OnGUI()
        {
            GUILayout.Label("VRoid MToon材质自动配置工具", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 显示当前选中的FBX文件
            GUILayout.Label("当前选中的FBX文件:");
            if (!string.IsNullOrEmpty(selectedFbxPath))
            {
                EditorGUILayout.TextField(selectedFbxPath, EditorStyles.label);

                // 骨骼和材质路径处理部分
                GUILayout.Space(15);
                GUILayout.Label("模型预处理:", EditorStyles.boldLabel);

                if (GUILayout.Button("调整骨骼绑定并生成外部材质", GUILayout.Height(30)))
                {
                    logMessages.Clear();
                    ProcessModelSetup();
                }

                // 显示文件夹结构验证结果
                GUILayout.BeginHorizontal();
                if (IsValidTargetFolder(selectedFolderPath))
                {
                    EditorGUILayout.LabelField("✅ 已找到Materials文件夹", EditorStyles.label);
                }
                else
                {
                    EditorGUILayout.LabelField("❌ 尚未生成Materials文件夹，请先执行模型预处理", EditorStyles.boldLabel);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("请在Project窗口中选中一个FBX模型文件", MessageType.Info);
                EditorGUILayout.LabelField("提示：选中FBX文件后可右键选择 'VRoidTools/配置MToon材质' 快速启动", EditorStyles.miniLabel);
            }

            GUILayout.Space(15);

            // 贴图子文件夹路径配置
            GUILayout.Label("贴图子文件夹名称:");
            GUILayout.BeginHorizontal();
            textureFolderName = EditorGUILayout.TextField(textureFolderName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("baked_files", GUILayout.Width(100))) textureFolderName = "baked_files";
            if (GUILayout.Button("atlas_files", GUILayout.Width(100))) textureFolderName = "atlas_files";
            GUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("设置存放贴图的子文件夹名称（默认：baked_files）", MessageType.Info);

            GUILayout.Space(15);

            // 新增选项设置
            GUILayout.Label("材质处理选项:", EditorStyles.boldLabel);

            setEyeMaterialsToTransparent = EditorGUILayout.Toggle(
                "将含'eye'的材质设置为透明", setEyeMaterialsToTransparent);

            // 修改剔除模式选择方式，使用下拉列表选择整数数值
            int selectedCullModeIndex = System.Array.IndexOf(cullModeValues, cullMode);
            if (selectedCullModeIndex == -1) selectedCullModeIndex = 0;

            selectedCullModeIndex = EditorGUILayout.Popup(
                "设置材质剔除模式", selectedCullModeIndex, cullModeOptions);
            cullMode = cullModeValues[selectedCullModeIndex];
            // 新增描边选项
            addOutlineToOpaqueMaterials = EditorGUILayout.Toggle(
                "为非透明材质添加描边", addOutlineToOpaqueMaterials);

            if (addOutlineToOpaqueMaterials)
            {
                EditorGUI.indentLevel++;
                outlineWidth = EditorGUILayout.Slider(
                    "描边宽度", outlineWidth, 0.001f, 0.1f); // 范围0.001-0.1
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(15);

            // MToon Shader状态检查
            GUILayout.Label("MToon Shader状态:");
            if (mtoonShader != null)
            {
                EditorGUILayout.LabelField("✅ 已找到: " + mtoonShader.name, EditorStyles.label);
            }
            else
            {
                EditorGUILayout.LabelField("❌ 未找到VRM MToon Shader！请导入VRM SDK。", EditorStyles.boldLabel);
            }

            GUILayout.Space(20);

            // 执行按钮
            bool isReady = mtoonShader != null
                         && !string.IsNullOrEmpty(selectedFolderPath)
                         && IsValidTargetFolder(selectedFolderPath)
                         && !string.IsNullOrEmpty(textureFolderName);

            GUI.enabled = isReady;
            if (GUILayout.Button("开始配置材质", GUILayout.Height(35)))
            {
                logMessages.Clear();
                if (ProcessMaterials())
                {
                    EditorUtility.DisplayDialog("完成", "材质配置已全部处理完毕", "确定");
                }
            }
            GUI.enabled = true;

            // 日志显示区域
            GUILayout.Space(15);
            GUILayout.Label("处理日志:", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(250), GUILayout.ExpandWidth(true));
            foreach (var msg in logMessages)
            {
                var style = msg.StartsWith("[错误]") ? EditorStyles.boldLabel :
                           msg.StartsWith("[警告]") ? EditorStyles.label : EditorStyles.label;
                style.normal.textColor = msg.StartsWith("[错误]") ? Color.red :
                                       msg.StartsWith("[警告]") ? Color.yellow : Color.white;
                EditorGUILayout.LabelField(msg, style);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool IsValidTargetFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            return Directory.Exists(Path.Combine(folderPath, "Materials"));
        }

        private void LoadMToonShader()
        {
            mtoonShader = Shader.Find("VRM/MToon");
        }

        /// <summary>
        /// 处理模型设置：调整骨骼绑定并生成外部材质
        /// </summary>
        private void ProcessModelSetup()
        {
            if (string.IsNullOrEmpty(selectedFbxPath))
            {
                logMessages.Add("[错误] 未选中有效的FBX文件");
                return;
            }

            // 导入设置修改需要使用ModelImporter
            ModelImporter importer = AssetImporter.GetAtPath(selectedFbxPath) as ModelImporter;
            if (importer == null)
            {
                logMessages.Add("[错误] 无法获取模型导入器");
                return;
            }

            try
            {
                // 保存原始设置
                ModelImporterAnimationType originalAnimType = importer.animationType;
                ModelImporterMaterialLocation originalMaterialLocation = importer.materialLocation; // 保存原始材质位置
                ModelImporterMaterialName originalMaterialNameMode = importer.materialName;
                ModelImporterMaterialSearch originalMaterialSearchMode = importer.materialSearch;

                // 设置骨骼绑定为类人骨骼
                importer.animationType = ModelImporterAnimationType.Human;
                logMessages.Add("[信息] 已将骨骼绑定设置为类人骨骼");

                // 重新导入以应用骨骼设置
                AssetDatabase.ImportAsset(selectedFbxPath, ImportAssetOptions.ForceUpdate);

                // 处理Avatar中的下巴骨骼映射
                //RemoveJawBoneFromAvatar(importer);

                // 设置使用外部材质（关键修复：使用materialLocation属性）
                string materialsPath = Path.Combine(selectedFolderPath, "Materials");
                // if (!Directory.Exists(materialsPath))
                // {
                //     //Directory.CreateDirectory(materialsPath);
                //     logMessages.Add($"[信息] 已创建Materials文件夹: {materialsPath}");
                // }

                // 核心修复：设置材质位置为外部
                importer.materialLocation = ModelImporterMaterialLocation.External;
                // 设置材质命名规则
                // importer.materialName = ModelImporterMaterialName.BasedOnModelNameAndMaterialName;
                // 设置材质搜索路径为本地（Materials文件夹）
                //importer.materialSearch = ModelImporterMaterialSearch.Local;

                // 重新导入资源以应用材质设置
                AssetDatabase.ImportAsset(selectedFbxPath, ImportAssetOptions.ForceUpdate);
                logMessages.Add("[信息] 已应用外部材质设置，材质已生成到Materials文件夹");

                logMessages.Add("[成功] 模型预处理完成");
                // 新增：保存导入器设置
                importer.SaveAndReimport();
            }
            catch (System.Exception ex)
            {
                logMessages.Add($"[错误] 处理模型时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从Avatar映射中移除下巴骨骼关联
        /// </summary>
        private void RemoveJawBoneFromAvatar(ModelImporter importer)
        {
            // 重新实现下巴骨骼处理（修复报错问题）
            if (importer.animationType == ModelImporterAnimationType.Human)
            {
                HumanDescription humanDesc = importer.humanDescription;
                var jawBone = humanDesc.human.FirstOrDefault(hb => hb.humanName == "Jaw");

                if (!string.IsNullOrEmpty(jawBone.boneName))
                {
                    // 创建新的HumanBone数组（避免修改原数组引用）
                    var newHumanBones = humanDesc.human.ToList();
                    int jawIndex = newHumanBones.FindIndex(hb => hb.humanName == "Jaw");
                    newHumanBones[jawIndex] = new HumanBone { humanName = "Jaw", boneName = "" };

                    humanDesc.human = newHumanBones.ToArray();
                    importer.humanDescription = humanDesc;

                    AssetDatabase.ImportAsset(selectedFbxPath, ImportAssetOptions.ForceUpdate);
                    logMessages.Add("[信息] 已将下巴骨骼映射设置为None");
                }
            }
        }

        private bool ProcessMaterials()
        {
            string textureFolderPath = Path.Combine(selectedFolderPath, textureFolderName);
            if (!Directory.Exists(textureFolderPath))
            {
                logMessages.Add($"[错误] 未找到贴图文件夹: {textureFolderPath}");
                return false;
            }

            string materialsPath = Path.Combine(selectedFolderPath, "Materials");
            string[] materialPaths = Directory.GetFiles(materialsPath, "*.mat", SearchOption.TopDirectoryOnly);
            if (materialPaths.Length == 0)
            {
                logMessages.Add("[警告] Materials文件夹中未找到任何材质文件");
                return false;
            }

            logMessages.Add($"开始处理 {materialPaths.Length} 个材质，贴图文件夹: {textureFolderName}");
            int successCount = 0;
            int failCount = 0;

            foreach (var matPath in materialPaths)
            {
                string matName = Path.GetFileNameWithoutExtension(matPath);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (material == null)
                {
                    logMessages.Add($"[错误] 无法加载材质: {matName}");
                    failCount++;
                    continue;
                }

                // 切换Shader
                bool shaderSuccess = SetMaterialShader(material, matName);
                if (!shaderSuccess)
                {
                    failCount++;
                    continue;
                }

                // 初始化基础颜色
                InitializeBaseColors(material, matName);

                // 绑定贴图
                bool lightSuccess = BindTextureToMaterial(material, matName, textureFolderPath, TextureType.Light);
                bool darkSuccess = BindTextureToMaterial(material, matName, textureFolderPath, TextureType.Dark);

                // 设置剔除模式
                material.SetInt("_CullMode", cullMode);
                logMessages.Add($"[信息] 材质 {matName} 剔除模式已设置为 {cullMode} ({(cullMode == 0 ? "Off" : "Back")})");

                // 处理眼睛材质透明度（支持大小写）
                if (setEyeMaterialsToTransparent && (matName.ToLower().Contains("eye") || matName.ToLower().Contains("nose")))
                {
                    // MToon透明模式核心配置
                    material.SetInt("_BlendMode", 2); // 明确设置为透明混合模式
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0); // 关闭深度写入
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = 3000;
                    material.SetInt("_AlphaToMask", 0); // 禁用Alpha遮罩

                    logMessages.Add($"[信息] 材质 {matName} 已设置为透明模式（包含眼/鼻）");
                }
                // 处理描边（非透明材质）
                if (addOutlineToOpaqueMaterials)
                {
                    // 判断是否为非透明材质（BlendMode不为2）
                    int currentBlendMode = material.HasProperty("_BlendMode") ? material.GetInt("_BlendMode") : 0;
                    if (currentBlendMode != 2) // 非透明
                    {
                        material.SetFloat("_OutlineWidth", outlineWidth); // 设置宽度
                        material.SetColor("_OutlineColor", Color.black); // 可自定义颜色，这里默认黑色
                        material.SetInt("_OutlineWidthMode", 1); // 固定宽度模式
                        logMessages.Add($"[信息] 材质 {matName} 已添加描边（宽度：{outlineWidth}）");
                    }
                }
                // 统计结果
                if (lightSuccess && darkSuccess)
                {
                    successCount++;
                    logMessages.Add($"[成功] 材质 {matName} 配置完成");
                }
                else
                {
                    failCount++;
                    logMessages.Add($"[警告] 材质 {matName} 部分贴图配置失败");
                }
            }

            logMessages.Add("");
            logMessages.Add($"处理完成 - 成功: {successCount} 个，失败: {failCount} 个");
            return true;
        }

        private bool SetMaterialShader(Material material, string matName)
        {
            if (material.shader == mtoonShader)
            {
                logMessages.Add($"[信息] 材质 {matName} 已使用MToon Shader");
                return true;
            }

            material.shader = mtoonShader;
            if (material.shader != mtoonShader)
            {
                logMessages.Add($"[错误] 材质 {matName} 切换Shader失败");
                return false;
            }

            logMessages.Add($"[信息] 材质 {matName} 已切换为MToon Shader");
            return true;
        }

        private void InitializeBaseColors(Material material, string matName)
        {
            // 初始化亮部颜色为白色
            if (material.HasProperty(LitColorProperty) && material.GetColor(LitColorProperty) != Color.white)
            {
                material.SetColor(LitColorProperty, Color.white);
                logMessages.Add($"[信息] 材质 {matName} 已初始化亮部颜色为白色");
            }
            else if (!material.HasProperty(LitColorProperty))
            {
                logMessages.Add($"[警告] 材质 {matName} 没有 {LitColorProperty} 属性");
            }

            // 初始化暗部颜色为深灰色
            Color defaultShadeColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            if (material.HasProperty(ShadeColorProperty) && material.GetColor(ShadeColorProperty) != defaultShadeColor)
            {
                material.SetColor(ShadeColorProperty, defaultShadeColor);
                logMessages.Add($"[信息] 材质 {matName} 已初始化暗部颜色为深灰色");
            }
            else if (!material.HasProperty(ShadeColorProperty))
            {
                logMessages.Add($"[警告] 材质 {matName} 没有 {ShadeColorProperty} 属性");
            }
        }

        private bool BindTextureToMaterial(Material material, string matName, string textureFolderPath, TextureType texType)
        {
            string propertyName = texType == TextureType.Light ? LitTextureProperty : ShadeTextureProperty;
            string texSuffix = texType == TextureType.Light ? " light" : " dark";
            string texTypeName = texType == TextureType.Light ? "亮部" : "暗部";

            // 检查材质是否有该属性
            if (!material.HasProperty(propertyName))
            {
                logMessages.Add($"[错误] 材质 {matName} 没有 {propertyName} 属性，无法绑定{texTypeName}贴图");
                return false;
            }

            string targetTexName = matName + texSuffix;
            Texture2D texture = FindTextureInFolder(textureFolderPath, targetTexName);

            // 尝试不带空格的版本（有些导出可能没有空格）
            if (texture == null)
            {
                targetTexName = matName + texSuffix.Replace(" ", "");
                texture = FindTextureInFolder(textureFolderPath, targetTexName);
            }

            if (texture == null)
            {
                logMessages.Add($"[错误] 材质 {matName} 未找到{texTypeName}贴图: {targetTexName}");
                return false;
            }

            material.SetTexture(propertyName, texture);
            logMessages.Add($"[信息] 材质 {matName} 的{texTypeName}贴图已绑定到 {propertyName}");
            return true;
        }

        private Texture2D FindTextureInFolder(string folderPath, string texName)
        {
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd" };
            foreach (var ext in imageExtensions)
            {
                string texPath = Path.Combine(folderPath, texName + ext).Replace("\\", "/");
                if (File.Exists(texPath))
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex != null) return tex;
                }
            }
            return null;
        }
    }
}
