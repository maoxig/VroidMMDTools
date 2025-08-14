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
        private float volumeScale = 1f; // 音量缩放因子
        private float frontPadding = 0f; // 前部沉默时长（秒）
        private float backPadding = 0f; // 后部沉默时长（秒）

        // FFmpeg 配置
        private bool useFFmpeg = false;
        private string ffmpegPath = ""; // FFmpeg可执行文件路径
        private string outputFormat = "ogg"; // 输出格式：wav, mp3, ogg
        private string additionalArgs = ""; // 附加FFmpeg参数

        private Texture2D waveformTexture;
        private const int waveformWidth = 500; // 降低宽度以优化性能
        private const int waveformHeight = 100;
        private const string FFMPEG_PATH_KEY = "SimblendTools_AudioEditor_FFmpegPath"; // EditorPrefs键

        private Vector2 scrollPosition = Vector2.zero; // 用于整体滚动视图
        private Rect waveformRect; // 存储波形图的矩形以用于点击seek
        private float currentTime = 0f; // 当前播放时间，用于seek滑块

        [MenuItem("Tools/音频编辑器")]
        public static void ShowWindow()
        {
            GetWindow<AudioEditor>("音频编辑器");
        }

        private void OnEnable()
        {
            if (previewAudioSource == null)
            {
                GameObject audioPreviewer = new GameObject("音频预览器");
                previewAudioSource = audioPreviewer.AddComponent<AudioSource>();
                previewAudioSource.hideFlags = HideFlags.HideAndDontSave;
            }
            // 加载保存的FFmpeg路径
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
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition); // 整体滚动视图

            GUILayout.Label("裁剪与淡入淡出音频片段", EditorStyles.boldLabel);

            audioClip = (AudioClip)EditorGUILayout.ObjectField("音频片段", audioClip, typeof(AudioClip), false);

            if (audioClip != null)
            {
                if (waveformTexture == null || GUILayout.Button("生成波形图"))
                {
                    waveformTexture = DrawWaveform(audioClip, waveformWidth, waveformHeight, new Color(1, 0.5f, 0), startTrim, endTrim, fadeStartDuration, fadeEndDuration);
                }

                if (waveformTexture != null)
                {
                    GUILayout.Label("波形预览");
                    waveformRect = GUILayoutUtility.GetRect(waveformWidth, waveformHeight);
                    GUI.DrawTexture(waveformRect, waveformTexture);

                    if (isPlaying)
                    {
                        float playheadPosition = (previewAudioSource.time / previewAudioSource.clip.length) * waveformRect.width;
                        Rect playheadRect = new Rect(waveformRect.x + playheadPosition, waveformRect.y, 2, waveformHeight);
                        EditorGUI.DrawRect(playheadRect, Color.red);
                    }

                    // 添加点击seek功能
                    if (Event.current.type == EventType.MouseDown && waveformRect.Contains(Event.current.mousePosition))
                    {
                        float clickPosition = Event.current.mousePosition.x - waveformRect.x;
                        float seekTime = (clickPosition / waveformRect.width) * (endTrim - startTrim);
                        if (previewAudioSource.clip != null)
                        {
                            previewAudioSource.time = seekTime;
                            currentTime = seekTime;
                        }
                    }
                }

                startTrim = EditorGUILayout.Slider("起始裁剪 (秒)", startTrim, 0f, audioClip.length);
                endTrim = EditorGUILayout.Slider("结束裁剪 (秒)", endTrim, 0f, audioClip.length);
                fadeStartDuration = EditorGUILayout.Slider("淡入时长 (秒)", fadeStartDuration, 0f, endTrim - startTrim);
                fadeEndDuration = EditorGUILayout.Slider("淡出时长 (秒)", fadeEndDuration, 0f, endTrim - startTrim);
                volumeScale = EditorGUILayout.Slider("音量缩放 (0-2, 慎重调节)", volumeScale, 0f, 2f);
                frontPadding = EditorGUILayout.Slider("前部沉默添加 (秒)", frontPadding, 0f, 10f);
                backPadding = EditorGUILayout.Slider("后部沉默添加 (秒)", backPadding, 0f, 10f);
                loopPreview = GUILayout.Toggle(loopPreview, "循环预览");

                // 添加seek滑块
                if (previewAudioSource.clip != null)
                {
                    currentTime = EditorGUILayout.Slider("当前播放时间 (秒)", currentTime, 0f, previewAudioSource.clip.length);
                    if (GUI.changed && !isPlaying)
                    {
                        previewAudioSource.time = currentTime;
                    }
                }

                // FFmpeg 配置区域
                GUILayout.Space(10);
                GUILayout.Label("可选FFmpeg输出配置", EditorStyles.boldLabel);
                useFFmpeg = EditorGUILayout.Toggle("使用FFmpeg保存 (需安装FFmpeg)", useFFmpeg);
                if (useFFmpeg)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    ffmpegPath = EditorGUILayout.TextField("FFmpeg路径", ffmpegPath);
                    if (GUILayout.Button("浏览", GUILayout.Width(60)))
                    {
                        string selectedPath = EditorUtility.OpenFilePanel("选择FFmpeg可执行文件", "", "");
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            ffmpegPath = selectedPath;
                            EditorPrefs.SetString(FFMPEG_PATH_KEY, ffmpegPath); // 保存路径
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(5);
                    outputFormat = EditorGUILayout.TextField("输出格式 (wav/mp3/ogg)", outputFormat).ToLower();
                    additionalArgs = EditorGUILayout.TextField("附加FFmpeg参数 (例如 -b:a 192k)", additionalArgs);

                    GUILayout.Space(5);
                    if (GUILayout.Button("查看FFmpeg文档"))
                    {
                        Application.OpenURL("https://ffmpeg.org/documentation.html");
                    }
                }

                if (GUI.changed)
                {
                    waveformTexture = DrawWaveform(audioClip, waveformWidth, waveformHeight, new Color(1, 0.5f, 0), startTrim, endTrim, fadeStartDuration, fadeEndDuration);
                }

                if (!isAudioAdded)
                {
                    endTrim = audioClip.length;
                    isAudioAdded = true;
                }

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
            else
            {
                isAudioAdded = false;
            }

            EditorGUILayout.EndScrollView(); // 结束整体滚动视图

            if (isPlaying)
            {
                currentTime = previewAudioSource.time;
                Repaint();
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
            previewAudioSource.volume = volumeScale;
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
            int startSample = Mathf.FloorToInt(startTrim * clip.frequency * clip.channels);
            int endSample = Mathf.FloorToInt(endTrim * clip.frequency * clip.channels);
            int trimSamples = endSample - startSample;

            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            int frontPaddingSamples = Mathf.FloorToInt(frontPadding * clip.frequency * clip.channels);
            int backPaddingSamples = Mathf.FloorToInt(backPadding * clip.frequency * clip.channels);
            int totalSamples = frontPaddingSamples + trimSamples + backPaddingSamples;

            float[] paddedSamples = new float[totalSamples];

            for (int i = 0; i < frontPaddingSamples; i++)
            {
                paddedSamples[i] = 0f;
            }

            for (int i = 0; i < trimSamples; i++)
            {
                paddedSamples[frontPaddingSamples + i] = samples[startSample + i] * volumeScale;
            }

            for (int i = frontPaddingSamples + trimSamples; i < totalSamples; i++)
            {
                paddedSamples[i] = 0f;
            }

            int fadeInSampleCount = Mathf.FloorToInt(fadeStartDuration * clip.frequency * clip.channels);
            int fadeOutSampleCount = Mathf.FloorToInt(fadeEndDuration * clip.frequency * clip.channels);

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

        private Texture2D DrawWaveform(AudioClip clip, int width, int height, Color waveformColor, float startTrim, float endTrim, float fadeStartDuration, float fadeEndDuration)
        {
            Texture2D texture = new Texture2D(width, height);
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            Color[] colors = new Color[width * height];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color(0.2f, 0.2f, 0.2f); // 背景色
            }

            int startSample = Mathf.FloorToInt(startTrim * clip.frequency * clip.channels);
            int endSample = Mathf.FloorToInt(endTrim * clip.frequency * clip.channels);
            int trimSamples = endSample - startSample;

            int fadeInSampleCount = Mathf.FloorToInt(fadeStartDuration * clip.frequency * clip.channels);
            int fadeOutSampleCount = Mathf.FloorToInt(fadeEndDuration * clip.frequency * clip.channels);

            int packSize = Mathf.Max(1, (trimSamples / width) + 1); // 防止packSize为0

            for (int i = 0; i < width; i++)
            {
                float max = 0;
                for (int j = 0; j < packSize; j++)
                {
                    int index = startSample + (i * packSize) + j;
                    if (index < samples.Length && index >= 0)
                    {
                        float wavePeak = Mathf.Abs(samples[index]) * volumeScale;
                        wavePeak = Mathf.Min(wavePeak, 1f); // 假设样本值标准化到[-1, 1]

                        int currentSampleIndex = i * packSize;
                        if (currentSampleIndex < fadeInSampleCount)
                        {
                            float fadeFactor = (float)currentSampleIndex / fadeInSampleCount;
                            wavePeak *= fadeFactor;
                        }

                        if (currentSampleIndex > trimSamples - fadeOutSampleCount)
                        {
                            float fadeFactor = (float)(trimSamples - currentSampleIndex) / fadeOutSampleCount;
                            wavePeak *= fadeFactor;
                        }

                        if (wavePeak > max) max = wavePeak;
                    }
                }

                int heightPos = Mathf.FloorToInt(max * (height / 2));
                heightPos = Mathf.Clamp(heightPos, 0, height / 2 - 1);
                for (int j = 0; j < heightPos; j++)
                {
                    int pixelIndexUp = (height / 2 + j) * width + i;
                    int pixelIndexDown = (height / 2 - j) * width + i;
                    if (pixelIndexUp < colors.Length && pixelIndexDown >= 0)
                    {
                        colors[pixelIndexUp] = waveformColor;
                        colors[pixelIndexDown] = waveformColor;
                    }
                }
            }

            texture.SetPixels(colors);
            texture.Apply();

            return texture;
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

            // 始终先保存为WAV
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

            int startSample = Mathf.FloorToInt(startTime * clip.frequency * clip.channels);
            int endSample = Mathf.FloorToInt((startTime + length) * clip.frequency * clip.channels);
            int trimSamples = endSample - startSample;

            int frontPaddingSamples = Mathf.FloorToInt(frontPadding * clip.frequency * clip.channels);
            int backPaddingSamples = Mathf.FloorToInt(backPadding * clip.frequency * clip.channels);
            int totalSamples = frontPaddingSamples + trimSamples + backPaddingSamples;

            float[] paddedData = new float[totalSamples];

            for (int i = 0; i < frontPaddingSamples; i++)
            {
                paddedData[i] = 0f;
            }

            for (int i = 0; i < trimSamples; i++)
            {
                paddedData[frontPaddingSamples + i] = data[startSample + i] * volumeScale;
            }

            for (int i = frontPaddingSamples + trimSamples; i < totalSamples; i++)
            {
                paddedData[i] = 0f;
            }

            if (fadeStartDuration > 0)
            {
                int fadeStartSamples = Mathf.FloorToInt(fadeStartDuration * clip.frequency * clip.channels);
                for (int i = 0; i < fadeStartSamples && i + frontPaddingSamples < paddedData.Length; i++)
                {
                    float fadeFactor = (float)i / fadeStartSamples;
                    paddedData[frontPaddingSamples + i] *= fadeFactor;
                }
            }

            if (fadeEndDuration > 0)
            {
                int fadeOutSampleCount = Mathf.FloorToInt(fadeEndDuration * clip.frequency * clip.channels);
                int fadeOutStart = frontPaddingSamples + trimSamples - fadeOutSampleCount;
                for (int i = 0; i < fadeOutSampleCount && fadeOutStart + i < paddedData.Length; i++)
                {
                    float fadeFactor = 1f - ((float)i / fadeOutSampleCount);
                    paddedData[fadeOutStart + i] *= fadeFactor;
                }
            }

            AudioClip newClip = AudioClip.Create(clip.name + "_已编辑", totalSamples / clip.channels, clip.channels, clip.frequency, false);
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