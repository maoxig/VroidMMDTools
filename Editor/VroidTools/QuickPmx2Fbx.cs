#if USE_QUICK_PMX
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VroidMMDTools.VroidTools
{
    /// <summary>
    /// PMX/PMD转FBX快速工具（单文件版）
    /// 功能：选中单个PMX/PMD文件，通过菜单触发异步转换，复用原有配置文件
    /// </summary>
    public static class QuickPmx2Fbx
    {
        #region 配置与状态变量
        /// <summary>
        /// 转换配置（读取PMX2FBX工具路径等）
        /// </summary>
        private static ConversionSettings _settings;

        /// <summary>
        /// 转换状态锁（避免并发转换）
        /// </summary>
        private static bool _isConverting;

        /// <summary>
        /// 取消令牌源（用于取消转换任务）
        /// </summary>
        private static CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 进度条取消状态（主线程更新，避免线程安全问题）
        /// </summary>
        private static bool _progressCanceled;
        #endregion

        
        // #region 编辑器菜单注册（触发入口）
        // /// <summary>
        // /// 顶部菜单入口：MMD for Unity > PMXPMD转FBX
        // /// </summary>
        // [MenuItem("MMD for Unity/PMXPMD转FBX", false, 1)]
        // private static async void ConvertFromTopMenu()
        // {
        //     await ConvertSelectedSinglePmxFile();
        // }

        // /// <summary>
        // /// 右键菜单入口：Assets > MMD for Unity > PMXPMD转FBX
        // /// </summary>
        // [MenuItem("Assets/MMD for Unity/PMXPMD转FBX", false, 10)]
        // private static async void ConvertFromAssetsContextMenu()
        // {
        //     await ConvertSelectedSinglePmxFile();
        // }

        // /// <summary>
        // /// 右键菜单有效性控制（仅选中单个PMX/PMD时显示）
        // /// </summary>
        // [MenuItem("Assets/MMD for Unity/PMXPMD转FBX", true)]
        // private static bool ValidateAssetsContextMenu()
        // {
        //     // 仅当选中1个且是PMX/PMD时有效
        //     return GetSelectedSinglePmxPath() != null;
        // }
        // #endregion


        #region 公共API（外部调用接口）
        /// <summary>
        /// 公共API：转换单个PMX/PMD文件为FBX
        /// </summary>
        /// <param name="pmxUnityPath">PMX/PMD的Unity Assets相对路径（如：Assets/Models/model.pmx）</param>
        /// <param name="outputUnityPath">FBX输出的Unity Assets相对路径（默认与PMX同目录）</param>
        /// <param name="progressCallback">进度回调（参数1：进度0-1，参数2：状态消息）</param>
        /// <param name="overwriteExisting">是否覆盖已存在的FBX（默认读取配置）</param>
        /// <param name="timeoutMs">转换超时时间（毫秒，默认180秒）</param>
        /// <param name="cancellationToken">外部取消令牌</param>
        /// <returns>转换成功返回true，失败返回false</returns>
        public static async Task<bool> ConvertPmxToFbx(
            string pmxUnityPath,
            string outputUnityPath = null,
            Action<float, string> progressCallback = null,
            bool? overwriteExisting = null,
            int timeoutMs = 180000,
            CancellationToken cancellationToken = default)
        {
            // 初始化配置
            _settings = _settings ?? ConversionSettings.Load();

            // 处理默认参数
            string actualOutputPath = string.IsNullOrEmpty(outputUnityPath)
                ? Path.GetDirectoryName(pmxUnityPath)
                : outputUnityPath;
            bool actualOverwrite = overwriteExisting ?? _settings.OverwriteExisting;

            // 参数验证
            if (!ValidateConversionParams(pmxUnityPath, actualOutputPath, actualOverwrite))
            {
                progressCallback?.Invoke(0f, "参数验证失败，终止转换");
                return false;
            }

            try
            {
                // 调用内部核心转换逻辑
                return await ConvertPmxInternalAsync(
                    pmxUnityPath,
                    actualOutputPath,
                    progressCallback,
                    actualOverwrite,
                    timeoutMs,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke(0f, $"转换异常：{ex.Message}");
                Debug.LogError($"[PMX转FBX] 公共API调用失败：{ex.Message}");
                return false;
            }
        }
        #endregion


        #region 内部核心转换逻辑
        /// <summary>
        /// 处理选中的单个PMX文件转换（菜单触发核心入口）
        /// </summary>
        private static async Task ConvertSelectedSinglePmxFile()
        {
            // 检查是否有转换任务正在进行
            if (_isConverting)
            {
                ShowMainThreadDialog("提示", "已有转换任务正在执行，请等待完成后再试", "确定");
                return;
            }

            // 获取选中的单个PMX/PMD文件（仅处理第一个）
            string selectedPmxPath = GetSelectedSinglePmxPath();
            if (string.IsNullOrEmpty(selectedPmxPath))
            {
                ShowMainThreadDialog("提示", "请选中至少一个PMX或PMD文件", "确定");
                return;
            }

            // 初始化配置与取消令牌
            _settings = ConversionSettings.Load();
            _cancellationTokenSource = new CancellationTokenSource();
            _isConverting = true;
            _progressCanceled = false;

            try
            {
                string fileName = Path.GetFileName(selectedPmxPath);
                bool conversionResult = false;

                // 执行单文件转换（带主线程进度条反馈）
                conversionResult = await ConvertPmxToFbx(
                    pmxUnityPath: selectedPmxPath,
                    progressCallback: async (progress, message) =>
                    {
                        // 关键修复：通过delayCall将进度条操作切换到主线程
                        await RunOnMainThreadAsync(() =>
                        {
                            // 显示可取消进度条（主线程执行）
                            _progressCanceled = EditorUtility.DisplayCancelableProgressBar(
                                "PMX转FBX",
                                $"{fileName}：{message}",
                                progress);

                            // 用户点击取消时触发令牌
                            if (_progressCanceled)
                                _cancellationTokenSource.Cancel();
                        });
                    },
                    cancellationToken: _cancellationTokenSource.Token);

                // 转换完成后在主线程显示结果
                await RunOnMainThreadAsync(() =>
                {
                    string resultMsg = _progressCanceled
                        ? "转换已取消"
                        : conversionResult ? "转换完成！FBX文件已生成" : "转换失败";
                    ShowMainThreadDialog("结果", resultMsg, "确定");
                    Debug.Log($"[PMX转FBX] 转换结束：{resultMsg}（文件：{fileName}）");
                });
            }
            catch (OperationCanceledException)
            {
                await RunOnMainThreadAsync(() =>
                {
                    Debug.Log("[PMX转FBX] 转换任务被用户取消");
                });
            }
            catch (Exception ex)
            {
                await RunOnMainThreadAsync(() =>
                {
                    ShowMainThreadDialog("错误", $"转换过程异常：{ex.Message}", "确定");
                    Debug.LogError($"[PMX转FBX] 转换异常：{ex.Message}");
                });
            }
            finally
            {
                // 主线程清理进度条与状态
                await RunOnMainThreadAsync(() =>
                {
                    EditorUtility.ClearProgressBar();
                });

                _isConverting = false;
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 内部核心异步转换逻辑（单文件实现）
        /// </summary>
        private static async Task<bool> ConvertPmxInternalAsync(
            string pmxUnityPath,
            string outputUnityPath,
            Action<float, string> progressCallback,
            bool overwriteExisting,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            // 主线程更新进度（初始状态）
            progressCallback?.Invoke(0.1f, "初始化转换环境...");

            // 路径转换（Unity相对路径 → 系统绝对路径）
            string pmxAbsPath = Path.GetFullPath(pmxUnityPath);
            string outputAbsPath = Path.GetFullPath(outputUnityPath);
            string pmxFileName = Path.GetFileNameWithoutExtension(pmxAbsPath);
            string fbxAbsPath = Path.Combine(outputAbsPath, $"{pmxFileName}.fbx");

            // 检查覆盖逻辑
            if (File.Exists(fbxAbsPath) && !overwriteExisting)
            {
                progressCallback?.Invoke(0f, $"文件已存在（未允许覆盖）：{pmxFileName}.fbx");
                return false;
            }

            try
            {
                // 步骤1：调用PMX2FBX工具生成FBX
                progressCallback?.Invoke(0.3f, "启动PMX2FBX工具...");
                await RunPMX2FBXAsync(
                    toolPath: _settings.PMX2FBXPath,
                    pmxAbsPath: pmxAbsPath,
                    workDir: outputAbsPath,
                    progressCallback: progressCallback,
                    timeoutMs: timeoutMs,
                    cancellationToken: cancellationToken);

                // 步骤2：验证FBX生成结果
                if (!File.Exists(fbxAbsPath))
                    throw new FileNotFoundException("工具执行完成，但未找到生成的FBX文件", fbxAbsPath);
                progressCallback?.Invoke(0.7f, "FBX文件生成成功");

                // 步骤3：刷新Unity资源库
                progressCallback?.Invoke(0.8f, "刷新Unity资源库...");
                await RunOnMainThreadAsync(() =>
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                });

                progressCallback?.Invoke(1.0f, "转换完成");
                return true;
            }
            catch (OperationCanceledException)
            {
                // 清理未完成的FBX文件
                if (File.Exists(fbxAbsPath))
                    File.Delete(fbxAbsPath);
                progressCallback?.Invoke(0f, "转换已取消");
                return false;
            }
            catch (TimeoutException)
            {
                // 清理超时产生的不完整文件
                if (File.Exists(fbxAbsPath))
                    File.Delete(fbxAbsPath);
                progressCallback?.Invoke(0f, $"转换超时（{timeoutMs / 1000}秒）");
                return false;
            }
            catch (Exception ex)
            {
                // 清理异常产生的不完整文件
                if (File.Exists(fbxAbsPath))
                    File.Delete(fbxAbsPath);
                progressCallback?.Invoke(0f, $"转换失败：{ex.Message}");
                Debug.LogError($"[PMX转FBX] 单文件转换失败（{pmxUnityPath}）：{ex.Message}");
                return false;
            }
        }
        #endregion


        #region 核心辅助方法（修复主线程问题）
        /// <summary>
        /// 获取选中的单个PMX/PMD文件路径（仅返回第一个）
        /// </summary>
        private static string GetSelectedSinglePmxPath()
        {
            // 过滤选中的Assets文件，仅保留第一个PMX/PMD
            var selectedAssets = Selection.GetFiltered<DefaultAsset>(SelectionMode.Assets);
            foreach (var asset in selectedAssets)
            {
                string assetPath = AssetDatabase.GetAssetPath(asset);
                string ext = Path.GetExtension(assetPath).ToLower();
                if (ext == ".pmx" || ext == ".pmd")
                {
                    return assetPath; // 仅返回第一个符合条件的文件
                }
            }
            return null;
        }

        /// <summary>
        /// 在主线程执行操作（解决Editor UI线程安全问题）
        /// </summary>
        /// <param name="action">需要在主线程执行的逻辑</param>
        private static Task RunOnMainThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            // 利用EditorApplication.delayCall将操作委托到主线程
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action?.Invoke();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            return tcs.Task;
        }

        /// <summary>
        /// 在主线程显示对话框（避免线程安全问题）
        /// </summary>
        private static void ShowMainThreadDialog(string title, string message, string ok)
        {
            EditorUtility.DisplayDialog(title, message, ok);
        }

        /// <summary>
        /// 验证转换参数有效性
        /// </summary>
        private static bool ValidateConversionParams(string pmxPath, string outputPath, bool overwrite)
        {
            // 1. 验证PMX/PMD文件存在性
            if (string.IsNullOrEmpty(pmxPath) || !File.Exists(pmxPath))
            {
                ShowMainThreadDialog("错误", $"PMX/PMD文件不存在：{pmxPath}", "确定");
                return false;
            }

            // 2. 验证文件格式
            string ext = Path.GetExtension(pmxPath).ToLower();
            if (ext != ".pmx" && ext != ".pmd")
            {
                ShowMainThreadDialog("错误", $"不支持的文件格式：{ext}（仅支持PMX/PMD）", "确定");
                return false;
            }

            // 3. 验证PMX2FBX工具路径
            if (string.IsNullOrEmpty(_settings.PMX2FBXPath) || !File.Exists(_settings.PMX2FBXPath))
            {
                ShowMainThreadDialog(
                    "错误",
                    $"PMX2FBX工具路径无效：{_settings.PMX2FBXPath}\n请在配置文件（Assets/VMD2AnimSettings.asset）中设置正确路径",
                    "确定");
                return false;
            }

            // 4. 验证输出目录（不存在则创建）
            if (!Directory.Exists(outputPath))
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception ex)
                {
                    ShowMainThreadDialog("错误", $"创建输出目录失败：{ex.Message}", "确定");
                    return false;
                }
            }

            // 5. 验证覆盖权限（文件存在且需要覆盖时）
            string fbxPath = Path.Combine(outputPath, $"{Path.GetFileNameWithoutExtension(pmxPath)}.fbx");
            if (File.Exists(fbxPath) && overwrite && !IsFileWritable(fbxPath))
            {
                ShowMainThreadDialog("错误", $"FBX文件不可写：{fbxPath}（无法覆盖）", "确定");
                return false;
            }

            return true;
        }
        #endregion


        #region PMX2FBX工具调用相关
        /// <summary>
        /// 异步调用PMX2FBX工具（不阻塞主线程）
        /// </summary>
        private static Task RunPMX2FBXAsync(
            string toolPath,
            string pmxAbsPath,
            string workDir,
            Action<float, string> progressCallback,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            // 包装同步方法为异步任务（避免阻塞主线程）
            return Task.Run(() =>
            {
                RunPMX2FBXSync(toolPath, pmxAbsPath, workDir, progressCallback, timeoutMs, cancellationToken);
            }, cancellationToken);
        }

        /// <summary>
        /// 同步调用PMX2FBX工具（内部使用，由异步方法包装）
        /// </summary>
        private static void RunPMX2FBXSync(
            string toolPath,
            string pmxAbsPath,
            string workDir,
            Action<float, string> progressCallback,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            string toolAbsPath = Path.GetFullPath(toolPath);
            string toolDir = Path.GetDirectoryName(toolAbsPath);
            string defaultConfigPath = Path.Combine(toolDir, "pmx2fbx.xml");
            string backupConfigPath = Path.Combine(toolDir, "pmx2fbx.xml.bak");
            bool configBackedUp = false;

            try
            {
                // 1. 备份工具原始配置（避免修改默认配置）
                if (File.Exists(defaultConfigPath))
                {
                    File.Copy(defaultConfigPath, backupConfigPath, true);
                    configBackedUp = true;
                    progressCallback?.Invoke(0.4f, "备份PMX2FBX原始配置...");
                }

                // 2. 生成优化的临时配置（修复原XML语法错误）
                string tempConfigPath = Path.Combine(workDir, "pmx2fbx_temp.xml");
                GenerateOptimizedConfig(tempConfigPath);
                progressCallback?.Invoke(0.5f, "应用优化转换配置...");

                // 3. 替换工具配置为临时配置
                if (File.Exists(tempConfigPath))
                    File.Copy(tempConfigPath, defaultConfigPath, true);

                // 4. 构造命令行参数（仅传递PMX绝对路径）
                string arguments = $"\"{pmxAbsPath}\"";
                Debug.Log($"[PMX转FBX] 执行命令：{toolAbsPath} {arguments}");
                Debug.Log($"[PMX转FBX] 工作目录：{workDir}");

                // 5. 启动工具进程
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = toolAbsPath,
                        Arguments = arguments,
                        WorkingDirectory = workDir,
                        UseShellExecute = true,
                        CreateNoWindow = false, // 显示工具窗口（便于排查错误）
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };

                    process.Start();
                    DateTime startTime = DateTime.Now;
                    progressCallback?.Invoke(0.6f, "工具执行中...");

                    // 6. 监控进程状态（超时/取消检查）
                    while (!process.HasExited)
                    {
                        // 检查取消信号
                        if (cancellationToken.IsCancellationRequested)
                        {
                            process.Kill();
                            throw new OperationCanceledException("工具执行被用户取消");
                        }

                        // 检查超时
                        if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                        {
                            process.Kill();
                            throw new TimeoutException($"工具执行超时（{timeoutMs / 1000}秒）");
                        }

                        // 降低CPU占用
                        Thread.Sleep(100);
                    }

                    // 7. 检查进程退出码（非0表示失败）
                    if (process.ExitCode != 0)
                        throw new Exception($"工具执行失败，退出码：{process.ExitCode}");
                }
            }
            finally
            {
                // 恢复原始配置
                if (configBackedUp && File.Exists(backupConfigPath))
                {
                    File.Copy(backupConfigPath, defaultConfigPath, true);
                    File.Delete(backupConfigPath);
                    progressCallback?.Invoke(0.65f, "恢复原始配置...");
                }

                // 清理临时配置文件
                string tempConfigPath = Path.Combine(workDir, "pmx2fbx_temp.xml");
                if (File.Exists(tempConfigPath))
                    File.Delete(tempConfigPath);
            }
        }

        /// <summary>
        /// 生成优化的PMX2FBX配置文件（修复原XML语法错误）
        /// </summary>
        private static void GenerateOptimizedConfig(string configPath)
        {
            // 修复：原代码中animRootTransformFlag标签重复（多了FlagFlag1）
            string configContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<PMX2FBXConfig xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <globalSettings>
        <editorAdvancedMode>true</editorAdvancedMode>
        <morphRenameFlag>0</morphRenameFlag>
        <prefixMorphNoNameFlag>0</prefixMorphNoNameFlag>
    </globalSettings>
    <bulletPhysics>
        <enabled>0</enabled>
    </bulletPhysics>
</PMX2FBXConfig>";

            File.WriteAllText(configPath, configContent);
            Debug.Log($"[PMX转FBX] 生成优化配置文件：{configPath}");
        }

        /// <summary>
        /// 检查文件是否可写
        /// </summary>
        private static bool IsFileWritable(string filePath)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Write))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }

    // ==============================================
    // 复用原有配置类（保持与原工具配置一致）
    // ==============================================
    [Serializable]
    public class ConversionSettings : ScriptableObject
    {
        [Header("PMX2FBX工具配置")]
        public string PMX2FBXPath = "Assets/MMD4Mecanim/Editor/PMX2FBX/pmx2fbx.exe";

        [Header("转换选项")]
        public bool OverwriteExisting = false;
        public int TimeoutSeconds = 180;

        // 原配置中未使用的字段（保留以兼容原配置文件）
        public string VmdFilePath = "";
        public string PmxFilePath = "";
        public string DefaultPmxPath = "Assets/AnimConverter/Model/Tda_miku.pmx";
        public string OutputPath = "Assets/AnimConverter/Output/";
        public bool UseDefaultPMX = true;

        /// <summary>
        /// 配置文件路径（与原工具一致）
        /// </summary>
        private const string SETTINGS_PATH = "Assets/VMD2AnimSettings.asset";

        /// <summary>
        /// 加载配置（不存在则自动创建）
        /// </summary>
        public static ConversionSettings Load()
        {
            ConversionSettings settings = AssetDatabase.LoadAssetAtPath<ConversionSettings>(SETTINGS_PATH);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ConversionSettings>();
                AssetDatabase.CreateAsset(settings, SETTINGS_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log($"[PMX转FBX] 配置文件不存在，已自动创建：{SETTINGS_PATH}");
            }
            return settings;
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            ConversionSettings existing = AssetDatabase.LoadAssetAtPath<ConversionSettings>(SETTINGS_PATH);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(this, SETTINGS_PATH);
            }
            else
            {
                EditorUtility.CopySerialized(this, existing);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif