using UnityEditor;
using UnityEngine;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace SimblendTools
{
    public class AudioEditor : EditorWindow
    {
        private AudioSource previewAudioSource;
        private bool isPlaying = false;
        private float[] trimmedSamples;
        private bool isAudioAdded = false;

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

        private const string FFMPEG_PATH_KEY = "SimblendTools_AudioEditor_FFmpegPath";

        private Vector2 scrollPosition = Vector2.zero;
        private Rect waveformRect;
        private float currentTime = 0f;

        // 帧相关变量
        private const float framesPerSecond = 60f;
        private int startTrimFrames = 0;
        private int endTrimFrames = 0;
        private int fadeStartDurationFrames = 0;
        private int fadeEndDurationFrames = 0;
        private int frontPaddingFrames = 0;
        private int backPaddingFrames = 0;

        // 预览条相关
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
            if (previewAudioSource != null)
            {
                DestroyImmediate(previewAudioSource.gameObject);
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("裁剪与淡入淡出音频片段", EditorStyles.boldLabel);

            AudioClip newAudioClip = (AudioClip)EditorGUILayout.ObjectField("音频片段", audioClip, typeof(AudioClip), false);
            if (newAudioClip != audioClip)
            {
                audioClip = newAudioClip;
                if (audioClip != null)
                {
                    endTrim = audioClip.length;
                    endTrimFrames = Mathf.RoundToInt(endTrim * framesPerSecond);
                    viewLength = audioClip.length * 1.2f;
                    isAudioAdded = true;
                }
                else
                {
                    isAudioAdded = false;
                }
            }

            if (audioClip != null)
            {
                DrawPreviewBar();

                GUILayout.Label("所有时间单位支持秒和帧（每秒60帧）", EditorStyles.helpBox);
                GUILayout.Label("操作          秒                  帧", EditorStyles.boldLabel);

                // 起始裁剪
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("起始裁剪", GUILayout.Width(100));
                startTrim = EditorGUILayout.Slider(startTrim, 0f, audioClip.length);
                startTrimFrames = Mathf.RoundToInt(startTrim * framesPerSecond);
                startTrimFrames = EditorGUILayout.IntField(startTrimFrames, GUILayout.Width(100));
                startTrim = startTrimFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                // 结束裁剪
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("结束裁剪", GUILayout.Width(100));
                endTrim = EditorGUILayout.Slider(endTrim, 0f, audioClip.length);
                endTrimFrames = Mathf.RoundToInt(endTrim * framesPerSecond);
                endTrimFrames = EditorGUILayout.IntField(endTrimFrames, GUILayout.Width(100));
                endTrim = endTrimFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                // 淡入时长
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("淡入时长", GUILayout.Width(100));
                fadeStartDuration = EditorGUILayout.Slider(fadeStartDuration, 0f, endTrim - startTrim);
                fadeStartDurationFrames = Mathf.RoundToInt(fadeStartDuration * framesPerSecond);
                fadeStartDurationFrames = EditorGUILayout.IntField(fadeStartDurationFrames, GUILayout.Width(100));
                fadeStartDuration = fadeStartDurationFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                // 淡出时长
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("淡出时长", GUILayout.Width(100));
                fadeEndDuration = EditorGUILayout.Slider(fadeEndDuration, 0f, endTrim - startTrim);
                fadeEndDurationFrames = Mathf.RoundToInt(fadeEndDuration * framesPerSecond);
                fadeEndDurationFrames = EditorGUILayout.IntField(fadeEndDurationFrames, GUILayout.Width(100));
                fadeEndDuration = fadeEndDurationFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                // 前部沉默添加
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("前部沉默添加", GUILayout.Width(100));
                frontPadding = EditorGUILayout.Slider(frontPadding, 0f, 10f);
                frontPaddingFrames = Mathf.RoundToInt(frontPadding * framesPerSecond);
                frontPaddingFrames = EditorGUILayout.IntField(frontPaddingFrames, GUILayout.Width(100));
                frontPadding = frontPaddingFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                // 后部沉默添加
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("后部沉默添加", GUILayout.Width(100));
                backPadding = EditorGUILayout.Slider(backPadding, 0f, 10f);
                backPaddingFrames = Mathf.RoundToInt(backPadding * framesPerSecond);
                backPaddingFrames = EditorGUILayout.IntField(backPaddingFrames, GUILayout.Width(100));
                backPadding = backPaddingFrames / framesPerSecond;
                EditorGUILayout.EndHorizontal();

                loopPreview = GUILayout.Toggle(loopPreview, "循环预览");

                if (previewAudioSource.clip != null)
                {
                    currentTime = EditorGUILayout.Slider("当前播放时间", currentTime, 0f, previewAudioSource.clip.length);
                    if (GUI.changed && !isPlaying)
                    {
                        previewAudioSource.time = currentTime;
                    }
                }

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical("box");
                GUILayout.Label("可选FFmpeg输出配置", EditorStyles.boldLabel);
                useFFmpeg = EditorGUILayout.Toggle("使用FFmpeg保存", useFFmpeg, GUILayout.Height(30));
                if (useFFmpeg)
                {
                    EditorGUILayout.HelpBox("请确保已安装FFmpeg并设置正确路径。", MessageType.Info);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("FFmpeg路径", GUILayout.Width(100));
                    ffmpegPath = EditorGUILayout.TextField(ffmpegPath);
                    if (GUILayout.Button("浏览", GUILayout.Width(60)))
                    {
                        string selectedPath = EditorUtility.OpenFilePanel("选择FFmpeg可执行文件", "", "");
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

                if (GUILayout.Button("编辑并保存"))
                {
                    TrimAndFadeAudioClip();
                }
            }

            EditorGUILayout.EndScrollView();

            if (isPlaying)
            {
                currentTime = previewAudioSource.time;
                Repaint();
            }
        }

        private void DrawPreviewBar()
        {
            GUILayout.Label("音频范围预览 (灰色:沉默, 蓝色:原始音频)");
            float totalLength = frontPadding + (endTrim - startTrim) + backPadding;
            if (totalLength <= 0 || viewLength <= 0) return;

            // 调整背景时长
            viewLength = EditorGUILayout.Slider("背景时长 (秒)", viewLength, 1f, audioClip.length * 5f);

            // 计算显示宽度和每秒像素
            float displayWidth = position.width - 30f; // 留点边距
            float pixelsPerSecond = displayWidth / viewLength;

            // 计算内容总像素宽度
            float totalWidth = totalLength * pixelsPerSecond;

            // 水平滚动条，如果内容超过显示宽度
            if (totalWidth > displayWidth)
            {
                horizontalScroll = GUILayout.HorizontalScrollbar(horizontalScroll, displayWidth, 0f, totalWidth);
            }
            else
            {
                horizontalScroll = 0f;
            }

            // 绘制预览条
            Rect previewRect = GUILayoutUtility.GetRect(displayWidth, 20f);

            // 绘制背景（参考时长）
            GUI.color = new Color(0.8f, 0.8f, 0.8f); // 浅灰
            GUI.DrawTexture(previewRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.BeginClip(previewRect);
            GUI.BeginGroup(new Rect(-horizontalScroll, 0, totalWidth, 20f));

            // 前沉默
            float frontWidth = frontPadding * pixelsPerSecond;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(0, 0, frontWidth, 20f), Texture2D.whiteTexture);

            // 原始音频
            float audioWidth = (endTrim - startTrim) * pixelsPerSecond;
            GUI.color = Color.blue;
            GUI.DrawTexture(new Rect(frontWidth, 0, audioWidth, 20f), Texture2D.whiteTexture);

            // 后沉默
            float backWidth = backPadding * pixelsPerSecond;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(frontWidth + audioWidth, 0, backWidth, 20f), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // 标注关键信息
            GUI.Label(new Rect(0, 0, frontWidth, 20f), "前沉默: " + frontPadding.ToString("F2") + "s", EditorStyles.miniLabel);
            GUI.Label(new Rect(frontWidth, 0, audioWidth, 20f), "音频: " + (endTrim - startTrim).ToString("F2") + "s (" + startTrim.ToString("F2") + " - " + endTrim.ToString("F2") + ")", EditorStyles.miniLabel);
            GUI.Label(new Rect(frontWidth + audioWidth, 0, backWidth, 20f), "后沉默: " + backPadding.ToString("F2") + "s", EditorStyles.miniLabel);

            // 淡入淡出标注
            if (fadeStartDuration > 0)
            {
                float fadeInWidth = fadeStartDuration * pixelsPerSecond;
                GUI.color = new Color(1, 1, 0, 0.5f);
                GUI.DrawTexture(new Rect(frontWidth, 0, fadeInWidth, 20f), Texture2D.whiteTexture);
            }
            if (fadeEndDuration > 0)
            {
                float fadeOutWidth = fadeEndDuration * pixelsPerSecond;
                GUI.color = new Color(1, 1, 0, 0.5f);
                GUI.DrawTexture(new Rect(frontWidth + audioWidth - fadeOutWidth, 0, fadeOutWidth, 20f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            GUI.EndGroup();
            GUI.EndClip();

            // 支持点击seek
            if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
            {
                float clickPos = Event.current.mousePosition.x - previewRect.x + horizontalScroll;
                float seekTime = clickPos / pixelsPerSecond;
                if (previewAudioSource.clip != null)
                {
                    previewAudioSource.time = seekTime;
                    currentTime = seekTime;
                }
            }

            // 快捷键支持（左右滑动）
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
                    horizontalScroll = Mathf.Min(totalWidth - displayWidth, horizontalScroll + 10);
                    Event.current.Use();
                    Repaint();
                }
            }
        }

        private void PlayPreview()
        {
            if (previewAudioSource == null || audioClip == null)
                return;

            if (isPlaying)
            {
                StopPreview();
            }

            trimmedSamples = TrimAndFadeAudioSamples(audioClip, startTrim, endTrim, fadeStartDuration, fadeEndDuration, frontPadding, backPadding);
            AudioClip trimmedClip = AudioClip.Create("裁剪片段", trimmedSamples.Length / audioClip.channels, audioClip.channels, audioClip.frequency, false);
            trimmedClip.SetData(trimmedSamples, 0);
            previewAudioSource.loop = loopPreview;
            previewAudioSource.clip = trimmedClip;
            previewAudioSource.volume = 1f;
            previewAudioSource.Play();

            isPlaying = true;
        }

        private void StopPreview()
        {
            if (previewAudioSource == null || !isPlaying)
                return;

            previewAudioSource.Stop();
            isPlaying = false;
        }

        private float[] TrimAndFadeAudioSamples(AudioClip clip, float startTrim, float endTrim, float fadeStartDuration, float fadeEndDuration, float frontPadding, float backPadding)
        {
            int freq = clip.frequency;
            int channels = clip.channels;
            float[] samples = new float[clip.samples * channels];
            clip.GetData(samples, 0);

            int startSample = Mathf.FloorToInt(startTrim * freq * channels);
            int endSample = Mathf.FloorToInt(endTrim * freq * channels);

            // Clamp to prevent index out of range
            startSample = Mathf.Clamp(startSample, 0, samples.Length);
            endSample = Mathf.Clamp(endSample, startSample, samples.Length);

            int trimSamples = endSample - startSample;

            int frontPaddingSamples = Mathf.FloorToInt(frontPadding * freq * channels);
            int backPaddingSamples = Mathf.FloorToInt(backPadding * freq * channels);
            int totalSamples = frontPaddingSamples + trimSamples + backPaddingSamples;

            float[] paddedSamples = new float[totalSamples];

            for (int i = 0; i < frontPaddingSamples; i++)
            {
                paddedSamples[i] = 0f;
            }

            for (int i = 0; i < trimSamples; i++)
            {
                paddedSamples[frontPaddingSamples + i] = samples[startSample + i];
            }

            for (int i = frontPaddingSamples + trimSamples; i < totalSamples; i++)
            {
                paddedSamples[i] = 0f;
            }

            int fadeInSampleCount = Mathf.FloorToInt(fadeStartDuration * freq * channels);
            int fadeOutSampleCount = Mathf.FloorToInt(fadeEndDuration * freq * channels);

            for (int i = 0; i < fadeInSampleCount && i + frontPaddingSamples < paddedSamples.Length; i++)
            {
                float fadeFactor = (float)i / fadeInSampleCount;
                paddedSamples[frontPaddingSamples + i] *= fadeFactor;
            }

            if (fadeEndDuration > 0)
            {
                int fadeOutStart = frontPaddingSamples + trimSamples - fadeOutSampleCount;
                for (int i = 0; i < fadeOutSampleCount && fadeOutStart + i < paddedSamples.Length; i++)
                {
                    float fadeFactor = 1f - ((float)i / fadeOutSampleCount);
                    paddedSamples[fadeOutStart + i] *= fadeFactor;
                }
            }

            return paddedSamples;
        }

        private void TrimAndFadeAudioClip()
        {
            if (audioClip == null)
            {
                Debug.LogError("未选择要裁剪的音频片段。");
                return;
            }

            float length = endTrim - startTrim;
            if (length <= 0)
            {
                Debug.LogError("无效的裁剪值。结束裁剪必须大于起始裁剪。");
                return;
            }

            string originalPath = AssetDatabase.GetAssetPath(audioClip);
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogError("无法获取所选音频片段的路径。");
                return;
            }

            string directory = Path.GetDirectoryName(originalPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
            string extension = useFFmpeg ? outputFormat : "wav";
            string newPath = Path.Combine(directory, fileNameWithoutExtension + "_已编辑." + extension);

            string tempWavPath = Path.Combine(directory, fileNameWithoutExtension + "_temp.wav");
            AudioClip trimmedClip = TrimAndFadeClip(audioClip, startTrim, length, fadeStartDuration, fadeEndDuration, frontPadding, backPadding);
            SaveAsWav(trimmedClip, tempWavPath);

            if (useFFmpeg && !string.IsNullOrEmpty(ffmpegPath) && outputFormat != "wav")
            {
                ConvertWithFFmpeg(tempWavPath, newPath, outputFormat);
                File.Delete(tempWavPath);
            }
            else
            {
                if (File.Exists(newPath))
                {
                    File.Delete(newPath);
                }
                File.Move(tempWavPath, newPath);
            }

            Debug.Log($"裁剪并淡化的音频片段已保存到 {newPath}");
            AssetDatabase.Refresh();
        }

        private void ConvertWithFFmpeg(string inputPath, string outputPath, string format)
        {
            string codec = "";
            string defaultArgs = "";
            if (format == "mp3")
            {
                codec = "-c:a libmp3lame";
                defaultArgs = "-b:a 192k";
            }
            else if (format == "ogg")
            {
                codec = "-c:a libvorbis";
                defaultArgs = "-q:a 5";
            }
            else
            {
                Debug.LogError("不支持的输出格式: " + format);
                return;
            }

            string arguments = $"-i \"{inputPath}\" {codec} {defaultArgs} {additionalArgs} \"{outputPath}\"";

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        Debug.LogError("FFmpeg转换失败: " + error);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("运行FFmpeg时出错: " + ex.Message);
            }
        }

        private AudioClip TrimAndFadeClip(AudioClip clip, float startTime, float length, float fadeStartDuration, float fadeEndDuration, float frontPadding, float backPadding)
        {
            float[] data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);

            int freq = clip.frequency;
            int channels = clip.channels;

            int startSample = Mathf.FloorToInt(startTime * freq * channels);
            int endSample = Mathf.FloorToInt((startTime + length) * freq * channels);

            // Clamp to prevent index out of range
            startSample = Mathf.Clamp(startSample, 0, data.Length);
            endSample = Mathf.Clamp(endSample, startSample, data.Length);

            int trimSamples = endSample - startSample;

            int frontPaddingSamples = Mathf.FloorToInt(frontPadding * freq * channels);
            int backPaddingSamples = Mathf.FloorToInt(backPadding * freq * channels);
            int totalSamples = frontPaddingSamples + trimSamples + backPaddingSamples;

            float[] paddedData = new float[totalSamples];

            for (int i = 0; i < frontPaddingSamples; i++)
            {
                paddedData[i] = 0f;
            }

            for (int i = 0; i < trimSamples; i++)
            {
                paddedData[frontPaddingSamples + i] = data[startSample + i];
            }

            for (int i = frontPaddingSamples + trimSamples; i < totalSamples; i++)
            {
                paddedData[i] = 0f;
            }

            if (fadeStartDuration > 0)
            {
                int fadeStartSamples = Mathf.FloorToInt(fadeStartDuration * freq * channels);
                for (int i = 0; i < fadeStartSamples && i + frontPaddingSamples < paddedData.Length; i++)
                {
                    float fadeFactor = (float)i / fadeStartSamples;
                    paddedData[frontPaddingSamples + i] *= fadeFactor;
                }
            }

            if (fadeEndDuration > 0)
            {
                int fadeOutSampleCount = Mathf.FloorToInt(fadeEndDuration * freq * channels);
                int fadeOutStart = frontPaddingSamples + trimSamples - fadeOutSampleCount;
                for (int i = 0; i < fadeOutSampleCount && fadeOutStart + i < paddedData.Length; i++)
                {
                    float fadeFactor = 1f - ((float)i / fadeOutSampleCount);
                    paddedData[fadeOutStart + i] *= fadeFactor;
                }
            }

            AudioClip newClip = AudioClip.Create(clip.name + "_已编辑", totalSamples / channels, channels, freq, false);
            newClip.SetData(paddedData, 0);

            return newClip;
        }

        private void SaveAsWav(AudioClip clip, string path)
        {
            if (clip == null)
            {
                Debug.LogError("音频片段为空，无法保存为 WAV。");
                return;
            }

            byte[] wavData = WavUtility.FromAudioClip(clip);
            File.WriteAllBytes(path, wavData);
        }
    }
}