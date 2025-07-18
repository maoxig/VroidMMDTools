
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VMD2Anim
{
    public class VMDConverter : EditorWindow
    {
        #region 配置参数
        public bool QuickConvertMode = true;

        public bool QuickLoadAnim = true; // 是否快速加载动画
        private static ConversionSettings settings;
        private bool isConverting = false;
        private float progress = 0f;
        private string progressMessage = "";


        #endregion

        #region 窗口初始化
        [MenuItem("MMD for Unity/VMD To Anim Converter")]
        public static void ShowWindow()
        {
            GetWindow<VMDConverter>("VMD To Anim Converter");
        }

        private void OnEnable()
        {
            settings = ConversionSettings.Load();
        }
        #endregion

        #region 界面绘制
        private void OnGUI()
        {
            GUILayout.Label("VMD动画转换工具", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(isConverting);

            DrawFilePathField("VMD文件:", ref settings.VmdFilePath, FileType.VMD);
            DrawModelPathField();
            DrawFilePathField("输出路径:", ref settings.OutputPath, FileType.Directory);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("PMX2FBX工具:", GUILayout.Width(100));
            settings.PMX2FBXPath = EditorGUILayout.TextField(settings.PMX2FBXPath);

            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFilePanel("选择PMX2FBX工具", "", "exe");
                if (!string.IsNullOrEmpty(path))
                    settings.PMX2FBXPath = path;
            }
            // 提示用户需要添加PMX2FBX工具，默认是选中了MMD4Mecanim的PMX2FBX工具，不过也可以指定已有的PMX2FBX工具路径
            if (string.IsNullOrEmpty(settings.PMX2FBXPath) || !File.Exists(settings.PMX2FBXPath))
            {
                EditorGUILayout.HelpBox("请指定PMX2FBX工具路径，默认是MMD4Mecanim的PMX2FBX工具，如果没有，请安装然后指定", MessageType.Warning);
            }

            EditorGUILayout.EndHorizontal();

            settings.OverwriteExisting = EditorGUILayout.Toggle("覆盖已存在文件", settings.OverwriteExisting);

            if (GUILayout.Button("保存配置", GUILayout.Height(20)))
            {
                settings.Save();
                EditorUtility.DisplayDialog("提示", "配置已保存", "确定");
            }

            GUILayout.Space(20);

            if (GUILayout.Button("开始转换", GUILayout.Height(30)))
            {
                ConvertVMDToAnim();
            }

            EditorGUI.EndDisabledGroup();

            if (isConverting)
            {
                EditorGUILayout.LabelField("转换进度:");
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, progressMessage);
                Repaint();
            }

            GUILayout.Space(20);
            EditorGUILayout.LabelField("提示: 确保PMX和VMD文件路径无特殊字符，且PMX2FBX工具正常。");
        }


        private void DrawFilePathField(string label, ref string path, FileType fileType)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            path = EditorGUILayout.TextField(path);

            string buttonText = fileType == FileType.Directory ? "浏览" : "选择";
            string fileExtension = fileType == FileType.VMD ? "vmd" : "pmx";
            string dialogTitle = fileType == FileType.VMD ? "选择VMD文件" : "选择PMX文件";

            if (GUILayout.Button(buttonText, GUILayout.Width(60)))
            {
                if (fileType == FileType.Directory)
                {
                    string selectedPath = EditorUtility.OpenFolderPanel(dialogTitle, "", "");
                    if (!string.IsNullOrEmpty(selectedPath))
                        path = selectedPath + "/";
                }
                else
                {
                    string selectedPath = EditorUtility.OpenFilePanel(dialogTitle, "", fileExtension);
                    if (!string.IsNullOrEmpty(selectedPath))
                        path = selectedPath;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModelPathField()
        {
            settings.UseDefaultPMX = EditorGUILayout.Toggle("使用默认PMX模型", settings.UseDefaultPMX);
            if (!settings.UseDefaultPMX)
            {
                DrawFilePathField("PMX文件:", ref settings.PmxFilePath, FileType.PMX);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("默认PMX路径:", GUILayout.Width(100));
                settings.DefaultPmxPath = EditorGUILayout.TextField(settings.DefaultPmxPath);
                EditorGUILayout.EndHorizontal();
            }
        }
        #endregion

        #region 核心转换逻辑
        // 主窗口转换方法
        private void ConvertVMDToAnim()
        {
            // 调用公共转换方法
            ConvertVMDInternal(
                settings.VmdFilePath,
                settings.UseDefaultPMX ? settings.DefaultPmxPath : settings.PmxFilePath,
                settings.OutputPath,
                (p, msg) => { progress = p; progressMessage = msg; Repaint(); },
                settings.OverwriteExisting,
                QuickConvertMode,
                QuickLoadAnim
            );
        }

        // 外部API转换方法
        public static bool ConvertVMD(
            string vmdPath,
            string pmxPath = null,
            string outputPath = null,
            Action<float, string> progressCallback = null,
            bool? overwrite = null
            )
        {
            var settings = ConversionSettings.Load();
            string actualPmxPath = pmxPath ?? (settings.UseDefaultPMX ? settings.DefaultPmxPath : settings.PmxFilePath);
            string actualOutputPath = outputPath ?? settings.OutputPath;
            bool actualOverwrite = overwrite ?? settings.OverwriteExisting;

            // 调用公共转换方法
            return ConvertVMDInternal(
                vmdPath,
                actualPmxPath,
                actualOutputPath,
                progressCallback,
                actualOverwrite,
                quickMode: true, // 外部调用默认快速模式
                true
            );
        }

        private static bool ConvertVMDInternal(
           string vmdPath,
           string pmxPath,
           string outputPath,
           Action<float, string> progressCallback,
           bool overwriteExisting,
           bool quickMode,
           bool quickLoadAnim)
        {
            // 验证参数
            //UnityEngine.Debug.Log($"[VMD转换] 开始转换: {vmdPath} -> {pmxPath} -> {outputPath}");
            if (!ValidateConversionParams(vmdPath, pmxPath, ConversionSettings.Load().PMX2FBXPath))
            {
                progressCallback?.Invoke(0f, "参数验证失败，转换中止。");
                return false;
            }

            progressCallback?.Invoke(20f, "准备转换...");

            // 核心路径变量（完全使用项目内路径）
            string vmdFileName = Path.GetFileNameWithoutExtension(vmdPath);
            string pmxDir = Path.GetDirectoryName(pmxPath); // PMX所在目录（项目内）
            string fbxPath = Path.Combine(pmxDir, $"{vmdFileName}.fbx"); // FBX直接生成在PMX同目录
            string finalAnimPath = Path.Combine(outputPath, $"{vmdFileName}.anim");

            try
            {
                // 检查覆盖
                if (File.Exists(finalAnimPath) && !overwriteExisting)
                {
                    progressCallback?.Invoke(0f, "目标文件已存在且未允许覆盖，转换中止。");
                    return false;
                }
                //UnityEngine.Debug.Log($"[VMD转换] 开始转换: {vmdPath} -> {fbxPath}");

                // 步骤1：生成FBX
                progressCallback?.Invoke(0.3f, "生成FBX文件...");
                string generatedFbxPath = RunPMX2FBX(
                    ConversionSettings.Load().PMX2FBXPath,
                    pmxPath,
                    vmdPath,
                    pmxDir, // 工作目录为PMX所在目录
                    progressCallback,
                    quickMode
                );
                fbxPath = generatedFbxPath; // 确认实际生成的FBX路径
      
                UnityEngine.Debug.Log($"[VMD转换] FBX生成成功: {fbxPath}");

                // 步骤2：刷新AssetDatabase以识别新生成的FBX
                progressCallback?.Invoke(0.5f, "导入FBX并提取动画...");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                // 转换回Unity路径格式
                fbxPath = MakeRelativePath(generatedFbxPath);

                // 步骤3：设置FBX导入为Humanoid（直接使用项目内FBX路径）
                EnsureFBXImportSettings(fbxPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // 步骤4：提取动画（直接从项目内FBX提取）
                AnimationClip extractedClip = ExtractHumanoidAnimation(fbxPath, vmdFileName, quickLoadAnim);
                if (extractedClip == null)
                    throw new Exception("未从FBX中找到有效的Humanoid动画");

                // 步骤5：保存动画
                progressCallback?.Invoke(0.9f, "保存动画文件...");
                string animDir = Path.GetDirectoryName(finalAnimPath);
                if (!Directory.Exists(animDir))
                {
                    Directory.CreateDirectory(animDir);
                }
                string relativeAnimPath = MakeRelativePath(finalAnimPath);
                if (!relativeAnimPath.StartsWith("Assets/"))
                    throw new Exception($"无效的资产路径: {relativeAnimPath}");

                AssetDatabase.CreateAsset(extractedClip, relativeAnimPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 步骤6：清理生成的FBX文件（避免项目污染）
                CleanupGeneratedFbx(fbxPath);

                progressCallback?.Invoke(1.0f, "转换完成!");
                return true;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke(0f, $"转换失败: {ex.Message}");
                UnityEngine.Debug.LogError($"[VMD转换] 失败: {ex.Message}");
                // 异常时也清理FBX
                CleanupGeneratedFbx(fbxPath);
                return false;
            }
        }

        /// <summary>
        /// 快速提取Humanoid动画（参考Vmd2AnimUtils的做法）
        /// </summary>
        /// <summary>
        /// 提取动画剪辑（简化路径依赖）
        /// </summary>
        private static AnimationClip ExtractHumanoidAnimation(string fbxAssetPath, string vmdFileName, bool quickLoadAnim)
        {
            // 强制验证FBX路径在Assets内
            if (!fbxAssetPath.StartsWith("Assets/"))
                throw new Exception($"FBX路径必须在Assets目录内: {fbxAssetPath}");

            AnimationClip sourceClip = null;

            // 快速模式：加载FBX所有资源，通过名称快速筛选目标动画
            if (quickLoadAnim && !string.IsNullOrEmpty(vmdFileName))
            {
                // 构建预期的动画剪辑名称（根据你的命名规则：vmdFileName_vmd）
                string expectedAnimName = $"{vmdFileName}_vmd";

                // 加载FBX所有子资源
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                if (assets == null || assets.Length == 0)
                    throw new Exception($"FBX无资产: {fbxAssetPath}（检查FBX是否有效）");

                // 通过名称快速筛选目标动画
                sourceClip = assets.OfType<AnimationClip>()
                                   .FirstOrDefault(clip => clip.name == expectedAnimName);

                if (sourceClip != null)
                {
                    UnityEngine.Debug.Log($"[VMD转换] 快速筛选动画成功: {expectedAnimName}");
                    return CloneAnimationClip(sourceClip);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[VMD转换] 未找到预期名称的动画: {expectedAnimName}，尝试常规方式...");
                }
            }

            // 常规模式：加载整个FBX并从中提取第一个动画
            sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(fbxAssetPath);
            if (sourceClip == null)
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                if (assets == null || assets.Length == 0)
                    throw new Exception($"FBX无资产: {fbxAssetPath}");

                sourceClip = assets.OfType<AnimationClip>().FirstOrDefault();
                if (sourceClip == null)
                    throw new Exception($"FBX中无AnimationClip");
            }

            return CloneAnimationClip(sourceClip);
        }
        // 独立的动画克隆方法，提高代码可读性

        private static AnimationClip CloneAnimationClip(AnimationClip sourceClip)
        {
            AnimationClip newClip = new AnimationClip();
            newClip.name = sourceClip.name;
            newClip.frameRate = sourceClip.frameRate;
            newClip.legacy = false;

            // 设置动画曲线
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
            foreach (var binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                if (curve != null)
                    newClip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
            }

            // 设置动画属性
            AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(newClip);
            clipSettings.loopTime = false;
            clipSettings.loopBlendOrientation = true;
            clipSettings.loopBlendPositionY = true;
            clipSettings.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(newClip, clipSettings);

            return newClip;
        }
        private static void EnsureFBXImportSettings(string unityFbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(unityFbxPath) as ModelImporter;
            if (importer == null)
                throw new Exception($"无法获取FBX导入器: {unityFbxPath}");

            // 强制设置为Humanoid类型（参考AutoChangeImporter_Vmd2Anim）
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importAnimation = true;
                // 优化压缩
                importer.animationCompression = ModelImporterAnimationCompression.Optimal;
                importer.SaveAndReimport();
            }
        }

        #endregion

        #region 辅助方法
        // 验证参数（保持不变）
        private static bool ValidateConversionParams(string vmdPath, string pmxPath, string pmx2FbxPath)
        {
            if (string.IsNullOrEmpty(vmdPath) || !File.Exists(vmdPath))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的VMD文件", "确定");
                return false;
            }
            if (string.IsNullOrEmpty(pmxPath) || !File.Exists(pmxPath))
            {
                EditorUtility.DisplayDialog("错误", "PMX文件不存在，请检查路径", "确定");
                return false;
            }
            if (string.IsNullOrEmpty(pmx2FbxPath) || !File.Exists(pmx2FbxPath))
            {
                EditorUtility.DisplayDialog("错误", "PMX2FBX工具不存在，请检查路径", "确定");
                return false;
            }
            return true;
        }

        // 执行PMX2FBX工具
        private static string RunPMX2FBX(string toolPath, string pmxPath, string vmdPath, string workDir,
                    Action<float, string> progressCallback = null, bool quickMode = true)
        {
            progressCallback?.Invoke(0.25f, "执行PMX2FBX工具...");

            string toolDirectory = Path.GetDirectoryName(toolPath);
            string defaultConfigPath = Path.Combine(toolDirectory, "pmx2fbx.xml");
            string backupConfigPath = Path.Combine(toolDirectory, "pmx2fbx.xml.bak");
            string tempConfigPath = Path.Combine(workDir, "pmx2fbx_temp.xml"); // 临时配置放在PMX目录
            bool configBackedUp = false;

            // 这里需要把路径都改为绝对路径
            pmxPath = Path.GetFullPath(pmxPath);
            vmdPath = Path.GetFullPath(vmdPath);
            try
            {
                // 仅快速模式生成临时配置
                if (quickMode)
                    GenerateQuickConvertConfig(tempConfigPath, quickMode);

                // 备份原始配置（必要操作）
                if (File.Exists(defaultConfigPath))
                {
                    File.Copy(defaultConfigPath, backupConfigPath, true);
                    configBackedUp = true;
                }

                // 应用临时配置（仅快速模式）
                if (quickMode && File.Exists(tempConfigPath))
                    File.Copy(tempConfigPath, defaultConfigPath, true);

                // 执行PMX2FBX，直接在项目内生成FBX
                string arguments = $"\"{pmxPath}\" \"{vmdPath}\"";
                UnityEngine.Debug.Log($"[PMX2FBX] 执行命令: {toolPath} {arguments}");
                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = toolPath,
                        Arguments = arguments,
                        WorkingDirectory = workDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    bool completed = process.WaitForExit(180000); // 180000ms = 180秒
                    if (!completed)
                    {
                        process.Kill(); // 超时则强制终止进程
                        throw new Exception("PMX2FBX工具执行超时（3分钟），可能是文件错误或工具无响应");
                    }

                    if (!string.IsNullOrEmpty(error))
                        UnityEngine.Debug.LogError($"[PMX2FBX错误]\n{error}");
                    if (process.ExitCode != 0)
                        throw new Exception($"PMX2FBX执行失败，退出码: {process.ExitCode}\n{error}");





                    string fbxPath = Path.ChangeExtension(pmxPath, ".fbx");
                    if (!File.Exists(fbxPath))
                        throw new Exception($"未找到生成的FBX文件: {fbxPath}");

                    progressCallback?.Invoke(0.5f, "FBX生成成功");

                    return fbxPath;
                }
            }
            finally
            {
                // 恢复原始配置（必要操作）
                if (configBackedUp && File.Exists(backupConfigPath))
                {
                    File.Copy(backupConfigPath, defaultConfigPath, true);
                    File.Delete(backupConfigPath);
                }
                // 清理临时配置文件
                if (File.Exists(tempConfigPath))
                    File.Delete(tempConfigPath);
            }
        }

        // 清理生成的FBX文件（转换完成后移除，避免项目污染）
        private static void CleanupGeneratedFbx(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath)) return;

            // 删除FBX文件及其meta文件
            if (File.Exists(fbxPath))
            {
                AssetDatabase.DeleteAsset(fbxPath);
                UnityEngine.Debug.Log($"[VMD转换] 清理临时FBX: {fbxPath}");
            }
            string metaPath = $"{fbxPath}.meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
        private static void GenerateQuickConvertConfig(string configPath, bool quickMode)
        {
            if (!quickMode) return;

            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PMX2FBXConfig  xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <globalSettings>
        <editorAdvancedMode>true</editorAdvancedMode>
        <blendShapesFlag>0</blendShapesFlag>
        <boneRenameFlag>0</boneRenameFlag>
        <prefixBoneRenameFlag>0</prefixBoneRenameFlag>
        <prefixBoneNoNameFlag>0</prefixBoneNoNameFlag>
        <prefixNullBoneNameFlag>0</prefixNullBoneNameFlag>
        <morphRenameFlag>0</morphRenameFlag>
        <prefixMorphNoNameFlag>0</prefixMorphNoNameFlag>
        <materialRenameFlag>0</materialRenameFlag>
        <prefixMaterialNoNameFlag>0</prefixMaterialNoNameFlag>
        <escapeMaterialNameFlag>0</escapeMaterialNameFlag>
        <splitMeshFlag>0</splitMeshFlag>
        <animKeyReductionFlag>1</animKeyReductionFlag>
        <animNullAnimationFlag>0</animNullAnimationFlag>
        <animRootTransformFlag>0</animRootTransformFlag>
        <animKeyRotationEpsilon1>0.02</animKeyRotationEpsilon1>
        <animKeyRotationEpsilon2>0.03</animKeyRotationEpsilon2>
        <animKeyTranslationEpsilon1>0.002</animKeyTranslationEpsilon1>
        <animKeyTranslationEpsilon2>0.003</animKeyTranslationEpsilon2>
        <animAwakeWaitingTime>3</animAwakeWaitingTime>
        <enableFBXTexture>0</enableFBXTexture>
    </globalSettings>
    <bulletPhysics>
        <enabled>0</enabled>
    </bulletPhysics>
    <renameList />
  <splitMeshBoneList />
  <edgeStretchList />
  <freezeRigidBodyList />
  <freezeMotionList />
</PMX2FBXConfig>";
            File.WriteAllText(configPath, xml);
            UnityEngine.Debug.Log($"[VMD转换] 生成快速模式配置文件: {configPath}");
        }

        /// <summary>
        /// 修复路径转换，确保生成正确的Assets相对路径
        /// </summary>
        private static string MakeRelativePath(string absolutePath)
        {
            try
            {
                string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
                absolutePath = absolutePath.Replace('\\', '/');

                // 强制路径以项目根目录为基准
                if (!absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // 若路径不在项目内，尝试直接拼接Assets（处理用户输出路径在Assets外的情况）
                    if (absolutePath.Contains("Assets/"))
                        return absolutePath.Substring(absolutePath.IndexOf("Assets/"));
                    else
                        throw new Exception($"输出路径必须在项目内: {absolutePath}");
                }

                // 生成相对路径
                string relativePath = absolutePath.Substring(projectRoot.Length).TrimStart('/');
                if (!relativePath.StartsWith("Assets/"))
                    relativePath = $"Assets/{relativePath}";

                return relativePath;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"路径转换失败: {ex.Message}，原始路径: {absolutePath}");
                throw; // 直接抛出，避免使用无效路径
            }
        }


        #endregion

    }

    [Serializable]
    public class ConversionSettings : ScriptableObject
    {
        public string VmdFilePath = "";
        public string PmxFilePath = "";
        public string DefaultPmxPath = "Assets/AnimConverter/Model/Tda_miku.pmx";
        public string OutputPath = "Assets/AnimConverter/Output/";
        public string PMX2FBXPath = "Assets/MMD4Mecanim/Editor/PMX2FBX/pmx2fbx.exe";
        public bool UseDefaultPMX = true;
        public bool OverwriteExisting = false;
        

        private const string SETTINGS_PATH = "Assets/VMD2AnimSettings.asset";

        public static ConversionSettings Load()
        {
            ConversionSettings settings = AssetDatabase.LoadAssetAtPath<ConversionSettings>(SETTINGS_PATH);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ConversionSettings>();
                AssetDatabase.CreateAsset(settings, SETTINGS_PATH);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        public void Save()
        {
            ConversionSettings settings = AssetDatabase.LoadAssetAtPath<ConversionSettings>(SETTINGS_PATH);
            if (settings == null)
            {
                AssetDatabase.CreateAsset(this, SETTINGS_PATH);
            }
            else
            {
                EditorUtility.CopySerialized(this, settings);
            }
            AssetDatabase.SaveAssets();
        }
    }

    public enum FileType
    {
        VMD,
        PMX,
        Directory
    }
}
