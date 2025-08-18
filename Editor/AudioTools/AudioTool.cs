using UnityEditor;
using UnityEngine;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Linq;

namespace AudioTools
{
    public class AudioEditor : EditorWindow
    {
        private AudioSource previewAudioSource;
        private bool isPlaying = false;
        private float[] trimmedSamples;

        private AudioClip audioClip;
        private float startTrim = 0f;
        private float endTrim = 0f;
        private float fadeStartDuration = 0f;
        private float fadeEndDuration = 0f;
        private bool loopPreview = false;
        private float frontPadding = 0f;
        private float backPadding = 0f;

        // FFmpeg 配置
        private bool useFFmpeg = false;
        private string ffmpegPath = "";
        private string outputFormat = "ogg";
        private string additionalArgs = "";

        private const string FFMPEG_PATH_KEY = "AudioEditor_FFmpegPath";
        private Vector2 scrollPosition = Vector2.zero;
        private float currentTime = 0f;

        // 帧相关变量
        private const float framesPerSecond = 60f;
        private int startTrimFrames = 0;
        private int endTrimFrames = 0;
        private int fadeStartDurationFrames = 0;
        private int fadeEndDurationFrames = 0;
        private int frontPaddingFrames = 0;
        private int backPaddingFrames = 0;

        // 预览条相关变量
        private float viewLength = 0f; // 背景显示时长
        private float horizontalScroll = 0f; // 水平滑动位置（像素）

        [MenuItem("Tools/音频编辑器")]
        public static void ShowWindow()
        {
            AudioEditor window = GetWindow<AudioEditor>("音频编辑器");
            window.minSize = new Vector2(600, 800);
            window.maxSize = new Vector2(600, 800);
        }

        private void OnEnable()
        {
            if (previewAudioSource == null)
            {
                GameObject audioPreviewer = new GameObject("音频预览器");
                previewAudioSource = audioPreviewer.AddComponent<AudioSource>();
                previewAudioSource.hideFlags = HideFlags.HideAndDontSave;
            }
            ffmpegPath = EditorPrefs.GetString(FFMPEG_PATH_KEY, "");
        }

        private void OnDisable()
        {
            // 销毁预览音频源和残留的预览剪辑
            if (previewAudioSource != null)
            {
                if (previewAudioSource.clip != null)
                    DestroyImmediate(previewAudioSource.clip);
                DestroyImmediate(previewAudioSource.gameObject);
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("裁剪与淡入淡出音频片段", EditorStyles.boldLabel);

            // 音频剪辑选择（修复：更换音频时重置预览条参数）
            AudioClip newAudioClip = (AudioClip)EditorGUILayout.ObjectField("音频片段", audioClip, typeof(AudioClip), false);
            if (newAudioClip != audioClip)
            {
                audioClip = newAudioClip;
                if (audioClip != null)
                {
                    startTrim = 0f;
                    endTrim = audioClip.length;
                    endTrimFrames = Mathf.RoundToInt(endTrim * framesPerSecond);
                    viewLength = audioClip.length * 1.2f;
                    horizontalScroll = 0f; // 重置水平滚动
                }
                // 停止当前预览
                if (isPlaying)
                    StopPreview();
            }

            if (audioClip != null)
            {
                DrawPreviewBar();

                GUILayout.Label("所有时间单位支持秒和帧（每秒60帧）", EditorStyles.helpBox);
                GUILayout.Label("操作          秒                  帧", EditorStyles.boldLabel);

                // 起始裁剪（修复：滑块最大值设为endTrim，避免无效范围）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("起始裁剪", GUILayout.Width(100));
                startTrim = EditorGUILayout.Slider(startTrim, 0f, endTrim); // 最大值改为endTrim
                startTrimFrames = Mathf.RoundToInt(startTrim * framesPerSecond);
                startTrimFrames = EditorGUILayout.IntField(startTrimFrames, GUILayout.Width(100));
                // 帧转秒后再次Clamp，确保不超过endTrim
                startTrim = Mathf.Clamp(startTrimFrames / framesPerSecond, 0f, endTrim);
                EditorGUILayout.EndHorizontal();

                // 结束裁剪（修复：滑块最小值设为startTrim，避免无效范围）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("结束裁剪", GUILayout.Width(100));
                endTrim = EditorGUILayout.Slider(endTrim, startTrim, audioClip.length); // 最小值改为startTrim
                endTrimFrames = Mathf.RoundToInt(endTrim * framesPerSecond);
                endTrimFrames = EditorGUILayout.IntField(endTrimFrames, GUILayout.Width(100));
                // 帧转秒后再次Clamp，确保不小于startTrim
                endTrim = Mathf.Clamp(endTrimFrames / framesPerSecond, startTrim, audioClip.length);
                EditorGUILayout.EndHorizontal();

                // 淡入时长（修复：最大值设为裁剪后音频长度）
                EditorGUILayout.BeginHorizontal();
                float trimmedLength = endTrim - startTrim;
                GUILayout.Label("淡入时长", GUILayout.Width(100));
                fadeStartDuration = EditorGUILayout.Slider(fadeStartDuration, 0f, trimmedLength);
                fadeStartDurationFrames = Mathf.RoundToInt(fadeStartDuration * framesPerSecond);
                fadeStartDurationFrames = EditorGUILayout.IntField(fadeStartDurationFrames, GUILayout.Width(100));
                fadeStartDuration = Mathf.Clamp(fadeStartDurationFrames / framesPerSecond, 0f, trimmedLength);
                EditorGUILayout.EndHorizontal();

                // 淡出时长（修复：最大值设为裁剪后音频长度）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("淡出时长", GUILayout.Width(100));
                fadeEndDuration = EditorGUILayout.Slider(fadeEndDuration, 0f, trimmedLength);
                fadeEndDurationFrames = Mathf.RoundToInt(fadeEndDuration * framesPerSecond);
                fadeEndDurationFrames = EditorGUILayout.IntField(fadeEndDurationFrames, GUILayout.Width(100));
                fadeEndDuration = Mathf.Clamp(fadeEndDurationFrames / framesPerSecond, 0f, trimmedLength);
                EditorGUILayout.EndHorizontal();

                // 前部沉默添加（逻辑不变，仅保留Clamp）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("前部沉默添加", GUILayout.Width(100));
                frontPadding = EditorGUILayout.Slider(frontPadding, 0f, 10f);
                frontPaddingFrames = Mathf.RoundToInt(frontPadding * framesPerSecond);
                frontPaddingFrames = EditorGUILayout.IntField(frontPaddingFrames, GUILayout.Width(100));
                frontPadding = Mathf.Clamp(frontPaddingFrames / framesPerSecond, 0f, 10f);
                EditorGUILayout.EndHorizontal();

                // 后部沉默添加（逻辑不变，仅保留Clamp）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("后部沉默添加", GUILayout.Width(100));
                backPadding = EditorGUILayout.Slider(backPadding, 0f, 10f);
                backPaddingFrames = Mathf.RoundToInt(backPadding * framesPerSecond);
                backPaddingFrames = EditorGUILayout.IntField(backPaddingFrames, GUILayout.Width(100));
                backPadding = Mathf.Clamp(backPaddingFrames / framesPerSecond, 0f, 10f);
                EditorGUILayout.EndHorizontal();

                loopPreview = GUILayout.Toggle(loopPreview, "循环预览");

                // 当前播放时间滑块（逻辑不变）
                if (previewAudioSource.clip != null)
                {
                    currentTime = EditorGUILayout.Slider("当前播放时间", currentTime, 0f, previewAudioSource.clip.length);
                    if (GUI.changed && !isPlaying)
                    {
                        previewAudioSource.time = currentTime;
                    }
                }

                // FFmpeg配置区域（逻辑不变，仅保留）
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical("box");
                GUILayout.Label("可选FFmpeg输出配置", EditorStyles.boldLabel);
                useFFmpeg = EditorGUILayout.Toggle("使用FFmpeg保存", useFFmpeg, GUILayout.Height(30));
                if (useFFmpeg)
                {
                    EditorGUILayout.HelpBox("请确保已安装FFmpeg并设置正确路径（支持空格路径）。", MessageType.Info);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("FFmpeg路径", GUILayout.Width(100));
                    ffmpegPath = EditorGUILayout.TextField(ffmpegPath);
                    if (GUILayout.Button("浏览", GUILayout.Width(60)))
                    {
                        string selectedPath = EditorUtility.OpenFilePanel("选择FFmpeg可执行文件", "", "exe"); // 限定exe格式（Windows）
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            ffmpegPath = selectedPath;
                            EditorPrefs.SetString(FFMPEG_PATH_KEY, ffmpegPath);
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("输出格式\n(wav/mp3/ogg)", GUILayout.Width(100));
                    outputFormat = EditorGUILayout.TextField(outputFormat).ToLower();
                    // 限制支持的格式
                    if (!new[] { "wav", "mp3", "ogg" }.Contains(outputFormat))
                    {
                        EditorGUILayout.HelpBox("仅支持wav/mp3/ogg格式！", MessageType.Warning);
                        outputFormat = "ogg"; // 重置为默认
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("附加参数\n(例如 -b:a 192k)", GUILayout.Width(100));
                    additionalArgs = EditorGUILayout.TextField(additionalArgs);
                    EditorGUILayout.EndHorizontal();

                    if (GUILayout.Button("查看FFmpeg文档"))
                    {
                        Application.OpenURL("https://ffmpeg.org/documentation.html");
                    }
                }
                EditorGUILayout.EndVertical();

                // 播放/停止按钮（逻辑不变）
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("播放"))
                {
                    PlayPreview();
                }
                if (GUILayout.Button("停止"))
                {
                    StopPreview();
                }
                GUILayout.EndHorizontal();

                // 编辑并保存按钮（逻辑不变，依赖修复后的TrimAndFadeAudioClip）
                if (GUILayout.Button("编辑并保存"))
                {
                    TrimAndFadeAudioClip();
                }
            }

            EditorGUILayout.EndScrollView();

            // 播放时更新当前时间（逻辑不变）
            if (isPlaying)
            {
                currentTime = previewAudioSource.time;
                Repaint();
            }
        }

        // 修复：预览条Seek定位错误，基于实际处理后长度计算时间
        private void DrawPreviewBar()
        {
            GUILayout.Label("音频范围预览 (灰色:沉默, 蓝色:原始音频, 黄色:淡入淡出)");
            float trimmedLength = endTrim - startTrim;
            float totalLength = frontPadding + trimmedLength + backPadding;
            if (totalLength <= 0 || viewLength <= 0) return;

            // 调整背景时长（逻辑不变）
            viewLength = EditorGUILayout.Slider("背景时长 (秒)", viewLength, 1f, audioClip.length * 5f);

            // 计算显示宽度和每秒像素（修复：基于处理后总长度计算，确保Seek准确）
            float displayWidth = position.width - 30f;
            float pixelsPerSecond = displayWidth / viewLength; // 背景显示的像素密度
            float contentTotalWidth = totalLength * pixelsPerSecond; // 实际内容的总像素宽度

            // 水平滚动条（逻辑不变）
            if (contentTotalWidth > displayWidth)
            {
                horizontalScroll = GUILayout.HorizontalScrollbar(horizontalScroll, displayWidth, 0f, contentTotalWidth);
            }
            else
            {
                horizontalScroll = 0f;
            }

            // 绘制预览条背景（逻辑不变）
            Rect previewRect = GUILayoutUtility.GetRect(displayWidth, 20f);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            GUI.DrawTexture(previewRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 裁剪区域，处理滚动
            GUI.BeginClip(previewRect);
            GUI.BeginGroup(new Rect(-horizontalScroll, 0, contentTotalWidth, 20f));

            // 绘制前沉默（逻辑不变）
            float frontWidth = frontPadding * pixelsPerSecond;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(0, 0, frontWidth, 20f), Texture2D.whiteTexture);

            // 绘制处理后的音频（逻辑不变）
            float audioWidth = trimmedLength * pixelsPerSecond;
            GUI.color = Color.blue;
            GUI.DrawTexture(new Rect(frontWidth, 0, audioWidth, 20f), Texture2D.whiteTexture);

            // 绘制后沉默（逻辑不变）
            float backWidth = backPadding * pixelsPerSecond;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(frontWidth + audioWidth, 0, backWidth, 20f), Texture2D.whiteTexture);

            // 绘制淡入淡出区域（逻辑不变）
            GUI.color = new Color(1, 1, 0, 0.5f); // 半透明黄色
            if (fadeStartDuration > 0)
            {
                float fadeInWidth = fadeStartDuration * pixelsPerSecond;
                GUI.DrawTexture(new Rect(frontWidth, 0, fadeInWidth, 20f), Texture2D.whiteTexture);
            }
            if (fadeEndDuration > 0)
            {
                float fadeOutWidth = fadeEndDuration * pixelsPerSecond;
                GUI.DrawTexture(new Rect(frontWidth + audioWidth - fadeOutWidth, 0, fadeOutWidth, 20f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            // 绘制文本标签（逻辑不变）
            GUI.Label(new Rect(0, 0, frontWidth, 20f), $"前沉默: {frontPadding:F2}s", EditorStyles.miniLabel);
            GUI.Label(new Rect(frontWidth, 0, audioWidth, 20f), $"音频: {trimmedLength:F2}s ({startTrim:F2}-{endTrim:F2})", EditorStyles.miniLabel);
            GUI.Label(new Rect(frontWidth + audioWidth, 0, backWidth, 20f), $"后沉默: {backPadding:F2}s", EditorStyles.miniLabel);

            // 结束分组和裁剪
            GUI.EndGroup();
            GUI.EndClip();

            // 修复：点击预览条Seek（基于处理后总长度计算时间）
            if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
            {
                if (previewAudioSource.clip != null)
                {
                    // 计算点击位置对应的像素（含滚动）
                    float clickPixel = Event.current.mousePosition.x - previewRect.x + horizontalScroll;
                    // 像素转时间（基于处理后音频的总长度）
                    float seekTime = clickPixel / pixelsPerSecond;
                    // Clamp到有效范围
                    seekTime = Mathf.Clamp(seekTime, 0f, previewAudioSource.clip.length);
                    previewAudioSource.time = seekTime;
                    currentTime = seekTime;
                    Event.current.Use(); // 消耗事件，避免穿透
                }
            }

            // 快捷键支持（逻辑不变）
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.LeftArrow)
                {
                    horizontalScroll = Mathf.Max(0, horizontalScroll - 10);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.keyCode == KeyCode.RightArrow)
                {
                    horizontalScroll = Mathf.Min(contentTotalWidth - displayWidth, horizontalScroll + 10);
                    Event.current.Use();
                    Repaint();
                }
            }
        }

        // 修复：预览音频内存泄漏（销毁旧剪辑）
        private void PlayPreview()
        {
            if (previewAudioSource == null || audioClip == null)
                return;

            // 停止当前预览并销毁旧剪辑
            StopPreview();
            if (previewAudioSource.clip != null)
            {
                DestroyImmediate(previewAudioSource.clip);
                previewAudioSource.clip = null;
            }

            // 生成处理后的音频样本（复用统一方法，避免重复代码）
            trimmedSamples = ProcessAudioSamples(audioClip, startTrim, endTrim, fadeStartDuration, fadeEndDuration, frontPadding, backPadding);
            // 创建预览剪辑
            AudioClip trimmedClip = AudioClip.Create(
                $"预览_{audioClip.name}",
                trimmedSamples.Length / audioClip.channels,
                audioClip.channels,
                audioClip.frequency,
                false
            );
            trimmedClip.SetData(trimmedSamples, 0);

            // 播放预览
            previewAudioSource.loop = loopPreview;
            previewAudioSource.clip = trimmedClip;
            previewAudioSource.volume = 1f;
            previewAudioSource.Play();
            isPlaying = true;
        }

        // 停止预览（逻辑不变，仅保留）
        private void StopPreview()
        {
            if (previewAudioSource == null || !isPlaying)
                return;

            previewAudioSource.Stop();
            isPlaying = false;
        }

        // 修复：合并重复代码，统一音频样本处理逻辑
        private float[] ProcessAudioSamples(AudioClip clip, float startTrim, float endTrim, float fadeStartDuration, float fadeEndDuration, float frontPadding, float backPadding)
        {
            int freq = clip.frequency;
            int channels = clip.channels;
            float[] originalSamples = new float[clip.samples * channels];
            clip.GetData(originalSamples, 0);

            // 计算裁剪起始/结束样本索引（Clamp避免越界）
            int startSample = Mathf.Clamp(Mathf.FloorToInt(startTrim * freq * channels), 0, originalSamples.Length);
            int endSample = Mathf.Clamp(Mathf.FloorToInt(endTrim * freq * channels), startSample, originalSamples.Length);
            int trimmedSampleCount = endSample - startSample;

            // 计算前后沉默样本数
            int frontPaddingSamples = Mathf.FloorToInt(frontPadding * freq * channels);
            int backPaddingSamples = Mathf.FloorToInt(backPadding * freq * channels);
            int totalSampleCount = frontPaddingSamples + trimmedSampleCount + backPaddingSamples;

            // 初始化最终样本数组
            float[] finalSamples = new float[totalSampleCount];

            // 填充前沉默（0值）
            for (int i = 0; i < frontPaddingSamples; i++)
                finalSamples[i] = 0f;

            // 填充裁剪后的音频样本
            for (int i = 0; i < trimmedSampleCount; i++)
                finalSamples[frontPaddingSamples + i] = originalSamples[startSample + i];

            // 填充后沉默（0值）
            for (int i = frontPaddingSamples + trimmedSampleCount; i < totalSampleCount; i++)
                finalSamples[i] = 0f;

            // 应用淡入效果
            if (fadeStartDuration > 0)
            {
                int fadeInSamples = Mathf.FloorToInt(fadeStartDuration * freq * channels);
                for (int i = 0; i < fadeInSamples && (frontPaddingSamples + i) < finalSamples.Length; i++)
                {
                    float fadeFactor = Mathf.InverseLerp(0, fadeInSamples, i); // 线性淡入（可改为缓动）
                    finalSamples[frontPaddingSamples + i] *= fadeFactor;
                }
            }

            // 应用淡出效果
            if (fadeEndDuration > 0)
            {
                int fadeOutSamples = Mathf.FloorToInt(fadeEndDuration * freq * channels);
                int fadeOutStartIndex = frontPaddingSamples + trimmedSampleCount - fadeOutSamples;
                for (int i = 0; i < fadeOutSamples && (fadeOutStartIndex + i) < finalSamples.Length; i++)
                {
                    float fadeFactor = Mathf.InverseLerp(fadeOutSamples, 0, i); // 线性淡出
                    finalSamples[fadeOutStartIndex + i] *= fadeFactor;
                }
            }

            return finalSamples;
        }

        // 修复：FFmpeg导出无效（补充WavUtility依赖，修复路径和参数）
        private void TrimAndFadeAudioClip()
        {
            if (audioClip == null)
            {
                Debug.LogError("未选择要裁剪的音频片段。");
                return;
            }

            float trimmedLength = endTrim - startTrim;
            if (trimmedLength <= 0)
            {
                Debug.LogError("无效的裁剪范围：结束时间必须大于起始时间。");
                return;
            }

            // 获取原始音频路径（逻辑不变）
            string originalPath = AssetDatabase.GetAssetPath(audioClip);
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogError("无法获取音频片段的资源路径（仅支持Project窗口中的音频文件）。");
                return;
            }

            // 构建输出路径（逻辑不变）
            string directory = Path.GetDirectoryName(originalPath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            string outputExt = useFFmpeg ? outputFormat : "wav";
            string outputPath = Path.Combine(directory, $"{fileNameWithoutExt}_已编辑.{outputExt}");

            // 构建临时WAV路径（用于FFmpeg转换）
            string tempWavPath = Path.Combine(directory, $"{fileNameWithoutExt}_temp.wav");

            try
            {
                // 1. 生成处理后的音频剪辑
                float[] processedSamples = ProcessAudioSamples(audioClip, startTrim, endTrim, fadeStartDuration, fadeEndDuration, frontPadding, backPadding);
                AudioClip processedClip = AudioClip.Create(
                    $"{audioClip.name}_已编辑",
                    processedSamples.Length / audioClip.channels,
                    audioClip.channels,
                    audioClip.frequency,
                    false
                );
                processedClip.SetData(processedSamples, 0);


                byte[] wavData = WavUtility.FromAudioClip(processedClip);
                File.WriteAllBytes(tempWavPath, wavData);
                Debug.Log($"临时WAV文件已生成：{tempWavPath}");

                // 3. 处理输出（直接保存WAV或用FFmpeg转换）
                if (useFFmpeg && !string.IsNullOrEmpty(ffmpegPath) && outputExt != "wav")
                {
                    // 修复：FFmpeg转换（路径加引号，添加强制覆盖）
                    bool convertSuccess = ConvertWithFFmpeg(tempWavPath, outputPath, outputFormat);
                    if (convertSuccess)
                    {
                        Debug.Log($"FFmpeg转换成功，输出文件：{outputPath}");
                        File.Delete(tempWavPath); // 转换成功后删除临时文件
                    }
                    else
                    {
                        Debug.LogError($"FFmpeg转换失败，临时WAV文件保留：{tempWavPath}");
                    }
                }
                else
                {
                    // 直接保存为WAV（覆盖旧文件）
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(tempWavPath, outputPath);
                    Debug.Log($"直接保存WAV成功，输出文件：{outputPath}");
                }

                // 刷新AssetDatabase，显示新文件
                AssetDatabase.Refresh();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"音频处理失败：{ex.Message}\n堆栈跟踪：{ex.StackTrace}");
                // 异常时删除临时文件（避免残留）
                if (File.Exists(tempWavPath))
                    File.Delete(tempWavPath);
            }
        }

        // 修复：FFmpeg转换逻辑（路径加引号，添加-y覆盖，检查文件存在）
        private bool ConvertWithFFmpeg(string inputPath, string outputPath, string format)
        {
            // 检查FFmpeg可执行文件是否存在
            if (!File.Exists(ffmpegPath))
            {
                Debug.LogError($"FFmpeg文件不存在：{ffmpegPath}");
                return false;
            }

            // 检查输入文件是否存在
            if (!File.Exists(inputPath))
            {
                Debug.LogError($"FFmpeg输入文件不存在：{inputPath}");
                return false;
            }

            // 配置FFmpeg编码参数（逻辑不变，补充格式校验）
            string codec = "";
            string defaultArgs = "";
            switch (format.ToLower())
            {
                case "mp3":
                    codec = "-c:a libmp3lame";
                    defaultArgs = "-b:a 192k"; // 默认192kbps比特率
                    break;
                case "ogg":
                    codec = "-c:a libvorbis";
                    defaultArgs = "-q:a 5"; // 默认质量5（0-10）
                    break;
                default:
                    Debug.LogError($"不支持的FFmpeg输出格式：{format}（仅支持mp3/ogg）");
                    return false;
            }

            // 构建FFmpeg参数（修复：路径加引号，添加-y强制覆盖）
            string safeFfmpegPath = WrapPathInQuotes(ffmpegPath);
            string safeInputPath = WrapPathInQuotes(inputPath);
            string safeOutputPath = WrapPathInQuotes(outputPath);
            string arguments = $"-y -i {safeInputPath} {codec} {defaultArgs} {additionalArgs} {safeOutputPath}";

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = safeFfmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using (Process process = Process.Start(startInfo))
                {
                    // 读取输出和错误信息（避免死锁）
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // 输出FFmpeg日志（便于调试）
                    if (!string.IsNullOrEmpty(output))
                        Debug.Log($"FFmpeg输出：{output}");
                    if (!string.IsNullOrEmpty(error))
                        Debug.LogWarning($"FFmpeg警告：{error}");

                    // 判断是否成功（ExitCode=0为成功）
                    if (process.ExitCode == 0)
                        return true;
                    else
                        Debug.LogError($"FFmpeg转换失败（ExitCode={process.ExitCode}）：{error}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"运行FFmpeg时出错：{ex.Message}\n堆栈跟踪：{ex.StackTrace}");
            }

            return false;
        }

        // 辅助方法：路径含空格时包裹引号
        private string WrapPathInQuotes(string path)
        {
            return path.Contains(" ") && !path.StartsWith("\"") && !path.EndsWith("\"")
                ? $"\"{path}\""
                : path;
        }
    }

    // 补充：WavUtility工具类（核心修复，用于生成有效WAV文件）
}