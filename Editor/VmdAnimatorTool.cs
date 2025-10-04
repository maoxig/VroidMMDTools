using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;
using VMDPaser;
using VroidMMDTools.Utils;
// CancellationTokenSource
using System.Threading;
// Task
using System.Threading.Tasks;
using UnityEngine.Playables;

namespace VroidMMDTools
{
    public class VmdMorphAnimatorTool : EditorWindow
    {
        private const string DefaultOutputPath = "Assets/AnimConverter/Output/";
        private const string TempBuildFolder = "Assets/Temp/TempPureBuild/";
        private const float DefaultFrameRate = 30f;

        // 核心组件引用
        private AnimationClip sourceClip;
        private GameObject targetModel;
        private SkinnedMeshRenderer bodyRenderer;

        // VMD文件相关（按功能拆分）
        // 1. 动画VMD
        private string animVmdFilePath;
        private VMD parsedAnimVmd;
        private bool animVmdParsed = false;

        private bool isConverting = false;
        private float progress = 0f;
        private string progressMessage = "";
        // 超时时间
        private int timeoutSeconds = 240;

        // 2. 镜头VMD（支持多个）
        private List<string> cameraVmdFilePaths = new List<string>();
        private List<VMD> parsedCameraVmds = new List<VMD>();
        private List<VMDCameraFrame> vmdCameraFrames = new List<VMDCameraFrame>();
        private bool cameraVmdParsed = false;

        private float cameraScale = 1.0f; // 相机缩放比例

        // 3. 表情VMD（支持多个）
        private List<string> morphVmdFilePaths = new List<string>();
        private List<VMD> parsedMorphVmds = new List<VMD>();
        private List<VMDMorphFrame> vmdMorphFrames = new List<VMDMorphFrame>();
        private bool morphVmdParsed = false;

        // 配置选项
        private string outputPath = DefaultOutputPath;
        private string newClipName = "NewMorphAnimation";
        private string controllerName = "NewAnimatorController";

        // 形态键管理
        private List<string> availableMorphs = new List<string>();
        private Dictionary<string, bool> selectedMorphs = new Dictionary<string, bool>();
        private Dictionary<string, string> morphMapping = new Dictionary<string, string>(); // 形态键映射表

        // VRM标准形态键映射表
        private readonly Dictionary<string, string> vrmBlendShapeMapping = new Dictionary<string, string>
        {
            { "まばたき", "Blink" },
            { "にこり", "Smile" },
            { "悲しい", "Sorrow" },
            { "驚き", "Angry" },
            { "あ", "A" },
            { "い", "I" },
            { "う", "U" },
            { "え", "E" },
            { "お", "O" },
            { "ウィンク", "Wink" },
            { "のび", "Joy" },
            { "びっくり", "Surprised" }
        };

        // 直接映射模式配置
        private bool directMappingMode = true;
        private string defaultSkinnedMeshPath = "Body";
        private string defaultSkinnedMeshName = "Body";
        private bool showSkinnedMeshOptions = false;

        // 相机动画配置
        private bool showCameraAdvancedOptions = false;
        private bool enableCameraAnimation = false;

        // 相机路径配置
        private string cameraRootPath = "Camera_root";  // 位移接受组件
        private string cameraComponentPath = "Camera_root/Camera_root_1/Camera";// 主相机组件路径
        private string cameraDistancePath = "Camera_root/Camera_root_1"; // Distance变换路径（接收距离动画）

        // 音频配置
        private string audioFilePath = "";

        // 打包配置
        private bool addMorphCurves = true; // 添加表情曲线
        private bool addCameraCurves = false; // 添加镜头曲线

        private string bundleBaseName = "character_animation";
        private BuildAssetBundleOptions bundleOptions = BuildAssetBundleOptions.ChunkBasedCompression;
        private bool showBundleOptions = false;
        private string bundleOutputPath = "";

        // 界面状态
        private Vector2 mainScrollPos;
        private Vector2 allMorphsScrollPos;
        private bool showMappingOptions = false;

        // 用于Timeline预览
        private bool showTimelinePreview = false;
        private GameObject characterModel;


        // 4. 在编辑器窗口中添加取消支持
        private CancellationTokenSource cancellationTokenSource;

        // 新增：配置管理器
        private ToolConfigManager configManager;

        // 动画提取模式
        enum AnimExtractionMode { FromExistingClip, FromVmdFile }
        AnimExtractionMode animExtractionMode = AnimExtractionMode.FromVmdFile;

        // 是否使用快速配置
        private bool useQuickConfig = false;

        // PMX辅助文件
        private bool showPmxOptions = false;
        private string pmxFilePath = "";

        // 优先级放到最上面
        [MenuItem("MMD for Unity/VMD Morph Camera Animator Tool", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<VmdMorphAnimatorTool>("VMD Morph Animator Tool");
            window.minSize = new Vector2(600, 800);
            window.maxSize = new Vector2(600, 800);
        }

        private void OnGUI()
        {
            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);
            EditorGUILayout.LabelField("VMD Morph Animator Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 1. 动画提取部分
            var _ = DrawAnimationExtractionSection();
            DrawSeparator();

            // 2. 镜头提取部分
            DrawCameraExtractionSection();
            DrawSeparator();

            // 3. 表情提取部分
            DrawMorphExtractionSection();
            DrawSeparator();

            // 原有其他设置保持不变
            DrawModelSettings();
            DrawMorphMappingSettings();
            DrawSeparator();

            DrawOutputSettings();
            DrawNamingSettings();
            DrawActionButtons();
            DrawAudioSettings();
            // 使用timeline预览
            DrawTimelinePreview();
            DrawSeparator();


            DrawAssetBundleSettings();

            EditorGUILayout.EndScrollView();
        }

        #region 提取部分的UI和逻辑
        private async Task DrawAnimationExtractionSection()
        {
            EditorGUILayout.LabelField("1. 动画提取", EditorStyles.boldLabel);
            animExtractionMode = (AnimExtractionMode)EditorGUILayout.EnumPopup("动画来源", animExtractionMode);

            if (animExtractionMode == AnimExtractionMode.FromExistingClip)
            {
                EditorGUILayout.BeginHorizontal();
                sourceClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "已有动画剪辑", sourceClip, typeof(AnimationClip), false);

                if (GUILayout.Button("清空", GUILayout.Width(60)))
                {
                    sourceClip = null;
                    ResetAnimVmdState();
                }
                EditorGUILayout.EndHorizontal();
            }
            else // FromVmdFile
            {
                // 使用通用拖拽框方法
                animVmdFilePath = DrawVmdDragAndDropArea(animVmdFilePath, "动画VMD文件", "浏览...", "清空");
                // 配置超时秒
                timeoutSeconds = EditorGUILayout.IntField("转换超时（秒）", timeoutSeconds);
                // 帮助信息：如果转换失败，尝试手动生成anim文件
                EditorGUILayout.HelpBox("如果转换失败，请尝试手动生成anim文件", MessageType.Info);

                // 快速配置选项
                useQuickConfig = EditorGUILayout.Toggle("使用快速转换配置文件", useQuickConfig);

                // PMX辅助选项
                showPmxOptions = EditorGUILayout.Foldout(showPmxOptions, "使用PMX/PMD模型辅助转换（可选）");
                if (showPmxOptions)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("PMX/PMD文件", GUILayout.Width(EditorGUIUtility.labelWidth));
                    if (!string.IsNullOrEmpty(pmxFilePath) && File.Exists(pmxFilePath))
                    {
                        EditorGUILayout.LabelField(Path.GetFileName(pmxFilePath), EditorStyles.objectFieldThumb);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("未选择PMX/PMD文件", EditorStyles.objectFieldThumb);
                    }
                    if (GUILayout.Button("浏览...", GUILayout.Width(80)))
                    {
                        var path = EditorUtility.OpenFilePanel("选择PMX/PMD文件", Application.dataPath, "pmx,pmd");
                        if (!string.IsNullOrEmpty(path) && (path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".pmd", StringComparison.OrdinalIgnoreCase)))
                        {
                            pmxFilePath = path;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // 生成动画按钮
                if (!string.IsNullOrEmpty(animVmdFilePath) && File.Exists(animVmdFilePath))
                {
                    string animOutputDir = outputPath;
                    AssetUtils.EnsureDirectoryExists(animOutputDir);

                    string animFileName = Path.GetFileNameWithoutExtension(animVmdFilePath) + ".anim";
                    string animFullPath = Path.Combine(animOutputDir, animFileName);

                    EditorGUI.BeginDisabledGroup(isConverting);
                    if (GUILayout.Button("从VMD生成动画剪辑"))
                    {
                        // 如果已有任务，先取消
                        if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
                        {
                            cancellationTokenSource.Cancel();
                            cancellationTokenSource.Dispose();
                        }

                        cancellationTokenSource = new CancellationTokenSource();


                        try
                        {
                            bool result = await VMD2Anim.VMDConverter.ConvertVMD(
                                animVmdFilePath,
                                showPmxOptions && !string.IsNullOrEmpty(pmxFilePath) ? pmxFilePath : null,
                                animOutputDir,
                                (p, msg) =>
                                {
                                    progress = p;
                                    progressMessage = msg;
                                    Repaint(); // 刷新 GUI 以更新进度条
                                },
                                overwrite: true,
                                quickMode: useQuickConfig, // 使用快速配置
                                timeoutMs: timeoutSeconds * 1000,
                                cancellationToken: cancellationTokenSource.Token
                            );

                            if (result && File.Exists(animFullPath))
                            {
                                sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetUtils.GetProjectRelativePath(animFullPath));
                                EditorUtility.DisplayDialog("成功", $"已生成动画剪辑: {animFileName}", "确定");
                                AutoNameResources();
                                animVmdParsed = true;
                            }
                            else
                            {
                                EditorUtility.DisplayDialog("失败", "VMD转换为动画失败", "确定");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            EditorUtility.DisplayDialog("取消", "VMD转换已取消", "确定");
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.DisplayDialog("错误", $"VMD转换失败: {ex.Message}", "确定");
                            UnityEngine.Debug.LogError($"[VMD转换] 失败: {ex.Message}");
                        }
                        finally
                        {
                            isConverting = false;
                            cancellationTokenSource.Dispose();
                            cancellationTokenSource = null;
                        }
                    }
                    EditorGUI.EndDisabledGroup();

                    // 转换中的进度条和取消按钮
                    if (isConverting)
                    {
                        EditorGUILayout.LabelField("转换进度:");
                        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, progressMessage);
                        if (GUILayout.Button("取消"))
                        {
                            cancellationTokenSource?.Cancel();
                        }
                        Repaint();
                    }
                }
            }
        }
        private void DrawCameraExtractionSection()
        {
            EditorGUILayout.LabelField("2. 镜头提取", EditorStyles.boldLabel);
            enableCameraAnimation = EditorGUILayout.Toggle("启用镜头动画", enableCameraAnimation);

            if (enableCameraAnimation)
            {
                // 绘制多文件拖拽区域
                cameraVmdFilePaths = DrawMultiVmdDragAndDropArea(cameraVmdFilePaths, "镜头VMD文件", "添加镜头VMD", "清空所有");

                // 显示已添加的文件列表
                if (cameraVmdFilePaths.Count > 0)
                {
                    EditorGUILayout.LabelField("已添加的镜头VMD文件:", EditorStyles.miniBoldLabel);
                    for (int i = 0; i < cameraVmdFilePaths.Count; i++)
                    {

                        string fileName = Path.GetFileName(cameraVmdFilePaths[i]);
                        EditorGUILayout.LabelField($"{i + 1}. {fileName}", EditorStyles.miniLabel);
                        if (GUILayout.Button("移除", GUILayout.Width(50)))
                        {
                            cameraVmdFilePaths.RemoveAt(i);
                            Repaint();
                            break;
                        }

                    }
                }
                // 镜头缩放配置
                cameraScale = EditorGUILayout.Slider("相机位移缩放", cameraScale, 0.1f, 2.0f);


                showCameraAdvancedOptions = EditorGUILayout.Foldout(showCameraAdvancedOptions, "镜头路径配置");
                if (showCameraAdvancedOptions)
                {
                    cameraRootPath = EditorGUILayout.TextField("相机位移接收路径", cameraRootPath);
                    cameraDistancePath = EditorGUILayout.TextField("Distance父对象路径", cameraDistancePath);
                    cameraComponentPath = EditorGUILayout.TextField("相机组件完整路径", cameraComponentPath);
                }

                // 解析按钮和状态
                if (cameraVmdFilePaths.Count > 0 && cameraVmdFilePaths.All(File.Exists))
                {
                    if (GUILayout.Button("解析所有镜头VMD文件"))
                    {
                        ParseAllCameraVmdFiles();
                    }
                }

                if (cameraVmdParsed)
                {
                    EditorGUILayout.LabelField($"✓ 已解析 {vmdCameraFrames.Count} 个镜头帧 (来自 {cameraVmdFilePaths.Count} 个文件)", EditorStyles.miniLabel);
                }

                //  清空所有镜头帧
                if (GUILayout.Button("清空所有镜头帧"))
                {
                    ResetCameraVmdState();
                    Repaint();
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawMorphExtractionSection()
        {
            EditorGUILayout.LabelField("3. 表情提取", EditorStyles.boldLabel);

            // 绘制多文件拖拽区域
            morphVmdFilePaths = DrawMultiVmdDragAndDropArea(morphVmdFilePaths, "表情VMD文件", "添加表情VMD", "清空所有");

            // 显示已添加的文件列表
            if (morphVmdFilePaths.Count > 0)
            {
                EditorGUILayout.LabelField("已添加的表情VMD文件:", EditorStyles.miniBoldLabel);
                for (int i = 0; i < morphVmdFilePaths.Count; i++)
                {

                    string fileName = Path.GetFileName(morphVmdFilePaths[i]);
                    EditorGUILayout.LabelField($"{i + 1}. {fileName}", EditorStyles.miniLabel);
                    if (GUILayout.Button("移除", GUILayout.Width(50)))
                    {
                        morphVmdFilePaths.RemoveAt(i);
                        Repaint();
                        break;
                    }

                }
            }

            // 解析按钮和状态
            if (morphVmdFilePaths.Count > 0 && morphVmdFilePaths.All(File.Exists))
            {
                if (GUILayout.Button("解析所有表情VMD文件"))
                {
                    ParseAllMorphVmdFiles();

                    // 初始化映射
                    if (directMappingMode && IsMorphVmdDataReady())
                    {
                        MorphUtils.InitializeDirectMorphMapping(vmdMorphFrames, directMappingMode, morphMapping, availableMorphs, selectedMorphs);
                    }
                }
            }

            if (morphVmdParsed)
            {
                var uniqueMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
                EditorGUILayout.LabelField($"✓ 已解析 {vmdMorphFrames.Count} 个表情帧，包含 {uniqueMorphs} 种表情 (来自 {morphVmdFilePaths.Count} 个文件)", EditorStyles.miniLabel);
            }
            // 清空所有表情帧

            if (GUILayout.Button("清空所有表情帧"))
            {
                ResetMorphVmdState();
                Repaint();
            }

            EditorGUILayout.Space();
        }

        #endregion

        #region 通用拖拽框方法

        // 单个VMD文件拖拽框
        private string DrawVmdDragAndDropArea(string currentPath, string label, string browseButtonText, string clearButtonText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));

            bool fileExists = !string.IsNullOrEmpty(currentPath) && File.Exists(currentPath);
            Rect dragAreaRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));

            // 绘制拖拽区域
            if (fileExists)
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.9f, 0.95f, 0.9f));
                EditorGUI.LabelField(dragAreaRect, Path.GetFileName(currentPath), EditorStyles.objectFieldThumb);
            }
            else
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.95f, 0.95f, 0.95f));
                EditorGUI.LabelField(dragAreaRect, $"未选择{label} (可拖拽)", EditorStyles.objectFieldThumb);
            }

            // 处理拖拽事件
            HandleVmdDragAndDrop(dragAreaRect, ref currentPath, false);

            if (GUILayout.Button(browseButtonText, GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel($"选择{label}", Application.dataPath, "vmd");
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = path;
                }
            }

            if (GUILayout.Button(clearButtonText, GUILayout.Width(60)))
            {
                currentPath = "";
            }
            EditorGUILayout.EndHorizontal();

            return currentPath;
        }

        // 多个VMD文件拖拽框
        private List<string> DrawMultiVmdDragAndDropArea(List<string> currentPaths, string label, string addButtonText, string clearButtonText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));

            bool hasFiles = currentPaths != null && currentPaths.Count > 0;
            Rect dragAreaRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));

            // 绘制拖拽区域
            if (hasFiles)
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.9f, 0.95f, 0.9f));
                EditorGUI.LabelField(dragAreaRect, $"已选择 {currentPaths.Count} 个文件", EditorStyles.objectFieldThumb);
            }
            else
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.95f, 0.95f, 0.95f));
                EditorGUI.LabelField(dragAreaRect, $"未选择{label} (可拖拽多个)", EditorStyles.objectFieldThumb);
            }

            // 处理拖拽事件（支持多个文件）
            HandleVmdDragAndDrop(dragAreaRect, ref currentPaths, true);

            if (GUILayout.Button(addButtonText, GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel($"选择{label}", Application.dataPath, "vmd");
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (!currentPaths.Contains(path))
                    {
                        currentPaths.Add(path);
                    }
                }
            }

            if (GUILayout.Button(clearButtonText, GUILayout.Width(80)))
            {
                currentPaths.Clear();
            }

            EditorGUILayout.EndHorizontal();

            return currentPaths;
        }

        // 处理VMD文件拖拽逻辑
        private void HandleVmdDragAndDrop(Rect dragAreaRect, ref string filePath, bool isMulti)
        {
            Event evt = Event.current;
            if (dragAreaRect.Contains(evt.mousePosition))
            {
                switch (evt.type)
                {
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                        if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            string vmdPath = DragAndDrop.paths.FirstOrDefault(p => p.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(vmdPath))
                            {
                                filePath = vmdPath;
                                evt.Use();
                            }
                        }
                        break;
                }
            }
        }

        // 处理多文件VMD拖拽逻辑
        private void HandleVmdDragAndDrop(Rect dragAreaRect, ref List<string> filePaths, bool isMulti)
        {
            Event evt = Event.current;
            if (dragAreaRect.Contains(evt.mousePosition))
            {
                switch (evt.type)
                {
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                        if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            foreach (string path in DragAndDrop.paths)
                            {
                                if (path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase) && !filePaths.Contains(path))
                                {
                                    filePaths.Add(path);
                                }
                            }
                            evt.Use();
                        }
                        break;
                }
            }
        }

        #endregion

        #region 原有UI方法（保持不变或微调）

        private void DrawSeparator()
        {
            EditorGUILayout.Space();
            Rect rect = GUILayoutUtility.GetRect(1f, 1.5f);
            rect.width = EditorGUIUtility.currentViewWidth - 20f;
            rect.x += 10f;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space();
        }

        private void DrawNamingSettings()
        {
            EditorGUILayout.LabelField("统一资源命名设置", EditorStyles.miniBoldLabel);
            var oldBaseName = bundleBaseName;
            bundleBaseName = EditorGUILayout.TextField("基础名称", bundleBaseName);

            if (!string.IsNullOrEmpty(bundleBaseName) && oldBaseName != bundleBaseName)
            {
                newClipName = bundleBaseName;
                controllerName = bundleBaseName;
            }

            if (GUILayout.Button("自动命名", GUILayout.Width(100)))
            {
                AutoNameResources();
            }
            EditorGUILayout.Space();
        }

        private void DrawModelSettings()
        {
            EditorGUILayout.LabelField("模型表情设置", EditorStyles.miniBoldLabel);
            directMappingMode = EditorGUILayout.Toggle("直接映射", directMappingMode);

            if (directMappingMode)
            {
                EditorGUILayout.HelpBox("直接映射模式将直接使用VMD中的表情写入到对应路径的动画里，无需关联模型", MessageType.Info);

                showSkinnedMeshOptions = EditorGUILayout.Foldout(showSkinnedMeshOptions, "SkinnedMeshRenderer 路径设置");
                if (showSkinnedMeshOptions)
                {
                    EditorGUILayout.BeginVertical();
                    defaultSkinnedMeshPath = EditorGUILayout.TextField("SkinnedMeshRenderer路径", defaultSkinnedMeshPath);
                    defaultSkinnedMeshName = EditorGUILayout.TextField("组件名称", defaultSkinnedMeshName);
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("非直接映射模式需要关联目标模型", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (directMappingMode)
            {

            }
            else
            {
                targetModel = (GameObject)EditorGUILayout.ObjectField(
                    "目标模型", targetModel, typeof(GameObject), true);
                // 添加判断，避免重复初始化
                if (targetModel != null)
                {
                    bodyRenderer = ModelUtils.UpdateModelComponents(
                        targetModel,
                        bodyRenderer,
                        availableMorphs,
                        selectedMorphs,
                        directMappingMode,
                        vmdMorphFrames,
                        morphMapping,
                        vrmBlendShapeMapping,
                        IsMorphVmdDataReady
                    );
                }
            }

            if (GUILayout.Button("重置", GUILayout.Width(60)))
            {
                targetModel = null;
                ResetModelState();
                if (directMappingMode && IsMorphVmdDataReady())
                {
                    MorphUtils.InitializeDirectMorphMapping(vmdMorphFrames, directMappingMode, morphMapping, availableMorphs, selectedMorphs);
                }
            }

            EditorGUILayout.EndHorizontal();
            ShowMorphStatistics();
            EditorGUILayout.Space();
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField("输出设置", EditorStyles.miniBoldLabel);

            EditorGUILayout.LabelField("动画曲线添加选项", EditorStyles.miniBoldLabel);
            addMorphCurves = EditorGUILayout.Toggle("添加表情曲线", addMorphCurves);
            addCameraCurves = EditorGUILayout.Toggle("添加镜头曲线", addCameraCurves);

            EditorGUILayout.BeginHorizontal();
            if (addMorphCurves && addCameraCurves)
            {
                EditorGUILayout.HelpBox("将原有动画与表情动画、镜头动画合并输出", MessageType.Info);
            }
            else if (addMorphCurves)
            {
                EditorGUILayout.HelpBox("将原有动画与表情动画合并输出", MessageType.Info);
            }
            else if (addCameraCurves)
            {
                EditorGUILayout.HelpBox("将原有动画与镜头动画合并输出", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("请至少选择一种曲线类型添加", MessageType.Warning);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private void DrawMorphMappingSettings()
        {
            showMappingOptions = EditorGUILayout.Foldout(showMappingOptions, "形态键选择与映射设置");
            if (showMappingOptions && availableMorphs.Count > 0 && IsMorphVmdDataReady())
            {
                allMorphsScrollPos = EditorGUILayout.BeginScrollView(allMorphsScrollPos, GUILayout.Height(300));
                EditorGUILayout.LabelField("选择需要使用的形态键并设置映射关系", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("（勾选启用，文本框填写映射目标名称）", EditorStyles.miniLabel);

                // 批量操作按钮
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("全选")) MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, true);
                if (GUILayout.Button("选择前20个")) MorphUtils.SelectFirstNMorphs(availableMorphs, selectedMorphs, 20);
                if (GUILayout.Button("取消全选")) MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, false);
                EditorGUILayout.EndHorizontal();

                // 获取VMD中的所有唯一形态键名称
                var vmdMorphNames = vmdMorphFrames.Select(f => f.MorphName).Distinct().ToList();

                foreach (var vmdMorph in vmdMorphNames)
                {
                    EditorGUILayout.BeginHorizontal();

                    bool isSelected = selectedMorphs.TryGetValue(vmdMorph, out bool selectedValue) ? selectedValue : false;

                    EditorGUI.BeginChangeCheck();
                    isSelected = EditorGUILayout.ToggleLeft("", isSelected, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedMorphs[vmdMorph] = isSelected;
                    }

                    EditorGUILayout.LabelField(vmdMorph, GUILayout.Width(150));

                    string currentMapping = morphMapping.TryGetValue(vmdMorph, out string mapValue)
                        ? mapValue
                        : ModelUtils.GetMappedMorphName(vmdMorph, morphMapping, vrmBlendShapeMapping, availableMorphs);

                    EditorGUI.BeginChangeCheck();
                    currentMapping = EditorGUILayout.TextField(currentMapping);
                    if (EditorGUI.EndChangeCheck())
                    {
                        morphMapping[vmdMorph] = currentMapping;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
            else if (availableMorphs.Count == 0 && (targetModel != null || IsMorphVmdDataReady()))
            {
                EditorGUILayout.HelpBox("未找到可用的形态键数据，请先解析表情VMD文件或关联模型", MessageType.Info);
            }
            EditorGUILayout.Space();
        }

        private void DrawActionButtons()
        {
            GUI.enabled = CanProcessAnimation();
            if (GUILayout.Button("添加到动画并创建控制器", GUILayout.Height(30)))
            {
                ProcessAnimationAndController();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
        }

        private void DrawAudioSettings()
        {
            EditorGUILayout.LabelField("音频设置", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("音频文件", GUILayout.Width(EditorGUIUtility.labelWidth));

            EditorGUI.BeginChangeCheck();
            var projectRelativeAudioPath = AssetUtils.GetProjectRelativePath(audioFilePath);
            var audioObj = !string.IsNullOrEmpty(projectRelativeAudioPath)
                ? AssetDatabase.LoadAssetAtPath<Object>(projectRelativeAudioPath)
                : null;

            audioObj = EditorGUILayout.ObjectField(audioObj, typeof(AudioClip), false);

            if (EditorGUI.EndChangeCheck())
            {
                audioFilePath = audioObj != null ? AssetDatabase.GetAssetPath(audioObj) : "";
            }

            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                EditorGUILayout.LabelField(Path.GetFileName(audioFilePath), EditorStyles.objectFieldThumb);
            }
            else
            {
                EditorGUILayout.LabelField("未选择音频文件", EditorStyles.objectFieldThumb);
            }

            if (GUILayout.Button("浏览...", GUILayout.Width(80)))
            {
                BrowseAudioFile();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }
        private void DrawTimelinePreview()
        {
            // 折叠区域控制
            showTimelinePreview = EditorGUILayout.Foldout(showTimelinePreview, "Timeline 预览", true);

            if (showTimelinePreview)
            {
                EditorGUI.indentLevel++;

                // 人物模型拖放区域
                EditorGUILayout.LabelField("角色模型", EditorStyles.boldLabel);
                characterModel = EditorGUILayout.ObjectField(
                    "拖放模型到此处",
                    characterModel,
                    typeof(GameObject),
                    true) as GameObject;

                EditorGUILayout.Space();

                // 创建Timeline按钮
                GUI.enabled = characterModel != null &&
                             !string.IsNullOrEmpty(bundleBaseName) &&
                             Directory.Exists(DefaultOutputPath);

                if (GUILayout.Button("创建预览Timeline"))
                {
                    CreateTimelinePreview();
                }

                // 按钮状态提示
                if (!GUI.enabled)
                {
                    string disabledReason = "";
                    if (characterModel == null)
                        disabledReason = "请先指定角色模型";
                    else if (string.IsNullOrEmpty(bundleBaseName))
                        disabledReason = "请设置有效的基础名称";
                    else if (!Directory.Exists(DefaultOutputPath))
                        disabledReason = "输出目录不存在，请先生成动画资源";

                    EditorGUILayout.HelpBox(disabledReason, MessageType.Info);
                }

                GUI.enabled = true;
                EditorGUI.indentLevel--;
            }
        }
        private void CreateTimelinePreview()
        {
            // --------------- 核心修改1：强制覆写模型的Animator控制器 ---------------
            if (characterModel == null)
            {
                EditorUtility.DisplayDialog("错误", "请先在 Inspector 中选择角色模型！", "确定");
                return;
            }

            // 1. 获取/添加模型的Animator组件（确保模型具备动画播放能力）
            Animator characterAnimator = characterModel.GetComponent<Animator>();
            if (characterAnimator == null)
            {
                characterAnimator = characterModel.AddComponent<Animator>();
                EditorUtility.DisplayDialog("提示", "已为模型自动添加Animator组件", "确定");
            }

            // 2. 加载当前工具生成的目标Controller（必须是包含Timeline所需动画的Controller）
            string targetControllerPath = $"{DefaultOutputPath}{bundleBaseName}.controller";
            RuntimeAnimatorController targetController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(targetControllerPath);

            if (targetController == null)
            {
                EditorUtility.DisplayDialog("错误", $"未找到目标动画控制器：{targetControllerPath}\n请先生成动画资源！", "确定");
                return;
            }

            // 3. 强制覆写Animator的Controller（不管之前有没有，直接替换为目标Controller）
            if (characterAnimator.runtimeAnimatorController != targetController)
            {
                // 记录旧Controller名称，用于用户提示
                string oldControllerName = characterAnimator.runtimeAnimatorController?.name ?? "空控制器";
                // 强制赋值目标Controller
                characterAnimator.runtimeAnimatorController = targetController;
                // 标记模型为已修改，确保Controller变更被保存
                EditorUtility.SetDirty(characterModel);
                // 提示用户“控制器已被更新”（避免用户困惑）
                Debug.Log($"控制器已更新: 模型原有控制器：{oldControllerName}，已替换为：{targetController.name}（用于匹配当前Timeline动画）");
            }
            // ----------------------------------------------------------------------

            // 创建Timeline资产路径
            string timelinePath = $"{DefaultOutputPath}{bundleBaseName}_preview.asset";
            string sceneDirectorName = $"{bundleBaseName}_director";

            // 确保输出目录存在
            if (!Directory.Exists(DefaultOutputPath))
            {
                Directory.CreateDirectory(DefaultOutputPath);
            }

            // 创建或获取Timeline资产（显式指定类型参数）
            TimelineAsset timelineAsset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (timelineAsset == null)
            {
                timelineAsset = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(timelineAsset, timelinePath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                // 清除现有轨道（避免旧轨道干扰）
                foreach (var track in timelineAsset.GetOutputTracks())
                {
                    timelineAsset.DeleteTrack(track);
                }
                EditorUtility.SetDirty(timelineAsset);
            }

            // 在当前场景中创建或获取PlayableDirector
            PlayableDirector director = GameObject.FindObjectOfType<PlayableDirector>();
            GameObject directorObj = null;

            if (director == null || director.gameObject.name != sceneDirectorName)
            {
                // 清除场景中同名旧导演对象（避免冲突）
                var oldDirectors = GameObject.FindObjectsOfType<PlayableDirector>();
                foreach (var oldDir in oldDirectors)
                {
                    if (oldDir.gameObject.name == sceneDirectorName)
                        DestroyImmediate(oldDir.gameObject);
                }

                // 创建新的导演对象并关联Timeline
                directorObj = new GameObject(sceneDirectorName);
                director = directorObj.AddComponent<PlayableDirector>();
                director.playableAsset = timelineAsset;
                director.extrapolationMode = DirectorWrapMode.Hold;
            }
            else
            {
                director.playableAsset = timelineAsset;
                EditorUtility.SetDirty(director);
            }

            // 添加动画轨道并绑定到模型（关键：确保轨道控制目标模型）
            AnimationTrack animTrack = timelineAsset.CreateTrack<AnimationTrack>("动画轨道");
            director.SetGenericBinding(animTrack, characterModel);

            // --------------- 核心修改2：直接使用已覆写的目标Controller ---------------
            // 此时模型的Animator已被强制设置为targetController，直接获取即可
            RuntimeAnimatorController modelController = characterAnimator.runtimeAnimatorController;
            // ----------------------------------------------------------------------

            if (modelController != null)
            {
                // 查找并添加当前Controller中匹配的动画剪辑（避免无关动画混入）
                foreach (var clip in modelController.animationClips)
                {
                    if (clip.name.Contains(bundleBaseName))
                    {
                        TimelineClip animTimelineClip = animTrack.CreateDefaultClip();
                        animTimelineClip.displayName = clip.name;
                        animTimelineClip.start = 0;
                        animTimelineClip.duration = clip.length;

                        // 赋值动画剪辑（修复属性名大小写问题，统一用小写clip，兼容不同Unity版本）
                        AnimationPlayableAsset animationAsset = animTimelineClip.asset as AnimationPlayableAsset;
                        if (animationAsset != null)
                        {
                            animationAsset.clip = clip; // 部分Unity版本中属性为小写clip，根据实际版本调整
                                                        // 若报错“不存在clip属性”，则改为：animationAsset.AnimationClip = clip;
                        }
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "角色模型的Animator没有关联控制器", "确定");
                return;
            }

            // 添加音频轨道（可选）
            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                AudioTrack audioTrack = timelineAsset.CreateTrack<AudioTrack>("音频轨道");
                var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(audioFilePath);

                if (audioClip != null)
                {
                    TimelineClip audioTimelineClip = audioTrack.CreateDefaultClip();
                    audioTimelineClip.displayName = audioClip.name;
                    audioTimelineClip.start = 0;
                    audioTimelineClip.duration = audioClip.length;

                    AudioPlayableAsset audioAsset = audioTimelineClip.asset as AudioPlayableAsset;
                    if (audioAsset != null)
                    {
                        audioAsset.clip = audioClip;
                    }
                }
            }

            // 保存所有修改（确保Controller覆写、Timeline轨道变更生效）
            EditorUtility.SetDirty(characterModel);   // 保存模型的Controller变更
            EditorUtility.SetDirty(timelineAsset);    // 保存Timeline轨道变更
            EditorUtility.SetDirty(director.gameObject); // 保存导演对象变更
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 提示用户操作指引
            EditorUtility.DisplayDialog(
                "Timeline创建成功",
                $"✅ 已完成以下操作：\n" +
                $"- 模型：{characterModel.name}\n" +
                $"- 控制器：已绑定 {targetController.name}\n" +
                $"- Timeline路径：{timelinePath}\n\n" +
                $"操作提示：\n" +
                $"1. 在Window > Sequencing > Timeline打开编辑器\n" +
                $"2. 点击场景播放按钮预览动画",
                "确定");

            // 自动打开Timeline窗口（提升用户体验）
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
        }


        private void DrawAssetBundleSettings()
        {
            EditorGUILayout.LabelField("资源打包设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("打包前请先在Unity内预览，确保一切正常，并且确保音频轴对上动作轴", MessageType.Info);
            EditorGUILayout.HelpBox("如果预览时人物朝向、初始位置不对，请在动画Inspector中调整", MessageType.Info);
            EditorGUILayout.LabelField("自动打包（高级）", EditorStyles.miniBoldLabel);

            showBundleOptions = EditorGUILayout.Foldout(showBundleOptions, "打包高级选项");
            if (showBundleOptions)
            {
                bundleOptions = (BuildAssetBundleOptions)EditorGUILayout.EnumFlagsField("打包选项", bundleOptions);
                EditorGUILayout.HelpBox(
                    "None: 基本打包\n" +
                    "ChunkBasedCompression: 分块压缩\n" +
                    "DeterministicAssetBundle: 确定性打包",
                    MessageType.Info
                );
            }

            if (string.IsNullOrEmpty(bundleOutputPath))
            {
                bundleOutputPath = outputPath;
            }
            bundleOutputPath = EditorGUILayout.TextField("自动打包输出路径", bundleOutputPath);
            if (GUILayout.Button("选择输出路径", GUILayout.Width(120)))
            {
                bundleOutputPath = SelectBundleOutputPath(bundleOutputPath);
            }

            if (!string.IsNullOrEmpty(bundleOutputPath))
            {
                bool isInProject = bundleOutputPath.StartsWith(Application.dataPath) ||
                                  bundleOutputPath.StartsWith("Assets/");
                EditorGUILayout.HelpBox(
                    isInProject ? $"输出路径在项目内: {bundleOutputPath}" :
                                  $"输出路径在项目外: {bundleOutputPath}",
                    MessageType.Info
                );
            }

            DrawBundleAssetsPreview(outputPath);

            GUI.enabled = CanBuildBundle(bundleOutputPath);
            if (GUILayout.Button("📦 自动打包", GUILayout.Height(30)))
            {
                AssetUtils.BuildAssetBundle(
                    outputPath,
                    TempBuildFolder,
                    bundleBaseName,
                    audioFilePath,
                    bundleOutputPath,
                    bundleOptions
                );
            }
            GUI.enabled = true;

            EditorGUILayout.HelpBox("如果自动打包失败, 请手动构建文件", MessageType.Info);
            EditorGUILayout.Space();
        }

        #endregion

        #region 新增和修改的核心方法

        private void BrowseAnimVmdFile()
        {
            var path = EditorUtility.OpenFilePanel("选择动画VMD文件", Application.dataPath, "vmd");
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd"))
            {
                animVmdFilePath = path;
            }
        }

        private void BrowseCameraVmdFile()
        {
            // Unity EditorUtility does not have OpenFilePanelMultiple, so we use a loop for multiple selection
            bool addMore = true;
            while (addMore)
            {
                string path = EditorUtility.OpenFilePanel("选择镜头VMD文件", Application.dataPath, "vmd");
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (!cameraVmdFilePaths.Contains(path))
                    {
                        cameraVmdFilePaths.Add(path);
                    }
                }
                // Ask user if they want to add more files
                addMore = EditorUtility.DisplayDialog("添加更多文件?", "是否继续添加镜头VMD文件？", "继续添加", "完成");
            }
        }

        private void BrowseMorphVmdFile()
        {
            // Unity EditorUtility does not have OpenFilePanelMultiple, so we use a loop for multiple selection
            bool addMore = true;
            while (addMore)
            {
                string path = EditorUtility.OpenFilePanel("选择表情VMD文件", Application.dataPath, "vmd");
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
                {
                    if (!morphVmdFilePaths.Contains(path))
                    {
                        morphVmdFilePaths.Add(path);
                    }
                }
                // Ask user if they want to add more files
                addMore = EditorUtility.DisplayDialog("添加更多文件?", "是否继续添加表情VMD文件？", "继续添加", "完成");
            }
        }

        private void ParseAnimVmdFile()
        {
            if (string.IsNullOrEmpty(animVmdFilePath) || !File.Exists(animVmdFilePath))
            {
                EditorUtility.DisplayDialog("错误", "动画VMD文件不存在", "确定");
                return;
            }

            try
            {
                using (var stream = new FileStream(animVmdFilePath, FileMode.Open, FileAccess.Read))
                {
                    parsedAnimVmd = VMDParser.ParseVMD(stream);
                    animVmdParsed = true;
                    Debug.Log($"成功解析动画VMD文件: {Path.GetFileName(animVmdFilePath)}");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("解析错误", $"解析动画VMD文件时出错: {e.Message}", "确定");
                Debug.LogError($"动画VMD解析错误: {e}");
                animVmdParsed = false;
            }
        }

        // 解析所有镜头VMD文件并合并帧数据
        private void ParseAllCameraVmdFiles()
        {
            if (cameraVmdFilePaths == null || cameraVmdFilePaths.Count == 0 || !cameraVmdFilePaths.All(File.Exists))
            {
                EditorUtility.DisplayDialog("错误", "部分镜头VMD文件不存在", "确定");
                return;
            }

            try
            {
                parsedCameraVmds.Clear();
                vmdCameraFrames.Clear();
                cameraVmdParsed = false;

                int totalFiles = cameraVmdFilePaths.Count;
                int currentFile = 0;

                foreach (var filePath in cameraVmdFilePaths)
                {
                    currentFile++;
                    EditorUtility.DisplayProgressBar("解析镜头VMD",
                        $"正在解析 {Path.GetFileName(filePath)} ({currentFile}/{totalFiles})",
                        (float)currentFile / totalFiles);

                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var parsedVmd = VMDParser.ParseVMD(stream);
                        parsedCameraVmds.Add(parsedVmd);
                        vmdCameraFrames.AddRange(parsedVmd.Cameras);
                    }
                }

                // 按帧时间排序
                vmdCameraFrames = vmdCameraFrames.OrderBy(f => f.FrameIndex).ToList();
                cameraVmdParsed = true;
                Debug.Log($"成功解析 {totalFiles} 个镜头VMD文件，共 {vmdCameraFrames.Count} 个镜头帧");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("解析错误", $"解析镜头VMD文件时出错: {e.Message}", "确定");
                Debug.LogError($"镜头VMD解析错误: {e}");
                cameraVmdParsed = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // 解析所有表情VMD文件并合并帧数据
        private void ParseAllMorphVmdFiles()
        {
            if (morphVmdFilePaths == null || morphVmdFilePaths.Count == 0 || !morphVmdFilePaths.All(File.Exists))
            {
                EditorUtility.DisplayDialog("错误", "部分表情VMD文件不存在", "确定");
                return;
            }

            try
            {
                parsedMorphVmds.Clear();
                vmdMorphFrames.Clear();
                morphVmdParsed = false;

                int totalFiles = morphVmdFilePaths.Count;
                int currentFile = 0;

                foreach (var filePath in morphVmdFilePaths)
                {
                    currentFile++;
                    EditorUtility.DisplayProgressBar("解析表情VMD",
                        $"正在解析 {Path.GetFileName(filePath)} ({currentFile}/{totalFiles})",
                        (float)currentFile / totalFiles);

                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var parsedVmd = VMDParser.ParseVMD(stream);
                        parsedMorphVmds.Add(parsedVmd);
                        vmdMorphFrames.AddRange(parsedVmd.Morphs);
                    }
                }

                // 按帧时间排序
                vmdMorphFrames = vmdMorphFrames.OrderBy(f => f.FrameIndex).ToList();
                morphVmdParsed = true;

                var uniqueMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
                Debug.Log($"成功解析 {totalFiles} 个表情VMD文件，共 {vmdMorphFrames.Count} 个表情帧，{uniqueMorphs} 种表情");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("解析错误", $"解析表情VMD文件时出错: {e.Message}", "确定");
                Debug.LogError($"表情VMD解析错误: {e}");
                morphVmdParsed = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void TryFindAnimVmdFile()
        {
            try
            {
                var clipName = sourceClip.name;
                if (clipName.Contains("@"))
                {
                    clipName = clipName.Substring(clipName.IndexOf("@") + 1);
                }

                var possibleNames = new[] {
                    clipName,
                    clipName.Replace("_vmd", ""),
                    clipName.Replace("_VMD", ""),
                    $"{clipName}_vmd",
                    $"{clipName}_VMD"
                };

                var vmdFiles = Directory.GetFiles(Application.dataPath, "*.vmd", SearchOption.AllDirectories);
                foreach (var possibleName in possibleNames)
                {
                    animVmdFilePath = vmdFiles.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f) == possibleName);

                    if (!string.IsNullOrEmpty(animVmdFilePath))
                    {
                        Debug.Log($"自动找到动画VMD文件: {animVmdFilePath}");
                        if (!animVmdParsed)
                            ParseAnimVmdFile();
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"自动查找VMD文件时出错: {e.Message}");
            }
        }

        private void ProcessAnimationAndController()
        {
            if (!addMorphCurves && !addCameraCurves)
            {
                EditorUtility.DisplayDialog("提示", "请至少选择一种曲线类型添加", "确定");
                return;
            }

            try
            {
                // 1. 创建基础动画（复制原动画曲线）
                var baseClip = AnimUtils.CreateOriginalAnimationClip(sourceClip, bundleBaseName, DefaultFrameRate);
                if (baseClip == null)
                {
                    EditorUtility.DisplayDialog("错误", "未找到原动画剪辑", "确定");
                    return;
                }

                // 2. 根据选项添加表情曲线
                if (addMorphCurves && IsMorphVmdDataReady())
                {
                    baseClip = directMappingMode
                        ? AnimUtils.AddMorphCurvesDirectMode(baseClip, vmdMorphFrames, selectedMorphs, morphMapping, defaultSkinnedMeshPath)
                        : AnimUtils.AddMorphCurvesToAnimation(baseClip, vmdMorphFrames, selectedMorphs, morphMapping, targetModel, bodyRenderer);
                }

                // 3. 根据选项添加镜头曲线
                if (addCameraCurves && cameraVmdParsed)
                {
                    foreach (var cameraVmdFilePath in cameraVmdFilePaths)
                    {
                        baseClip = AnimUtils.AddCameraCurvesToClip(baseClip, cameraVmdFilePath, cameraRootPath, cameraDistancePath, cameraComponentPath, cameraScale);
                    }
                }

                // 设置动画属性
                AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(baseClip);
                // Root Transform Rotation
                clipSettings.loopTime = false;
                clipSettings.loopBlend = true;

                clipSettings.keepOriginalOrientation = true; // Based Upon: Original
                                                             // Bake Into Pose: true

                // Root Transform Position Y
                clipSettings.keepOriginalPositionY = true;   // Based Upon: Original
                                                             // Bake Into Pose: true

                // Root Transform Position XZ
                clipSettings.keepOriginalPositionXZ = true;  // Based Upon: Original
                                                             // Bake Into Pose: true

                clipSettings.loopBlendOrientation = true;
                clipSettings.loopBlendPositionY = true;
                clipSettings.loopBlendPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(baseClip, clipSettings);

                // 4. 保存动画剪辑
                string clipPath = $"{outputPath}{bundleBaseName}.anim";
                AssetDatabase.CreateAsset(baseClip, clipPath);

                // 5. 处理动画控制器
                AnimatorController controller = AssetUtils.CreateControllerForClip(baseClip, "", outputPath, bundleBaseName);

                EditorUtility.DisplayDialog("成功",
                $"已生成动画: {baseClip.name}\n" +
                (controller != null ? $"已生成控制器: {controller.name}" : ""),
                "确定");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"处理动画时出错: {e.Message}", "确定");
                Debug.LogError($"动画处理错误: {e}");
            }
        }

        #endregion

        #region 辅助方法和状态管理

        private void BrowseAudioFile()
        {
            var path = EditorUtility.OpenFilePanel("选择音频文件", Application.dataPath, "wav,mp3,ogg");
            if (!string.IsNullOrEmpty(path))
            {
                audioFilePath = AssetUtils.GetProjectRelativePath(path);
            }
        }

        private string SelectBundleOutputPath(string currentPath)
        {
            var path = EditorUtility.OpenFolderPanel("选择输出文件夹",
                string.IsNullOrEmpty(currentPath) ? Application.dataPath : currentPath,
                "");

            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            return currentPath;
        }

        private void DrawBundleAssetsPreview(string outputPath)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("将打包的资源:", EditorStyles.miniBoldLabel);

            string clipName = $"{bundleBaseName}.anim";
            EditorGUILayout.LabelField($"- 动画: {clipName}", EditorStyles.miniLabel);
            string clipFullPath = Path.Combine(outputPath, clipName);
            if (!File.Exists(clipFullPath))
            {
                EditorGUILayout.HelpBox($"动画文件 {clipName} 不存在于输出: {outputPath}", MessageType.Warning);
            }

            string controllerName = $"{bundleBaseName}.controller";
            EditorGUILayout.LabelField($"- 控制器: {controllerName}", EditorStyles.miniLabel);
            string controllerFullPath = Path.Combine(outputPath, controllerName);
            if (!File.Exists(controllerFullPath))
            {
                EditorGUILayout.HelpBox($"控制器文件 {controllerName} 不存在于输出: {outputPath}", MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                string audioName = Path.GetFileName(audioFilePath);
                EditorGUILayout.LabelField($"- 音频: {audioName}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("- 音频: 未选择", EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField("资源将统一命名并被打包输出为: " + bundleBaseName + ".unity3d", EditorStyles.miniBoldLabel);
        }

        private void ShowMorphStatistics()
        {
            if (!IsMorphVmdDataReady()) return;

            var totalMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
            var matchedMorphs = vmdMorphFrames
                .Select(f => ModelUtils.GetMappedMorphName(f.MorphName, morphMapping, vrmBlendShapeMapping, availableMorphs))
                .Distinct()
                .Count(n => availableMorphs.Contains(n));

            EditorGUILayout.LabelField($"VMD表情总数: {totalMorphs} 个", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"匹配到模型的表情: {matchedMorphs} 个", EditorStyles.miniLabel);

            var matchRate = totalMorphs > 0 ? (float)matchedMorphs / totalMorphs * 100 : 0;
            EditorGUILayout.LabelField($"匹配率: {matchRate:F1}%", EditorStyles.miniLabel);

            if (matchedMorphs == 0 && !directMappingMode)
            {
                EditorGUILayout.HelpBox("未找到匹配的表情数据，请检查形态键映射设置", MessageType.Warning);
            }
        }

        private void AutoNameResources()
        {
            string baseName = "";

            if (morphVmdFilePaths != null && morphVmdFilePaths.Count > 0)
            {
                baseName = Path.GetFileNameWithoutExtension(morphVmdFilePaths[0]);
            }
            else if (!string.IsNullOrEmpty(animVmdFilePath))
            {
                baseName = Path.GetFileNameWithoutExtension(animVmdFilePath);
            }
            else if (sourceClip != null)
            {
                baseName = sourceClip.name;
            }

            if (!string.IsNullOrEmpty(baseName))
            {
                bundleBaseName = baseName;
                newClipName = baseName;
                controllerName = baseName;
            }
        }

        #endregion

        #region 状态检查和重置

        private bool CanProcessAnimation()
        {
            bool hasValidSource = sourceClip != null;
            bool hasValidMorphData = !addMorphCurves || (IsMorphVmdDataReady() && selectedMorphs.Any(m => m.Value));
            bool hasValidCameraData = !addCameraCurves || cameraVmdParsed;
            bool hasValidModel = directMappingMode || (targetModel != null && bodyRenderer != null);

            return hasValidSource && hasValidCameraData && hasValidModel;
        }

        private bool CanBuildBundle(string outputPath)
        {
            return !string.IsNullOrEmpty(AssetUtils.GetAnimationPath(newClipName, outputPath, sourceClip)) &&
                   !string.IsNullOrEmpty(AssetUtils.GetControllerPath(controllerName, outputPath)) &&
                   !string.IsNullOrEmpty(outputPath) &&
                   !string.IsNullOrEmpty(bundleBaseName);
        }

        private bool IsMorphVmdDataReady() => morphVmdParsed && vmdMorphFrames != null && vmdMorphFrames.Count > 0;
        private bool IsCameraVmdDataReady() => cameraVmdParsed && vmdCameraFrames != null && vmdCameraFrames.Count > 0;
        private bool IsAnimVmdDataReady() => animVmdParsed;

        private bool IsModelDataReady() => !directMappingMode && targetModel != null && bodyRenderer != null;

        private void ResetAnimVmdState()
        {
            animVmdFilePath = "";
            animVmdParsed = false;
            parsedAnimVmd = null;
        }

        private void ResetCameraVmdState()
        {
            cameraVmdFilePaths.Clear();
            parsedCameraVmds.Clear();
            vmdCameraFrames.Clear();
            cameraVmdParsed = false;
        }

        private void ResetMorphVmdState()
        {
            morphVmdFilePaths.Clear();
            parsedMorphVmds.Clear();
            vmdMorphFrames.Clear();
            morphVmdParsed = false;
        }

        private void ResetModelState()
        {
            bodyRenderer = null;
            availableMorphs.Clear();
            selectedMorphs.Clear();
            morphMapping.Clear();
            Repaint();
        }

        private void OnEnable()
        {
            configManager = new ToolConfigManager();
            ApplyConfigToTool();
        }

        private void OnDisable()
        {
            SyncToolToConfig();
            configManager.SaveConfig();
        }

        private void ApplyConfigToTool()
        {
            var config = configManager.Config;

            defaultSkinnedMeshPath = config.defaultSkinnedMeshPath;
            defaultSkinnedMeshName = config.defaultSkinnedMeshName;
            directMappingMode = config.directMappingMode;
            enableCameraAnimation = config.enableCameraAnimation;
            cameraRootPath = config.cameraRootPath;
            cameraComponentPath = config.cameraComponentPath;
            cameraDistancePath = config.cameraDistancePath;
            bundleOutputPath = config.bundleOutputPath;
        }

        private void SyncToolToConfig()
        {
            var config = configManager.Config;

            config.defaultSkinnedMeshPath = defaultSkinnedMeshPath;
            config.defaultSkinnedMeshName = defaultSkinnedMeshName;
            config.directMappingMode = directMappingMode;
            config.enableCameraAnimation = enableCameraAnimation;
            config.cameraRootPath = cameraRootPath;
            config.cameraComponentPath = cameraComponentPath;
            config.cameraDistancePath = cameraDistancePath;
            config.bundleOutputPath = bundleOutputPath;
        }

        #endregion
    }
}