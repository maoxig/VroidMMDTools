// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using UnityEditor;
// using UnityEditor.Animations;
// using UnityEngine;
// using Object = UnityEngine.Object;
// using VMDPaser;
// using MMD;
// using AnimConverter.Editor.Utils;
// using VMD2Anim;

// namespace Assets.AnimConverter.Editor
// {
//     public class VmdMorphAnimatorTool : EditorWindow
//     {

//         private const string DefaultOutputPath = "Assets/AnimConverter/Output/";
//         private const string TempBuildFolder = "Assets/Temp/TempPureBuild/";
//         private const float DefaultFrameRate = 30f;

//         // 核心组件引用
//         private AnimationClip sourceClip;
//         private GameObject targetModel;
//         private SkinnedMeshRenderer bodyRenderer;

//         // VMD文件相关（按功能拆分）
//         // 1. 动画VMD
//         private string animVmdFilePath;
//         private VMD parsedAnimVmd;
//         private bool animVmdParsed = false;

//         // 2. 镜头VMD
//         private string cameraVmdFilePath;
//         private VMD parsedCameraVmd;
//         private List<VMDCameraFrame> vmdCameraFrames = new List<VMDCameraFrame>();
//         private bool cameraVmdParsed = false;

//         // 3. 表情VMD
//         private string morphVmdFilePath;
//         private VMD parsedMorphVmd;
//         private List<VMDMorphFrame> vmdMorphFrames = new List<VMDMorphFrame>();
//         private bool morphVmdParsed = false;

//         // 配置选项
//         private string outputPath = DefaultOutputPath;
//         private string newClipName = "NewMorphAnimation";
//         private string controllerName = "NewAnimatorController";

//         // 形态键管理
//         private List<string> availableMorphs = new List<string>();
//         private Dictionary<string, bool> selectedMorphs = new Dictionary<string, bool>();
//         private Dictionary<string, string> morphMapping = new Dictionary<string, string>(); // 形态键映射表

//         // VRM标准形态键映射表
//         private readonly Dictionary<string, string> vrmBlendShapeMapping = new Dictionary<string, string>
//         {
//             { "まばたき", "Blink" },
//             { "にこり", "Smile" },
//             { "悲しい", "Sorrow" },
//             { "驚き", "Angry" },
//             { "あ", "A" },
//             { "い", "I" },
//             { "う", "U" },
//             { "え", "E" },
//             { "お", "O" },
//             { "ウィンク", "Wink" },
//             { "のび", "Joy" },
//             { "びっくり", "Surprised" }
//         };

//         // 直接映射模式配置
//         private bool directMappingMode = true;
//         private string defaultSkinnedMeshPath = "Body";
//         private string defaultSkinnedMeshName = "Body";

//         // 相机动画配置
//         private bool showCameraAdvancedOptions = false;
//         private bool enableCameraAnimation = false;

//         // 相机路径配置
//         private string cameraRootPath = "Camera_root";  // 位移接受组件
//         private string cameraComponentPath = "Camera_root/Camera_root_1/Camera";// 主相机组件路径
//         private string cameraDistancePath = "Camera_root/Camera_root_1"; // Distance变换路径（接收距离动画）

//         // 音频配置
//         private string audioFilePath = "";

//         // 打包配置
//         private bool addMorphCurves = true; // 添加表情曲线
//         private bool addCameraCurves = false; // 添加镜头曲线

//         private string bundleBaseName = "character_animation";
//         private BuildAssetBundleOptions bundleOptions = BuildAssetBundleOptions.ChunkBasedCompression;
//         private bool showBundleOptions = false;
//         private string bundleOutputPath = "";

//         // 界面状态
//         private Vector2 mainScrollPos;
//         private Vector2 allMorphsScrollPos;
//         private bool showMappingOptions = false;

//         private bool hasAutoSearchedVmd = false;
//         private string lastSearchedClipName = "";

//         // 新增：配置管理器
//         private ToolConfigManager configManager;

//         // 动画提取模式
//         enum AnimExtractionMode { FromExistingClip, FromVmdFile }
//         AnimExtractionMode animExtractionMode = AnimExtractionMode.FromVmdFile;

//         // PMX辅助文件
//         private bool showPmxOptions = false;
//         private string pmxFilePath = "";

//         [MenuItem("MMD for Unity/VMD Morph Camera Animator Tool")]
//         public static void ShowWindow()
//         {
//             var window = GetWindow<VmdMorphAnimatorTool>("VMD Morph Animator Tool");
//             window.minSize = new Vector2(600, 800);
//             window.maxSize = new Vector2(600, 800);
//         }

//         private void OnGUI()
//         {
//             mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);
//             EditorGUILayout.LabelField("VMD Morph Animator Tool", EditorStyles.boldLabel);
//             EditorGUILayout.Space();

//             // 1. 动画提取部分
//             DrawAnimationExtractionSection();
//             DrawSeparator();

//             // 2. 镜头提取部分
//             DrawCameraExtractionSection();
//             DrawSeparator();

//             // 3. 表情提取部分
//             DrawMorphExtractionSection();
//             DrawSeparator();

//             // 原有其他设置保持不变
//             DrawModelSettings();
//             DrawMorphMappingSettings();
//             DrawSeparator();

//             DrawOutputSettings();
//             DrawNamingSettings();
//             DrawActionButtons();
//             DrawAudioSettings();
//             DrawSeparator();

//             DrawAssetBundleSettings();

//             EditorGUILayout.EndScrollView();
//         }

//         #region 新增：三个提取部分的UI和逻辑

//         private void DrawAnimationExtractionSection()
//         {
//             EditorGUILayout.LabelField("1. 动画提取", EditorStyles.boldLabel);
//             animExtractionMode = (AnimExtractionMode)EditorGUILayout.EnumPopup("动画来源", animExtractionMode);

//             if (animExtractionMode == AnimExtractionMode.FromExistingClip)
//             {
//                 EditorGUILayout.BeginHorizontal();
//                 sourceClip = (AnimationClip)EditorGUILayout.ObjectField(
//                     "已有动画剪辑", sourceClip, typeof(AnimationClip), false);

//                 if (GUILayout.Button("清空", GUILayout.Width(60)))
//                 {
//                     sourceClip = null;
//                     ResetAnimVmdState();
//                 }
//                 EditorGUILayout.EndHorizontal();

//                 // 自动查找关联VMD（如果需要）
//                 if (string.IsNullOrEmpty(animVmdFilePath) && sourceClip != null &&
//                     (!hasAutoSearchedVmd || lastSearchedClipName != sourceClip.name))
//                 {
//                     hasAutoSearchedVmd = true;
//                     lastSearchedClipName = sourceClip.name;
//                     TryFindAnimVmdFile();
//                     AutoNameResources();
//                 }
//             }
//             else // FromVmdFile
//             {
//                 EditorGUILayout.BeginHorizontal();
//                 EditorGUILayout.LabelField("动画VMD文件", GUILayout.Width(EditorGUIUtility.labelWidth));

//                 if (!string.IsNullOrEmpty(animVmdFilePath) && File.Exists(animVmdFilePath))
//                 {
//                     EditorGUILayout.LabelField(Path.GetFileName(animVmdFilePath), EditorStyles.objectFieldThumb);
//                 }
//                 else
//                 {
//                     var vmdRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));
//                     EditorGUI.DrawRect(vmdRect, new Color(0.95f, 0.95f, 0.95f));
//                     EditorGUI.LabelField(vmdRect, "未选择动画VMD文件 (可拖拽)", EditorStyles.objectFieldThumb);

//                     // 拖拽支持
//                     if (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
//                     {
//                         if (vmdRect.Contains(Event.current.mousePosition))
//                         {
//                             DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//                             if (Event.current.type == EventType.DragPerform)
//                             {
//                                 DragAndDrop.AcceptDrag();
//                                 var vmdPath = DragAndDrop.paths.FirstOrDefault(p => p.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase));
//                                 if (!string.IsNullOrEmpty(vmdPath))
//                                 {
//                                     animVmdFilePath = vmdPath;
//                                     Event.current.Use();
//                                 }
//                             }
//                         }
//                     }
//                 }

//                 if (GUILayout.Button("浏览...", GUILayout.Width(80)))
//                 {
//                     BrowseAnimVmdFile();
//                 }
//                 if (GUILayout.Button("清空", GUILayout.Width(60)))
//                 {
//                     ResetAnimVmdState();
//                 }
//                 EditorGUILayout.EndHorizontal();

//                 // PMX辅助选项
//                 showPmxOptions = EditorGUILayout.Foldout(showPmxOptions, "使用PMX模型辅助转换（可选）");
//                 if (showPmxOptions)
//                 {
//                     EditorGUILayout.BeginHorizontal();
//                     EditorGUILayout.LabelField("PMX文件", GUILayout.Width(EditorGUIUtility.labelWidth));
//                     if (!string.IsNullOrEmpty(pmxFilePath) && File.Exists(pmxFilePath))
//                     {
//                         EditorGUILayout.LabelField(Path.GetFileName(pmxFilePath), EditorStyles.objectFieldThumb);
//                     }
//                     else
//                     {
//                         EditorGUILayout.LabelField("未选择PMX文件", EditorStyles.objectFieldThumb);
//                     }
//                     if (GUILayout.Button("浏览...", GUILayout.Width(80)))
//                     {
//                         var path = EditorUtility.OpenFilePanel("选择PMX文件", Application.dataPath, "pmx");
//                         if (!string.IsNullOrEmpty(path) && path.EndsWith(".pmx"))
//                         {
//                             pmxFilePath = path;
//                         }
//                     }
//                     EditorGUILayout.EndHorizontal();
//                 }



//                 // 生成动画按钮
//                 if (!string.IsNullOrEmpty(animVmdFilePath) && File.Exists(animVmdFilePath))
//                 {
//                     string animOutputDir = outputPath;
//                     AssetUtils.EnsureDirectoryExists(animOutputDir);

//                     string animFileName = Path.GetFileNameWithoutExtension(animVmdFilePath) + ".anim";
//                     string animFullPath = Path.Combine(animOutputDir, animFileName);

//                     if (GUILayout.Button("从VMD生成动画剪辑"))
//                     {
//                         bool result = false;
//                         try
//                         {
//                             result = VMD2Anim.VMDConverter.ConvertVMD(
//                                 animVmdFilePath,
//                                 showPmxOptions && !string.IsNullOrEmpty(pmxFilePath) ? pmxFilePath : null,
//                                 animOutputDir,
//                                 (progress, msg) => { EditorUtility.DisplayProgressBar("VMD转换", msg, progress); },
//                                 overwrite: true
//                             );
//                         }
//                         finally
//                         {
//                             EditorUtility.ClearProgressBar();
//                         }

//                         if (result && File.Exists(animFullPath))
//                         {
//                             sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetUtils.GetProjectRelativePath(animFullPath));
//                             EditorUtility.DisplayDialog("成功", $"已生成动画剪辑: {animFileName}", "确定");
//                             AutoNameResources();
//                             animVmdParsed = true;
//                         }
//                         else
//                         {
//                             EditorUtility.DisplayDialog("失败", "VMD转换为动画失败", "确定");
//                         }
//                     }
//                 }
//             }

//             // 解析状态显示
//             if (animVmdParsed)
//             {
//                 EditorGUILayout.LabelField("✓ 动画VMD已解析", EditorStyles.miniLabel);
//             }
//             else if (!string.IsNullOrEmpty(animVmdFilePath) && File.Exists(animVmdFilePath))
//             {
//                 if (GUILayout.Button("解析动画VMD文件"))
//                 {
//                     ParseAnimVmdFile();
//                 }
//             }
//             EditorGUILayout.Space();
//         }

//         private void DrawCameraExtractionSection()
//         {
//             EditorGUILayout.LabelField("2. 镜头提取", EditorStyles.boldLabel);
//             enableCameraAnimation = EditorGUILayout.Toggle("启用镜头动画", enableCameraAnimation);

//             if (enableCameraAnimation)
//             {
//                 EditorGUILayout.BeginHorizontal();
//                 EditorGUILayout.LabelField("镜头VMD文件", GUILayout.Width(EditorGUIUtility.labelWidth));

//                 // 替换为拖拽区域逻辑
//                 if (!string.IsNullOrEmpty(cameraVmdFilePath) && File.Exists(cameraVmdFilePath))
//                 {
//                     EditorGUILayout.LabelField(Path.GetFileName(cameraVmdFilePath), EditorStyles.objectFieldThumb);
//                 }
//                 else
//                 {
//                     var cameraVmdRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));
//                     EditorGUI.DrawRect(cameraVmdRect, new Color(0.95f, 0.95f, 0.95f));
//                     EditorGUI.LabelField(cameraVmdRect, "未选择镜头VMD文件 (可拖拽)", EditorStyles.objectFieldThumb);

//                     // 拖拽逻辑
//                     if (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
//                     {
//                         if (cameraVmdRect.Contains(Event.current.mousePosition))
//                         {
//                             DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//                             if (Event.current.type == EventType.DragPerform)
//                             {
//                                 DragAndDrop.AcceptDrag();
//                                 var vmdPath = DragAndDrop.paths.FirstOrDefault(p => p.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase));
//                                 if (!string.IsNullOrEmpty(vmdPath))
//                                 {
//                                     cameraVmdFilePath = vmdPath;
//                                     Event.current.Use();
//                                 }
//                             }
//                         }
//                     }
//                 }

//                 if (GUILayout.Button("浏览...", GUILayout.Width(80)))
//                 {
//                     BrowseCameraVmdFile();
//                 }
//                 if (GUILayout.Button("清空", GUILayout.Width(60)))
//                 {
//                     ResetCameraVmdState();
//                 }
//                 EditorGUILayout.EndHorizontal();

//                 showCameraAdvancedOptions = EditorGUILayout.Foldout(showCameraAdvancedOptions, "镜头路径配置");
//                 if (showCameraAdvancedOptions)
//                 {
//                     cameraRootPath = EditorGUILayout.TextField("相机位移接收路径", cameraRootPath);
//                     cameraDistancePath = EditorGUILayout.TextField("Distance父对象路径", cameraDistancePath);
//                     cameraComponentPath = EditorGUILayout.TextField("相机组件完整路径", cameraComponentPath);
//                 }

//                 // 解析按钮和状态
//                 if (!string.IsNullOrEmpty(cameraVmdFilePath) && File.Exists(cameraVmdFilePath) && !cameraVmdParsed)
//                 {
//                     if (GUILayout.Button("解析镜头VMD文件"))
//                     {
//                         ParseCameraVmdFile();
//                     }
//                 }

//                 if (cameraVmdParsed)
//                 {
//                     EditorGUILayout.LabelField($"✓ 已解析 {vmdCameraFrames.Count} 个镜头帧", EditorStyles.miniLabel);

//                 }
//             }
//             EditorGUILayout.Space();
//         }

//         private void DrawMorphExtractionSection()
//         {
//             EditorGUILayout.LabelField("3. 表情提取", EditorStyles.boldLabel);

//             EditorGUILayout.BeginHorizontal();
//             EditorGUILayout.LabelField("表情VMD文件", GUILayout.Width(EditorGUIUtility.labelWidth));

//             // 替换为拖拽区域逻辑
//             if (!string.IsNullOrEmpty(morphVmdFilePath) && File.Exists(morphVmdFilePath))
//             {
//                 EditorGUILayout.LabelField(Path.GetFileName(morphVmdFilePath), EditorStyles.objectFieldThumb);
//             }
//             else
//             {
//                 var morphVmdRect = GUILayoutUtility.GetRect(200, 40, GUILayout.ExpandWidth(true));
//                 EditorGUI.DrawRect(morphVmdRect, new Color(0.95f, 0.95f, 0.95f));
//                 EditorGUI.LabelField(morphVmdRect, "未选择表情VMD文件 (可拖拽)", EditorStyles.objectFieldThumb);

//                 // 拖拽逻辑
//                 if (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
//                 {
//                     if (morphVmdRect.Contains(Event.current.mousePosition))
//                     {
//                         DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
//                         if (Event.current.type == EventType.DragPerform)
//                         {
//                             DragAndDrop.AcceptDrag();
//                             var vmdPath = DragAndDrop.paths.FirstOrDefault(p => p.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase));
//                             if (!string.IsNullOrEmpty(vmdPath))
//                             {
//                                 morphVmdFilePath = vmdPath;
//                                 Event.current.Use();
//                             }
//                         }
//                     }
//                 }
//             }

//             if (GUILayout.Button("浏览...", GUILayout.Width(80)))
//             {
//                 BrowseMorphVmdFile();
//             }
//             if (GUILayout.Button("清空", GUILayout.Width(60)))
//             {
//                 ResetMorphVmdState();
//             }
//             EditorGUILayout.EndHorizontal();

//             // 解析按钮和状态
//             if (!string.IsNullOrEmpty(morphVmdFilePath) && File.Exists(morphVmdFilePath) && !morphVmdParsed)
//             {
//                 if (GUILayout.Button("解析表情VMD文件"))
//                 {
//                     ParseMorphVmdFile();


//                     // 初始化映射
//                     if (directMappingMode && IsMorphVmdDataReady())
//                     {
//                         MorphUtils.InitializeDirectMorphMapping(vmdMorphFrames, directMappingMode, morphMapping, availableMorphs, selectedMorphs);
//                     }
//                 }
//             }

//             if (morphVmdParsed)
//             {
//                 var uniqueMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
//                 EditorGUILayout.LabelField($"✓ 已解析 {vmdMorphFrames.Count} 个表情帧，包含 {uniqueMorphs} 种表情", EditorStyles.miniLabel);

//             }
//             EditorGUILayout.Space();
//         }

//         #endregion

//         #region 原有UI方法（保持不变或微调）

//         private void DrawSeparator()
//         {
//             EditorGUILayout.Space();
//             Rect rect = GUILayoutUtility.GetRect(1f, 1.5f);
//             rect.width = EditorGUIUtility.currentViewWidth - 20f;
//             rect.x += 10f;
//             EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
//             EditorGUILayout.Space();
//         }

//         private void DrawNamingSettings()
//         {
//             EditorGUILayout.LabelField("统一资源命名设置", EditorStyles.miniBoldLabel);
//             var oldBaseName = bundleBaseName;
//             bundleBaseName = EditorGUILayout.TextField("基础名称", bundleBaseName);

//             if (!string.IsNullOrEmpty(bundleBaseName) && oldBaseName != bundleBaseName)
//             {
//                 newClipName = bundleBaseName;
//                 controllerName = bundleBaseName;
//             }

//             if (GUILayout.Button("自动命名", GUILayout.Width(100)))
//             {
//                 AutoNameResources();
//             }
//             EditorGUILayout.Space();
//         }

//         private void DrawModelSettings()
//         {
//             EditorGUILayout.LabelField("模型表情设置", EditorStyles.miniBoldLabel);
//             directMappingMode = EditorGUILayout.Toggle("直接映射", directMappingMode);

//             if (directMappingMode)
//             {
//                 EditorGUILayout.HelpBox("直接映射模式将直接使用VMD中的表情写入到对应路径的动画里，无需关联模型", MessageType.Info);
//                 EditorGUILayout.BeginVertical();
//                 defaultSkinnedMeshPath = EditorGUILayout.TextField("SkinnedMeshRenderer路径", defaultSkinnedMeshPath);
//                 defaultSkinnedMeshName = EditorGUILayout.TextField("组件名称", defaultSkinnedMeshName);
//                 EditorGUILayout.EndVertical();
//             }
//             else
//             {
//                 EditorGUILayout.HelpBox("非直接映射模式需要关联目标模型", MessageType.Info);
//             }

//             EditorGUILayout.BeginHorizontal();
//             if (directMappingMode)
//             {
//                 GUI.enabled = false;
//                 targetModel = (GameObject)EditorGUILayout.ObjectField(
//                     "目标模型（直接映射下禁用）", targetModel, typeof(GameObject), true);
//                 GUI.enabled = true;
//             }
//             else
//             {
//                 targetModel = (GameObject)EditorGUILayout.ObjectField(
//                     "目标模型", targetModel, typeof(GameObject), true);
//                 // 添加判断，避免重复初始化
//                 if (targetModel != null)
//                 {
//                     bodyRenderer = ModelUtils.UpdateModelComponents(
//                         targetModel,
//                         bodyRenderer,
//                         availableMorphs,
//                         selectedMorphs,
//                         directMappingMode,
//                         vmdMorphFrames,
//                         morphMapping,
//                         vrmBlendShapeMapping,
//                         IsMorphVmdDataReady
//                     );
//                 }
//             }

//             if (GUILayout.Button("重置", GUILayout.Width(60)))
//             {
//                 targetModel = null;
//                 ResetModelState();
//                 if (directMappingMode && IsMorphVmdDataReady())
//                 {
//                     MorphUtils.InitializeDirectMorphMapping(vmdMorphFrames, directMappingMode, morphMapping, availableMorphs, selectedMorphs);
//                 }
//             }

//             EditorGUILayout.EndHorizontal();
//             ShowMorphStatistics();
//             EditorGUILayout.Space();
//         }

//         private void DrawOutputSettings()
//         {
//             EditorGUILayout.LabelField("输出设置", EditorStyles.miniBoldLabel);

//             EditorGUILayout.LabelField("动画曲线添加选项", EditorStyles.miniBoldLabel);
//             addMorphCurves = EditorGUILayout.Toggle("添加表情曲线", addMorphCurves);
//             addCameraCurves = EditorGUILayout.Toggle("添加镜头曲线", addCameraCurves);

//             EditorGUILayout.BeginHorizontal();
//             if (addMorphCurves && addCameraCurves)
//             {
//                 EditorGUILayout.HelpBox("将原有动画与表情动画、镜头动画合并输出", MessageType.Info);
//             }
//             else if (addMorphCurves)
//             {
//                 EditorGUILayout.HelpBox("将原有动画与表情动画合并输出", MessageType.Info);
//             }
//             else if (addCameraCurves)
//             {
//                 EditorGUILayout.HelpBox("将原有动画与镜头动画合并输出", MessageType.Info);
//             }
//             else
//             {
//                 EditorGUILayout.HelpBox("请至少选择一种曲线类型添加", MessageType.Warning);
//             }
//             EditorGUILayout.EndHorizontal();

//             EditorGUILayout.Space();
//         }

//         private void DrawMorphMappingSettings()
//         {
//             showMappingOptions = EditorGUILayout.Foldout(showMappingOptions, "形态键选择与映射设置");
//             if (showMappingOptions && availableMorphs.Count > 0 && IsMorphVmdDataReady())
//             {
//                 allMorphsScrollPos = EditorGUILayout.BeginScrollView(allMorphsScrollPos, GUILayout.Height(300));
//                 EditorGUILayout.LabelField("选择需要使用的形态键并设置映射关系", EditorStyles.miniBoldLabel);
//                 EditorGUILayout.LabelField("（勾选启用，文本框填写映射目标名称）", EditorStyles.miniLabel);

//                 // 批量操作按钮
//                 EditorGUILayout.BeginHorizontal();
//                 if (GUILayout.Button("全选")) MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, true);
//                 if (GUILayout.Button("选择前20个")) MorphUtils.SelectFirstNMorphs(availableMorphs, selectedMorphs, 20);
//                 if (GUILayout.Button("取消全选")) MorphUtils.SelectAllMorphs(availableMorphs, selectedMorphs, false);
//                 EditorGUILayout.EndHorizontal();

//                 // 获取VMD中的所有唯一形态键名称
//                 var vmdMorphNames = vmdMorphFrames.Select(f => f.MorphName).Distinct().ToList();

//                 foreach (var vmdMorph in vmdMorphNames)
//                 {
//                     EditorGUILayout.BeginHorizontal();

//                     bool isSelected = selectedMorphs.TryGetValue(vmdMorph, out bool selectedValue) ? selectedValue : false;

//                     EditorGUI.BeginChangeCheck();
//                     isSelected = EditorGUILayout.ToggleLeft("", isSelected, GUILayout.Width(20));
//                     if (EditorGUI.EndChangeCheck())
//                     {
//                         selectedMorphs[vmdMorph] = isSelected;
//                     }

//                     EditorGUILayout.LabelField(vmdMorph, GUILayout.Width(150));

//                     string currentMapping = morphMapping.TryGetValue(vmdMorph, out string mapValue)
//                         ? mapValue
//                         : ModelUtils.GetMappedMorphName(vmdMorph, morphMapping, vrmBlendShapeMapping, availableMorphs);

//                     EditorGUI.BeginChangeCheck();
//                     currentMapping = EditorGUILayout.TextField(currentMapping);
//                     if (EditorGUI.EndChangeCheck())
//                     {
//                         morphMapping[vmdMorph] = currentMapping;
//                     }

//                     EditorGUILayout.EndHorizontal();
//                 }

//                 EditorGUILayout.EndScrollView();
//             }
//             else if (availableMorphs.Count == 0 && (targetModel != null || IsMorphVmdDataReady()))
//             {
//                 EditorGUILayout.HelpBox("未找到可用的形态键数据，请先解析表情VMD文件或关联模型", MessageType.Info);
//             }
//             EditorGUILayout.Space();
//         }

//         private void DrawActionButtons()
//         {
//             GUI.enabled = CanProcessAnimation();
//             if (GUILayout.Button("添加到动画并创建控制器", GUILayout.Height(30)))
//             {
//                 ProcessAnimationAndController();
//             }
//             GUI.enabled = true;

//             EditorGUILayout.Space();
//         }

//         private void DrawAudioSettings()
//         {
//             EditorGUILayout.LabelField("音频设置", EditorStyles.miniBoldLabel);
//             EditorGUILayout.BeginHorizontal();
//             EditorGUILayout.LabelField("音频文件", GUILayout.Width(EditorGUIUtility.labelWidth));

//             EditorGUI.BeginChangeCheck();
//             var projectRelativeAudioPath = AssetUtils.GetProjectRelativePath(audioFilePath);
//             var audioObj = !string.IsNullOrEmpty(projectRelativeAudioPath)
//                 ? AssetDatabase.LoadAssetAtPath<Object>(projectRelativeAudioPath)
//                 : null;

//             audioObj = EditorGUILayout.ObjectField(audioObj, typeof(AudioClip), false);

//             if (EditorGUI.EndChangeCheck())
//             {
//                 audioFilePath = audioObj != null ? AssetDatabase.GetAssetPath(audioObj) : "";
//             }

//             if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
//             {
//                 EditorGUILayout.LabelField(Path.GetFileName(audioFilePath), EditorStyles.objectFieldThumb);
//             }
//             else
//             {
//                 EditorGUILayout.LabelField("未选择音频文件", EditorStyles.objectFieldThumb);
//             }

//             if (GUILayout.Button("浏览...", GUILayout.Width(80)))
//             {
//                 BrowseAudioFile();
//             }
//             EditorGUILayout.EndHorizontal();
//             EditorGUILayout.Space();
//         }

//         private void DrawAssetBundleSettings()
//         {
//             EditorGUILayout.LabelField("资源打包设置", EditorStyles.boldLabel);
//             EditorGUILayout.HelpBox("打包前请先在Unity内预览，确保一切正常，并且确保音频轴对上动作轴", MessageType.Info);
//             EditorGUILayout.HelpBox("此外请在动画预览中检查人物朝向（红色箭头和蓝色箭头），也许需要调整Root Transform Rotation: Offset", MessageType.Info);
//             EditorGUILayout.LabelField("自动打包（高级）", EditorStyles.miniBoldLabel);

//             showBundleOptions = EditorGUILayout.Foldout(showBundleOptions, "打包高级选项");
//             if (showBundleOptions)
//             {
//                 bundleOptions = (BuildAssetBundleOptions)EditorGUILayout.EnumFlagsField("打包选项", bundleOptions);
//                 EditorGUILayout.HelpBox(
//                     "None: 基本打包\n" +
//                     "ChunkBasedCompression: 分块压缩\n" +
//                     "DeterministicAssetBundle: 确定性打包",
//                     MessageType.Info
//                 );
//             }

//             if (string.IsNullOrEmpty(bundleOutputPath))
//             {
//                 bundleOutputPath = outputPath;
//             }
//             bundleOutputPath = EditorGUILayout.TextField("自动打包输出路径", bundleOutputPath);
//             if (GUILayout.Button("选择输出路径", GUILayout.Width(120)))
//             {
//                 bundleOutputPath = SelectBundleOutputPath(bundleOutputPath);
//             }

//             if (!string.IsNullOrEmpty(bundleOutputPath))
//             {
//                 bool isInProject = bundleOutputPath.StartsWith(Application.dataPath) ||
//                                   bundleOutputPath.StartsWith("Assets/");
//                 EditorGUILayout.HelpBox(
//                     isInProject ? $"输出路径在项目内: {bundleOutputPath}" :
//                                   $"输出路径在项目外: {bundleOutputPath}",
//                     MessageType.Info
//                 );
//             }

//             DrawBundleAssetsPreview(outputPath);

//             GUI.enabled = CanBuildBundle(bundleOutputPath);
//             if (GUILayout.Button("📦 自动打包", GUILayout.Height(30)))
//             {
//                 AssetUtils.BuildAssetBundle(
//                     outputPath,
//                     TempBuildFolder,
//                     bundleBaseName,
//                     audioFilePath,
//                     bundleOutputPath,
//                     bundleOptions
//                 );
//             }
//             GUI.enabled = true;

//             EditorGUILayout.HelpBox("如果自动打包失败, 请手动构建文件", MessageType.Info);
//             EditorGUILayout.Space();
//         }

//         #endregion

//         #region 新增和修改的核心方法

//         private void BrowseAnimVmdFile()
//         {
//             var path = EditorUtility.OpenFilePanel("选择动画VMD文件", Application.dataPath, "vmd");
//             if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd"))
//             {
//                 animVmdFilePath = path;
//             }
//         }

//         private void BrowseCameraVmdFile()
//         {
//             var path = EditorUtility.OpenFilePanel("选择镜头VMD文件", Application.dataPath, "vmd");
//             if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd"))
//             {
//                 cameraVmdFilePath = path;
//             }
//         }

//         private void BrowseMorphVmdFile()
//         {
//             var path = EditorUtility.OpenFilePanel("选择表情VMD文件", Application.dataPath, "vmd");
//             if (!string.IsNullOrEmpty(path) && path.EndsWith(".vmd"))
//             {
//                 morphVmdFilePath = path;
//             }
//         }

//         private void ParseAnimVmdFile()
//         {
//             if (string.IsNullOrEmpty(animVmdFilePath) || !File.Exists(animVmdFilePath))
//             {
//                 EditorUtility.DisplayDialog("错误", "动画VMD文件不存在", "确定");
//                 return;
//             }

//             try
//             {
//                 using (var stream = new FileStream(animVmdFilePath, FileMode.Open, FileAccess.Read))
//                 {
//                     parsedAnimVmd = VMDParser.ParseVMD(stream);
//                     animVmdParsed = true;
//                     Debug.Log($"成功解析动画VMD文件: {Path.GetFileName(animVmdFilePath)}");
//                     // EditorUtility.DisplayDialog("成功", $"动画VMD解析完成: {Path.GetFileName(animVmdFilePath)}", "确定");
//                 }
//             }
//             catch (Exception e)
//             {
//                 EditorUtility.DisplayDialog("解析错误", $"解析动画VMD文件时出错: {e.Message}", "确定");
//                 Debug.LogError($"动画VMD解析错误: {e}");
//                 animVmdParsed = false;
//             }
//         }

//         private void ParseCameraVmdFile()
//         {
//             if (string.IsNullOrEmpty(cameraVmdFilePath) || !File.Exists(cameraVmdFilePath))
//             {
//                 EditorUtility.DisplayDialog("错误", "镜头VMD文件不存在", "确定");
//                 return;
//             }

//             try
//             {
//                 using (var stream = new FileStream(cameraVmdFilePath, FileMode.Open, FileAccess.Read))
//                 {
//                     parsedCameraVmd = VMDParser.ParseVMD(stream);
//                     vmdCameraFrames = parsedCameraVmd.Cameras;
//                     cameraVmdParsed = true;
//                     Debug.Log($"成功解析镜头VMD文件: {Path.GetFileName(cameraVmdFilePath)}，包含 {vmdCameraFrames.Count} 个镜头帧");
//                     // EditorUtility.DisplayDialog("成功", $"镜头VMD解析完成，找到 {vmdCameraFrames.Count} 个镜头帧", "确定");
//                 }
//             }
//             catch (Exception e)
//             {
//                 EditorUtility.DisplayDialog("解析错误", $"解析镜头VMD文件时出错: {e.Message}", "确定");
//                 Debug.LogError($"镜头VMD解析错误: {e}");
//                 cameraVmdParsed = false;
//             }
//         }

//         private void ParseMorphVmdFile()
//         {
//             if (string.IsNullOrEmpty(morphVmdFilePath) || !File.Exists(morphVmdFilePath))
//             {
//                 EditorUtility.DisplayDialog("错误", "表情VMD文件不存在", "确定");
//                 return;
//             }

//             try
//             {
//                 using (var stream = new FileStream(morphVmdFilePath, FileMode.Open, FileAccess.Read))
//                 {
//                     parsedMorphVmd = VMDParser.ParseVMD(stream);
//                     vmdMorphFrames = parsedMorphVmd.Morphs;
//                     morphVmdParsed = true;

//                     var uniqueMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
//                     Debug.Log($"成功解析表情VMD文件: {Path.GetFileName(morphVmdFilePath)}，包含 {vmdMorphFrames.Count} 个表情帧，{uniqueMorphs} 种表情");
//                     // EditorUtility.DisplayDialog("成功", $"表情VMD解析完成，找到 {vmdMorphFrames.Count} 个表情帧", "确定");

//                     // 初始化映射
//                     if (directMappingMode)
//                     {
//                         MorphUtils.InitializeDirectMorphMapping(vmdMorphFrames, directMappingMode, morphMapping, availableMorphs, selectedMorphs);
//                     }
//                 }
//             }
//             catch (Exception e)
//             {
//                 EditorUtility.DisplayDialog("解析错误", $"解析表情VMD文件时出错: {e.Message}", "确定");
//                 Debug.LogError($"表情VMD解析错误: {e}");
//                 morphVmdParsed = false;
//             }
//         }

//         private void TryFindAnimVmdFile()
//         {
//             try
//             {
//                 var clipName = sourceClip.name;
//                 if (clipName.Contains("@"))
//                 {
//                     clipName = clipName.Substring(clipName.IndexOf("@") + 1);
//                 }

//                 var possibleNames = new[] {
//                     clipName,
//                     clipName.Replace("_vmd", ""),
//                     clipName.Replace("_VMD", ""),
//                     $"{clipName}_vmd",
//                     $"{clipName}_VMD"
//                 };

//                 var vmdFiles = Directory.GetFiles(Application.dataPath, "*.vmd", SearchOption.AllDirectories);
//                 foreach (var possibleName in possibleNames)
//                 {
//                     animVmdFilePath = vmdFiles.FirstOrDefault(f =>
//                         Path.GetFileNameWithoutExtension(f) == possibleName);

//                     if (!string.IsNullOrEmpty(animVmdFilePath))
//                     {
//                         Debug.Log($"自动找到动画VMD文件: {animVmdFilePath}");
//                         if (!animVmdParsed)
//                             ParseAnimVmdFile();
//                         break;
//                     }
//                 }
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"自动查找VMD文件时出错: {e.Message}");
//             }
//         }

//         private void ProcessAnimationAndController()
//         {
//             if (!addMorphCurves && !addCameraCurves)
//             {
//                 EditorUtility.DisplayDialog("提示", "请至少选择一种曲线类型添加", "确定");
//                 return;
//             }

//             try
//             {
//                 // 1. 创建基础动画（复制原动画曲线）
//                 var baseClip = AnimUtils.CreateOriginalAnimationClip(sourceClip, bundleBaseName, DefaultFrameRate);
//                 if (baseClip == null)
//                 {
//                     EditorUtility.DisplayDialog("错误", "未找到原动画剪辑", "确定");
//                     return;
//                 }

//                 // 2. 根据选项添加表情曲线
//                 if (addMorphCurves && IsMorphVmdDataReady())
//                 {
//                     baseClip = directMappingMode
//                         ? AnimUtils.AddMorphCurvesDirectMode(baseClip, vmdMorphFrames, selectedMorphs, morphMapping, defaultSkinnedMeshPath)
//                         : AnimUtils.AddMorphCurvesToAnimation(baseClip, vmdMorphFrames, selectedMorphs, morphMapping, targetModel, bodyRenderer);
//                 }

//                 // 3. 根据选项添加镜头曲线
//                 if (addCameraCurves && cameraVmdParsed)
//                 {
//                     baseClip = AnimUtils.AddCameraCurvesToClip(baseClip, cameraVmdFilePath, cameraRootPath, cameraDistancePath, cameraComponentPath);
//                 }

//                 // 设置动画属性
//                 AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(baseClip);
//                 clipSettings.loopTime = false;
//                 clipSettings.loopBlendOrientation = true;
//                 clipSettings.loopBlendPositionY = true;
//                 clipSettings.loopBlendPositionXZ = true;
//                 AnimationUtility.SetAnimationClipSettings(baseClip, clipSettings);

//                 // 4. 保存动画剪辑
//                 string clipPath = $"{outputPath}{bundleBaseName}.anim";
//                 AssetDatabase.CreateAsset(baseClip, clipPath);

//                 // 5. 处理动画控制器
//                 AnimatorController controller = AssetUtils.CreateControllerForClip(baseClip, "", outputPath, bundleBaseName);

//                 EditorUtility.DisplayDialog("成功",
//                 $"已生成动画: {baseClip.name}\n" +
//                 (controller != null ? $"已生成控制器: {controller.name}" : ""),
//                 "确定");
//             }
//             catch (Exception e)
//             {
//                 EditorUtility.DisplayDialog("错误", $"处理动画时出错: {e.Message}", "确定");
//                 Debug.LogError($"动画处理错误: {e}");
//             }
//         }

//         #endregion

//         #region 辅助方法和状态管理

//         private void BrowseAudioFile()
//         {
//             var path = EditorUtility.OpenFilePanel("选择音频文件", Application.dataPath, "wav,mp3,ogg");
//             if (!string.IsNullOrEmpty(path))
//             {
//                 audioFilePath = AssetUtils.GetProjectRelativePath(path);
//             }
//         }

//         private string SelectBundleOutputPath(string currentPath)
//         {
//             var path = EditorUtility.OpenFolderPanel("选择输出文件夹",
//                 string.IsNullOrEmpty(currentPath) ? Application.dataPath : currentPath,
//                 "");

//             if (!string.IsNullOrEmpty(path))
//             {
//                 return path;
//             }

//             return currentPath;
//         }

//         private void DrawBundleAssetsPreview(string outputPath)
//         {
//             EditorGUILayout.Space();
//             EditorGUILayout.LabelField("将打包的资源:", EditorStyles.miniBoldLabel);

//             string clipName = $"{bundleBaseName}.anim";
//             EditorGUILayout.LabelField($"- 动画: {clipName}", EditorStyles.miniLabel);
//             string clipFullPath = Path.Combine(outputPath, clipName);
//             if (!File.Exists(clipFullPath))
//             {
//                 EditorGUILayout.HelpBox($"动画文件 {clipName} 不存在于输出: {outputPath}", MessageType.Warning);
//             }

//             string controllerName = $"{bundleBaseName}.controller";
//             EditorGUILayout.LabelField($"- 控制器: {controllerName}", EditorStyles.miniLabel);
//             string controllerFullPath = Path.Combine(outputPath, controllerName);
//             if (!File.Exists(controllerFullPath))
//             {
//                 EditorGUILayout.HelpBox($"控制器文件 {controllerName} 不存在于输出: {outputPath}", MessageType.Warning);
//             }

//             if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
//             {
//                 string audioName = Path.GetFileName(audioFilePath);
//                 EditorGUILayout.LabelField($"- 音频: {audioName}", EditorStyles.miniLabel);
//             }
//             else
//             {
//                 EditorGUILayout.LabelField("- 音频: 未选择", EditorStyles.miniLabel);
//             }

//             EditorGUILayout.LabelField("资源将被打包输出为: " + bundleBaseName + ".unity3d", EditorStyles.miniBoldLabel);
//         }

//         private void ShowMorphStatistics()
//         {
//             if (!IsMorphVmdDataReady()) return;

//             var totalMorphs = vmdMorphFrames.Select(f => f.MorphName).Distinct().Count();
//             var matchedMorphs = vmdMorphFrames
//                 .Select(f => ModelUtils.GetMappedMorphName(f.MorphName, morphMapping, vrmBlendShapeMapping, availableMorphs))
//                 .Distinct()
//                 .Count(n => availableMorphs.Contains(n));

//             EditorGUILayout.LabelField($"VMD表情总数: {totalMorphs} 个", EditorStyles.miniLabel);
//             EditorGUILayout.LabelField($"匹配到模型的表情: {matchedMorphs} 个", EditorStyles.miniLabel);

//             var matchRate = totalMorphs > 0 ? (float)matchedMorphs / totalMorphs * 100 : 0;
//             EditorGUILayout.LabelField($"匹配率: {matchRate:F1}%", EditorStyles.miniLabel);

//             if (matchedMorphs == 0 && !directMappingMode)
//             {
//                 EditorGUILayout.HelpBox("未找到匹配的表情数据，请检查形态键映射设置", MessageType.Warning);
//             }
//         }

//         private void AutoNameResources()
//         {
//             string baseName = "";

//             if (!string.IsNullOrEmpty(morphVmdFilePath))
//             {
//                 baseName = Path.GetFileNameWithoutExtension(morphVmdFilePath);
//             }
//             else if (!string.IsNullOrEmpty(animVmdFilePath))
//             {
//                 baseName = Path.GetFileNameWithoutExtension(animVmdFilePath);
//             }
//             else if (sourceClip != null)
//             {
//                 baseName = sourceClip.name;
//             }

//             if (!string.IsNullOrEmpty(baseName))
//             {
//                 bundleBaseName = baseName;
//                 newClipName = baseName;
//                 controllerName = baseName;
//             }
//         }

//         #endregion

//         #region 状态检查和重置

//         private bool CanProcessAnimation()
//         {
//             bool hasValidSource = sourceClip != null;
//             bool hasValidMorphData = !addMorphCurves || (IsMorphVmdDataReady() && selectedMorphs.Any(m => m.Value));
//             bool hasValidCameraData = !addCameraCurves || cameraVmdParsed;
//             bool hasValidModel = directMappingMode || (targetModel != null && bodyRenderer != null);

//             return hasValidSource && hasValidMorphData && hasValidCameraData && hasValidModel;
//         }

//         private bool CanBuildBundle(string outputPath)
//         {
//             return !string.IsNullOrEmpty(AssetUtils.GetAnimationPath(newClipName, outputPath, sourceClip)) &&
//                    !string.IsNullOrEmpty(AssetUtils.GetControllerPath(controllerName, outputPath)) &&
//                    !string.IsNullOrEmpty(outputPath) &&
//                    !string.IsNullOrEmpty(bundleBaseName);
//         }

//         private bool IsMorphVmdDataReady() => morphVmdParsed && vmdMorphFrames.Count > 0;
//         private bool IsCameraVmdDataReady() => cameraVmdParsed && vmdCameraFrames.Count > 0;
//         private bool IsAnimVmdDataReady() => animVmdParsed;

//         private bool IsModelDataReady() => !directMappingMode && targetModel != null && bodyRenderer != null;

//         private void ResetAnimVmdState()
//         {
//             animVmdFilePath = "";
//             animVmdParsed = false;
//             parsedAnimVmd = null;
//             hasAutoSearchedVmd = false;
//         }

//         private void ResetCameraVmdState()
//         {
//             cameraVmdFilePath = "";
//             cameraVmdParsed = false;
//             parsedCameraVmd = null;
//             vmdCameraFrames.Clear();
//         }

//         private void ResetMorphVmdState()
//         {
//             morphVmdFilePath = "";
//             morphVmdParsed = false;
//             parsedMorphVmd = null;
//             vmdMorphFrames.Clear();
//         }

//         private void ResetModelState()
//         {
//             bodyRenderer = null;
//             availableMorphs.Clear();
//             selectedMorphs.Clear();
//             morphMapping.Clear();
//             Repaint();
//         }

//         private void OnEnable()
//         {
//             configManager = new ToolConfigManager();
//             ApplyConfigToTool();
//         }

//         private void OnDisable()
//         {
//             SyncToolToConfig();
//             configManager.SaveConfig();
//         }

//         private void ApplyConfigToTool()
//         {
//             var config = configManager.Config;

//             defaultSkinnedMeshPath = config.defaultSkinnedMeshPath;
//             defaultSkinnedMeshName = config.defaultSkinnedMeshName;
//             directMappingMode = config.directMappingMode;
//             enableCameraAnimation = config.enableCameraAnimation;
//             cameraRootPath = config.cameraRootPath;
//             cameraComponentPath = config.cameraComponentPath;
//             cameraDistancePath = config.cameraDistancePath;
//             bundleOutputPath = config.bundleOutputPath;
//         }

//         private void SyncToolToConfig()
//         {
//             var config = configManager.Config;

//             config.defaultSkinnedMeshPath = defaultSkinnedMeshPath;
//             config.defaultSkinnedMeshName = defaultSkinnedMeshName;
//             config.directMappingMode = directMappingMode;
//             config.enableCameraAnimation = enableCameraAnimation;
//             config.cameraRootPath = cameraRootPath;
//             config.cameraComponentPath = cameraComponentPath;
//             config.cameraDistancePath = cameraDistancePath;
//             config.bundleOutputPath = bundleOutputPath;
//         }

//         #endregion
//     }
// }