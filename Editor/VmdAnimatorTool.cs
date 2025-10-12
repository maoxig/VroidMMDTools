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
using static VroidMMDTools.LocalizationManager;
using static VroidMMDTools.L10nKeys;


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

            DrawLanguageSelector();
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
        private void DrawLanguageSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Get(LANGUAGE_LABEL), GUILayout.Width(120));

            var newLang = (Language)EditorGUILayout.EnumPopup(CurrentLanguage, GUILayout.Width(100));
            if (newLang != CurrentLanguage)
            {
                CurrentLanguage = newLang;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        #region 提取部分的UI和逻辑
        private async Task DrawAnimationExtractionSection()
        {
            EditorGUILayout.LabelField(Get(SECTION_ANIMATION), EditorStyles.boldLabel);
            animExtractionMode = (AnimExtractionMode)EditorGUILayout.EnumPopup(Get(ANIM_SOURCE), animExtractionMode);

            if (animExtractionMode == AnimExtractionMode.FromExistingClip)
            {
                EditorGUILayout.BeginHorizontal();
                sourceClip = (AnimationClip)EditorGUILayout.ObjectField(
                    Get(EXISTING_CLIP), sourceClip, typeof(AnimationClip), false);

                if (GUILayout.Button(Get(BTN_CLEAR), GUILayout.Width(60)))
                {
                    sourceClip = null;
                    ResetAnimVmdState();
                }
                EditorGUILayout.EndHorizontal();
            }
            else // FromVmdFile
            {
                // 使用通用拖拽框方法
                animVmdFilePath = DrawVmdDragAndDropArea(animVmdFilePath, Get(ANIM_VMD_FILE), Get(BTN_BROWSE), Get(BTN_CLEAR));
                // 配置超时秒
                timeoutSeconds = EditorGUILayout.IntField(Get(TIMEOUT_SECONDS), timeoutSeconds);
                // 帮助信息：如果转换失败，尝试手动生成anim文件
                EditorGUILayout.HelpBox(Get(HELP_CONVERSION_FAIL), MessageType.Info);

                // 快速配置选项
                useQuickConfig = EditorGUILayout.Toggle(Get(QUICK_CONFIG), useQuickConfig);

                // PMX辅助选项
                showPmxOptions = EditorGUILayout.Foldout(showPmxOptions, Get(PMX_ASSIST));
                if (showPmxOptions)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(Get(PMX_FILE), GUILayout.Width(EditorGUIUtility.labelWidth));
                    if (!string.IsNullOrEmpty(pmxFilePath) && File.Exists(pmxFilePath))
                    {
                        EditorGUILayout.LabelField(Path.GetFileName(pmxFilePath), EditorStyles.objectFieldThumb);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(Get(PMX_NOT_SELECTED), EditorStyles.objectFieldThumb);
                    }
                    if (GUILayout.Button(Get(BTN_BROWSE), GUILayout.Width(80)))
                    {
                        var path = EditorUtility.OpenFilePanel(Get("select_pmx_file"), Application.dataPath, "pmx,pmd");
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
                    if (GUILayout.Button(Get(BTN_GENERATE_ANIM)))
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
                                EditorUtility.DisplayDialog(Get(DIALOG_SUCCESS), string.Format(Get("msg_anim_generated"), animFileName), Get(DIALOG_CONFIRM));
                                AutoNameResources();
                                animVmdParsed = true;
                            }
                            else
                            {
                                EditorUtility.DisplayDialog(Get(DIALOG_ERROR), Get("msg_conversion_failed"), Get(DIALOG_CONFIRM));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            EditorUtility.DisplayDialog(Get(DIALOG_CANCEL), Get("msg_conversion_cancelled"), Get(DIALOG_CONFIRM));
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.DisplayDialog(
                                Get(DIALOG_ERROR),
                                string.Format(Get("msg_conversion_error"), ex.Message),
                                Get(DIALOG_CONFIRM)
                            );
                            UnityEngine.Debug.LogError(string.Format(Get("log_conversion_failed"), ex.Message));
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
                        EditorGUILayout.LabelField(Get(CONVERTING_PROGRESS));
                        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, progressMessage);
                        if (GUILayout.Button(Get(BTN_CANCEL)))
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
            EditorGUILayout.LabelField(Get(SECTION_CAMERA), EditorStyles.boldLabel);
            enableCameraAnimation = EditorGUILayout.Toggle(Get(ENABLE_CAMERA), enableCameraAnimation);

            if (enableCameraAnimation)
            {
                // 绘制多文件拖拽区域
                cameraVmdFilePaths = DrawMultiVmdDragAndDropArea(
                    cameraVmdFilePaths,
                    Get(CAMERA_VMD_FILE),
                    Get(BTN_ADD_CAMERA_VMD),
                    Get(BTN_CLEAR_ALL)
                );

                // 显示已添加的文件列表
                if (cameraVmdFilePaths.Count > 0)
                {
                    EditorGUILayout.LabelField(Get(ADDED_CAMERA_FILES), EditorStyles.miniBoldLabel);
                    for (int i = 0; i < cameraVmdFilePaths.Count; i++)
                    {

                        string fileName = Path.GetFileName(cameraVmdFilePaths[i]);
                        EditorGUILayout.LabelField($"{i + 1}. {fileName}", EditorStyles.miniLabel);
                        if (GUILayout.Button(Get(BTN_REMOVE), GUILayout.Width(50)))
                        {
                            cameraVmdFilePaths.RemoveAt(i);
                            Repaint();
                            break;
                        }

                    }
                }
                // 镜头缩放配置
                cameraScale = EditorGUILayout.Slider(Get(CAMERA_SCALE), cameraScale, 0.1f, 2.0f);


                showCameraAdvancedOptions = EditorGUILayout.Foldout(
                    showCameraAdvancedOptions,
                    Get(CAMERA_PATH_CONFIG)
                );
                if (showCameraAdvancedOptions)
                {
                    cameraRootPath = EditorGUILayout.TextField(Get(CAMERA_ROOT_PATH), cameraRootPath);
                    cameraDistancePath = EditorGUILayout.TextField(Get(CAMERA_DISTANCE_PATH), cameraDistancePath);
                    cameraComponentPath = EditorGUILayout.TextField(Get(CAMERA_COMPONENT_PATH), cameraComponentPath);
                }

                // 解析按钮和状态
                if (cameraVmdFilePaths.Count > 0 && cameraVmdFilePaths.All(File.Exists))
                {
                    if (GUILayout.Button(Get(BTN_PARSE_CAMERA)))
                    {
                        ParseAllCameraVmdFiles();
                    }
                }

                if (cameraVmdParsed)
                {
                    EditorGUILayout.LabelField(
                        string.Format(Get(CAMERA_PARSED_INFO), vmdCameraFrames.Count, cameraVmdFilePaths.Count),
                        EditorStyles.miniLabel
                    );
                }

                //  清空所有镜头帧
                if (GUILayout.Button(Get(BTN_CLEAR_ALL)))
                {
                    ResetCameraVmdState();
                    Repaint();
                }
            }
            EditorGUILayout.Space();
        }

        private void DrawMorphExtractionSection()
        {
            EditorGUILayout.LabelField(Get(SECTION_MORPH), EditorStyles.boldLabel);

            // 绘制多文件拖拽区域
            morphVmdFilePaths = DrawMultiVmdDragAndDropArea(
                morphVmdFilePaths,
                Get(MORPH_VMD_FILE),
                Get(BTN_ADD_MORPH_VMD),
                Get(BTN_CLEAR_ALL)
            );

            // 显示已添加的文件列表
            if (morphVmdFilePaths.Count > 0)
            {
                EditorGUILayout.LabelField(Get(ADDED_MORPH_FILES), EditorStyles.miniBoldLabel);
                for (int i = 0; i < morphVmdFilePaths.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    string fileName = Path.GetFileName(morphVmdFilePaths[i]);
                    EditorGUILayout.LabelField($"{i + 1}. {fileName}", EditorStyles.miniLabel);
                    if (GUILayout.Button(Get(BTN_REMOVE), GUILayout.Width(50)))
                    {
                        morphVmdFilePaths.RemoveAt(i);
                        Repaint();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 解析按钮和状态
            if (morphVmdFilePaths.Count > 0 && morphVmdFilePaths.All(File.Exists))
            {
                if (GUILayout.Button(Get(BTN_PARSE_MORPH)))
                {
                    ParseAllMorphVmdFiles();

                    if (directMappingMode && IsMorphVmdDataReady())
                    {
                        MorphUtils.InitializeDirectMorphMapping(
                            vmdMorphFrames,
                            directMappingMode,
                            morphMapping,
                            availableMorphs,
                            selectedMorphs
                        );
                    }
                }
            }

            if (morphVmdParsed)
            {
                var uniqueMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
                EditorGUILayout.LabelField(string.Format(Get("morph_parsed_info"), vmdMorphFrames.Count, uniqueMorphs, morphVmdFilePaths.Count), EditorStyles.miniLabel);
            }
            // 清空所有表情帧

            if (GUILayout.Button(Get(BTN_CLEAR_ALL)))
            {
                ResetMorphVmdState();
                Repaint();
            }

            EditorGUILayout.Space();
        }

        #endregion

        #region 通用拖拽框方法

        // 单个VMD文件拖拽框
        private string DrawVmdDragAndDropArea(
            string currentPath,
            string label,
            string browseButtonText,
            string clearButtonText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));

            bool fileExists = !string.IsNullOrEmpty(currentPath) && File.Exists(currentPath);
            Rect dragAreaRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));

            if (fileExists)
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.9f, 0.95f, 0.9f));
                EditorGUI.LabelField(dragAreaRect, Path.GetFileName(currentPath), EditorStyles.objectFieldThumb);
            }
            else
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.95f, 0.95f, 0.95f));
                EditorGUI.LabelField(
                    dragAreaRect,
                    string.Format(Get("file_not_selected"), label),
                    EditorStyles.objectFieldThumb
                );
            }

            HandleVmdDragAndDrop(dragAreaRect, ref currentPath, false);

            if (GUILayout.Button(browseButtonText, GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel(
                    string.Format(Get("select_anim_vmd")),
                    Application.dataPath,
                    "vmd"
                );
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
        private List<string> DrawMultiVmdDragAndDropArea(
            List<string> currentPaths,
            string label,
            string addButtonText,
            string clearButtonText)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));

            bool hasFiles = currentPaths != null && currentPaths.Count > 0;
            Rect dragAreaRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));

            if (hasFiles)
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.9f, 0.95f, 0.9f));
                EditorGUI.LabelField(
                    dragAreaRect,
                    string.Format(Get("file_count"), currentPaths.Count),
                    EditorStyles.objectFieldThumb
                );
            }
            else
            {
                EditorGUI.DrawRect(dragAreaRect, new Color(0.95f, 0.95f, 0.95f));
                EditorGUI.LabelField(
                    dragAreaRect,
                    string.Format(Get("file_not_selected_multi"), label),
                    EditorStyles.objectFieldThumb
                );
            }

            HandleVmdDragAndDrop(dragAreaRect, ref currentPaths, true);

            if (GUILayout.Button(addButtonText, GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel(
                    string.Format(Get("select_camera_vmd")),
                    Application.dataPath,
                    "vmd"
                );
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
            EditorGUILayout.LabelField(Get(SECTION_NAMING), EditorStyles.miniBoldLabel);
            var oldBaseName = bundleBaseName;
            bundleBaseName = EditorGUILayout.TextField(Get(BASE_NAME), bundleBaseName);

            if (!string.IsNullOrEmpty(bundleBaseName) && oldBaseName != bundleBaseName)
            {
                newClipName = bundleBaseName;
                controllerName = bundleBaseName;
            }

            if (GUILayout.Button(Get(BTN_AUTO_NAME), GUILayout.Width(100)))
            {
                AutoNameResources();
            }
            EditorGUILayout.Space();
        }

        private void DrawModelSettings()
        {
            EditorGUILayout.LabelField(Get(SECTION_MODEL), EditorStyles.miniBoldLabel);
            directMappingMode = EditorGUILayout.Toggle(Get(DIRECT_MAPPING), directMappingMode);

            if (directMappingMode)
            {
                EditorGUILayout.HelpBox(Get(HELP_DIRECT_MAPPING), MessageType.Info);

                showSkinnedMeshOptions = EditorGUILayout.Foldout(
                    showSkinnedMeshOptions,
                    Get(SKINNED_MESH_PATH_SETTINGS)
                );
                if (showSkinnedMeshOptions)
                {
                    EditorGUILayout.BeginVertical();
                    defaultSkinnedMeshPath = EditorGUILayout.TextField(
                        Get(SKINNED_MESH_PATH),
                        defaultSkinnedMeshPath
                    );
                    defaultSkinnedMeshName = EditorGUILayout.TextField(
                        Get(COMPONENT_NAME),
                        defaultSkinnedMeshName
                    );
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(Get(HELP_NON_DIRECT_MAPPING), MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (!directMappingMode)
            {
                targetModel = (GameObject)EditorGUILayout.ObjectField(
                    Get(TARGET_MODEL),
                    targetModel,
                    typeof(GameObject),
                    true
                );

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

            if (GUILayout.Button(Get(BTN_RESET), GUILayout.Width(60)))
            {
                targetModel = null;
                ResetModelState();
                if (directMappingMode && IsMorphVmdDataReady())
                {
                    MorphUtils.InitializeDirectMorphMapping(
                        vmdMorphFrames,
                        directMappingMode,
                        morphMapping,
                        availableMorphs,
                        selectedMorphs
                    );
                }
            }

            EditorGUILayout.EndHorizontal();
            ShowMorphStatistics();
            EditorGUILayout.Space();
        }

        private void DrawOutputSettings()
        {
            EditorGUILayout.LabelField(Get(SECTION_OUTPUT), EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(Get(ANIMATION_CURVE_OPTIONS), EditorStyles.miniBoldLabel);

            addMorphCurves = EditorGUILayout.Toggle(Get(ADD_MORPH_CURVES), addMorphCurves);
            addCameraCurves = EditorGUILayout.Toggle(Get(ADD_CAMERA_CURVES), addCameraCurves);

            EditorGUILayout.BeginHorizontal();
            if (addMorphCurves && addCameraCurves)
            {
                EditorGUILayout.HelpBox(Get(HELP_MERGE_MORPH_CAMERA), MessageType.Info);
            }
            else if (addMorphCurves)
            {
                EditorGUILayout.HelpBox(Get(HELP_MERGE_MORPH), MessageType.Info);
            }
            else if (addCameraCurves)
            {
                EditorGUILayout.HelpBox(Get(HELP_MERGE_CAMERA), MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(Get(HELP_SELECT_CURVE_TYPE), MessageType.Warning);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        private void DrawMorphMappingSettings()
        {
            showMappingOptions = EditorGUILayout.Foldout(
                showMappingOptions,
                Get(MORPH_MAPPING_SETTINGS)
            );

            if (showMappingOptions && availableMorphs.Count > 0 && IsMorphVmdDataReady())
            {
                allMorphsScrollPos = EditorGUILayout.BeginScrollView(allMorphsScrollPos, GUILayout.Height(300));
                EditorGUILayout.LabelField(Get(MORPH_MAPPING_INSTRUCTION1), EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(Get(MORPH_MAPPING_INSTRUCTION2), EditorStyles.miniLabel);

                // 批量操作按钮
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(Get(BTN_SELECT_ALL)))
                    MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, true);
                if (GUILayout.Button(Get(BTN_SELECT_FIRST_20)))
                    MorphUtils.SelectFirstNMorphs(availableMorphs, selectedMorphs, 20);
                if (GUILayout.Button(Get(BTN_DESELECT_ALL)))
                    MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, false);
                EditorGUILayout.EndHorizontal();

                // 获取VMD中的所有唯一形态键名称
                var vmdMorphNames = vmdMorphFrames.Select(f => f.MorphName).Distinct().ToList();

                foreach (var vmdMorph in vmdMorphNames)
                {
                    EditorGUILayout.BeginHorizontal();

                    bool isSelected = selectedMorphs.TryGetValue(vmdMorph, out bool selectedValue)
                        ? selectedValue
                        : false;

                    EditorGUI.BeginChangeCheck();
                    isSelected = EditorGUILayout.ToggleLeft("", isSelected, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedMorphs[vmdMorph] = isSelected;
                    }

                    EditorGUILayout.LabelField(vmdMorph, GUILayout.Width(150));

                    string currentMapping = morphMapping.TryGetValue(vmdMorph, out string mapValue)
                        ? mapValue
                        : ModelUtils.GetMappedMorphName(
                            vmdMorph,
                            morphMapping,
                            vrmBlendShapeMapping,
                            availableMorphs
                        );

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
                EditorGUILayout.HelpBox(Get(HELP_NO_MORPH_DATA), MessageType.Info);
            }
            EditorGUILayout.Space();
        }

        private void DrawActionButtons()
        {
            GUI.enabled = CanProcessAnimation();
            if (GUILayout.Button(Get(BTN_PROCESS), GUILayout.Height(30)))
            {
                ProcessAnimationAndController();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
        }

        private void DrawAudioSettings()
        {
            EditorGUILayout.LabelField(Get(SECTION_AUDIO), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Get(AUDIO_FILE), GUILayout.Width(EditorGUIUtility.labelWidth));

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
                EditorGUILayout.LabelField(Get(AUDIO_NOT_SELECTED), EditorStyles.objectFieldThumb);
            }

            if (GUILayout.Button(Get(BTN_BROWSE), GUILayout.Width(80)))
            {
                BrowseAudioFile();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }
        private void DrawTimelinePreview()
        {
            showTimelinePreview = EditorGUILayout.Foldout(showTimelinePreview, Get(SECTION_TIMELINE), true);

            if (showTimelinePreview)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField(Get(CHARACTER_MODEL), EditorStyles.boldLabel);
                characterModel = EditorGUILayout.ObjectField(
                    Get(DRAG_MODEL_HERE),
                    characterModel,
                    typeof(GameObject),
                    true) as GameObject;

                EditorGUILayout.Space();

                GUI.enabled = characterModel != null &&
                             !string.IsNullOrEmpty(bundleBaseName) &&
                             Directory.Exists(DefaultOutputPath);

                if (GUILayout.Button(Get(BTN_CREATE_TIMELINE)))
                {
                    CreateTimelinePreview();
                }

                if (!GUI.enabled)
                {
                    string disabledReason = "";
                    if (characterModel == null)
                        disabledReason = Get(HELP_SPECIFY_MODEL);
                    else if (string.IsNullOrEmpty(bundleBaseName))
                        disabledReason = Get(HELP_SET_BASE_NAME);
                    else if (!Directory.Exists(DefaultOutputPath))
                        disabledReason = Get(HELP_GENERATE_RESOURCES);

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
            EditorGUILayout.LabelField(Get(SECTION_BUNDLE), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(Get(HELP_PREVIEW_FIRST), MessageType.Info);
            EditorGUILayout.HelpBox(Get(HELP_ADJUST_POSE), MessageType.Info);
            EditorGUILayout.LabelField(Get(AUTO_BUILD_ADVANCED), EditorStyles.miniBoldLabel);

            showBundleOptions = EditorGUILayout.Foldout(showBundleOptions, Get(BUNDLE_ADVANCED_OPTIONS));
            if (showBundleOptions)
            {
                bundleOptions = (BuildAssetBundleOptions)EditorGUILayout.EnumFlagsField(
                    Get(BUNDLE_OPTIONS),
                    bundleOptions
                );
                EditorGUILayout.HelpBox(Get(HELP_BUNDLE_OPTIONS), MessageType.Info);
            }

            if (string.IsNullOrEmpty(bundleOutputPath))
            {
                bundleOutputPath = outputPath;
            }
            bundleOutputPath = EditorGUILayout.TextField(Get(AUTO_BUILD_OUTPUT_PATH), bundleOutputPath);
            if (GUILayout.Button(Get(BTN_SELECT_OUTPUT_PATH), GUILayout.Width(120)))
            {
                bundleOutputPath = SelectBundleOutputPath(bundleOutputPath);
            }

            if (!string.IsNullOrEmpty(bundleOutputPath))
            {
                bool isInProject = bundleOutputPath.StartsWith(Application.dataPath) ||
                                  bundleOutputPath.StartsWith("Assets/");
                EditorGUILayout.HelpBox(
                    isInProject
                        ? string.Format(Get("help_path_in_project"), bundleOutputPath)
                        : string.Format(Get("help_path_outside_project"), bundleOutputPath),
                    MessageType.Info
                );
            }

            DrawBundleAssetsPreview(outputPath);

            GUI.enabled = CanBuildBundle(bundleOutputPath);
            if (GUILayout.Button(Get(BTN_AUTO_BUILD), GUILayout.Height(30)))
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

            EditorGUILayout.HelpBox(Get(HELP_MANUAL_BUILD), MessageType.Info);
            EditorGUILayout.Space();
        }

        #endregion

        #region 新增和修改的核心方法

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
                EditorUtility.DisplayDialog(
                    Get(DIALOG_ERROR),
                    string.Format(Get("msg_files_not_exist"), Get(CAMERA_VMD_FILE)),
                    Get(DIALOG_CONFIRM)
                );
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
                    EditorUtility.DisplayProgressBar(
                        Get("progress_parsing_camera"),
                        string.Format(Get("progress_parsing_file"), Path.GetFileName(filePath), currentFile, totalFiles),
                        (float)currentFile / totalFiles
                    );

                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var parsedVmd = VMDParser.ParseVMD(stream);
                        parsedCameraVmds.Add(parsedVmd);
                        vmdCameraFrames.AddRange(parsedVmd.Cameras);
                    }
                }

                vmdCameraFrames = vmdCameraFrames.OrderBy(f => f.FrameIndex).ToList();
                cameraVmdParsed = true;
                Debug.Log(string.Format(
                    Get("log_parse_success_frames"),
                    totalFiles,
                    Get(CAMERA_VMD_FILE),
                    vmdCameraFrames.Count,
                    "镜头"
                ));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    Get(DIALOG_ERROR),
                    string.Format(Get("msg_parse_error"), Get(CAMERA_VMD_FILE), e.Message),
                    Get(DIALOG_CONFIRM)
                );
                Debug.LogError(string.Format(Get("log_parse_error"), "镜头", e));
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
                EditorUtility.DisplayDialog(Get(DIALOG_ERROR), $"解析表情VMD文件时出错: {e.Message}", "确定");
                Debug.LogError($"表情VMD解析错误: {e}");
                morphVmdParsed = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ProcessAnimationAndController()
        {
            if (!addMorphCurves && !addCameraCurves)
            {
                EditorUtility.DisplayDialog(
                    Get(DIALOG_INFO),
                    Get(HELP_SELECT_CURVE_TYPE),
                    Get(DIALOG_CONFIRM)
                );
                return;
            }

            try
            {
                var baseClip = AnimUtils.CreateOriginalAnimationClip(sourceClip, bundleBaseName, DefaultFrameRate);
                if (baseClip == null)
                {
                    EditorUtility.DisplayDialog(
                        Get(DIALOG_ERROR),
                        Get("msg_no_original_clip"),
                        Get(DIALOG_CONFIRM)
                    );
                    return;
                }

                if (addMorphCurves && IsMorphVmdDataReady())
                {
                    baseClip = directMappingMode
                        ? AnimUtils.AddMorphCurvesDirectMode(
                            baseClip,
                            vmdMorphFrames,
                            selectedMorphs,
                            morphMapping,
                            defaultSkinnedMeshPath
                        )
                        : AnimUtils.AddMorphCurvesToAnimation(
                            baseClip,
                            vmdMorphFrames,
                            selectedMorphs,
                            morphMapping,
                            targetModel,
                            bodyRenderer
                        );
                }

                if (addCameraCurves && cameraVmdParsed)
                {
                    foreach (var cameraVmdFilePath in cameraVmdFilePaths)
                    {
                        baseClip = AnimUtils.AddCameraCurvesToClip(
                            baseClip,
                            cameraVmdFilePath,
                            cameraRootPath,
                            cameraDistancePath,
                            cameraComponentPath,
                            cameraScale
                        );
                    }
                }

                AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(baseClip);
                clipSettings.loopTime = false;
                clipSettings.loopBlendOrientation = true;
                clipSettings.loopBlendPositionY = true;
                clipSettings.loopBlendPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(baseClip, clipSettings);

                string clipPath = $"{outputPath}{bundleBaseName}.anim";
                AssetDatabase.CreateAsset(baseClip, clipPath);

                AnimatorController controller = AssetUtils.CreateControllerForClip(
                    baseClip,
                    "",
                    outputPath,
                    bundleBaseName
                );

                string successMessage = string.Format(Get("msg_anim_created"), baseClip.name);
                if (controller != null)
                {
                    successMessage += "\n" + string.Format(Get("msg_controller_created"), controller.name);
                }

                EditorUtility.DisplayDialog(
                    Get(DIALOG_SUCCESS),
                    successMessage,
                    Get(DIALOG_CONFIRM)
                );
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    Get(DIALOG_ERROR),
                    string.Format(Get("msg_process_error"), e.Message),
                    Get(DIALOG_CONFIRM)
                );
                Debug.LogError(string.Format(Get("log_anim_process_error"), e));
            }
        }

        #endregion

        #region 辅助方法和状态管理

        private void BrowseAudioFile()
        {
            var path = EditorUtility.OpenFilePanel(Get("select_audio_file"), Application.dataPath, "wav,mp3,ogg");
            if (!string.IsNullOrEmpty(path))
            {
                audioFilePath = AssetUtils.GetProjectRelativePath(path);
            }
        }

        private string SelectBundleOutputPath(string currentPath)
        {
            var path = EditorUtility.OpenFolderPanel(Get("select_output_folder"),
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
            EditorGUILayout.LabelField(Get(ASSETS_TO_PACK), EditorStyles.miniBoldLabel);

            string clipName = $"{bundleBaseName}.anim";
            EditorGUILayout.LabelField(string.Format(Get(ASSET_ANIMATION), clipName), EditorStyles.miniLabel);
            string clipFullPath = Path.Combine(outputPath, clipName);
            if (!File.Exists(clipFullPath))
            {
                EditorGUILayout.HelpBox(
                    string.Format(Get("help_anim_not_exist"), clipName, outputPath),
                    MessageType.Warning
                );
            }

            string controllerName = $"{bundleBaseName}.controller";
            EditorGUILayout.LabelField(
                string.Format(Get(ASSET_CONTROLLER), controllerName),
                EditorStyles.miniLabel
            );
            string controllerFullPath = Path.Combine(outputPath, controllerName);
            if (!File.Exists(controllerFullPath))
            {
                EditorGUILayout.HelpBox(
                    string.Format(Get("help_controller_not_exist"), controllerName, outputPath),
                    MessageType.Warning
                );
            }

            if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
            {
                string audioName = Path.GetFileName(audioFilePath);
                EditorGUILayout.LabelField(
                    string.Format(Get(ASSET_AUDIO), audioName),
                    EditorStyles.miniLabel
                );
            }
            else
            {
                EditorGUILayout.LabelField(Get(ASSET_AUDIO_NONE), EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                string.Format(Get(BUNDLE_OUTPUT_INFO), bundleBaseName),
                EditorStyles.miniBoldLabel
            );
        }

        private void ShowMorphStatistics()
        {
            if (!IsMorphVmdDataReady()) return;

            var totalMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
            var matchedMorphs = vmdMorphFrames
                .Select(f => ModelUtils.GetMappedMorphName(
                    f.MorphName,
                    morphMapping,
                    vrmBlendShapeMapping,
                    availableMorphs
                ))
                .Distinct()
                .Count(n => availableMorphs.Contains(n));

            EditorGUILayout.LabelField(
                string.Format(Get(VMD_MORPH_COUNT), totalMorphs),
                EditorStyles.miniLabel
            );
            EditorGUILayout.LabelField(
                string.Format(Get(MATCHED_MORPH_COUNT), matchedMorphs),
                EditorStyles.miniLabel
            );

            var matchRate = totalMorphs > 0 ? (float)matchedMorphs / totalMorphs * 100 : 0;
            EditorGUILayout.LabelField(
                string.Format(Get(MATCH_RATE), matchRate),
                EditorStyles.miniLabel
            );

            if (matchedMorphs == 0 && !directMappingMode)
            {
                EditorGUILayout.HelpBox(Get(HELP_NO_MATCH), MessageType.Warning);
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