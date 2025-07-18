// 文件路径：Assets/AnimConverter/Editor/ToolConfigManager.cs
using System;
using System.IO;
using UnityEngine;

namespace Assets.AnimConverter.Editor
{
    [System.Serializable]
    public class ToolConfig
    {
        // 1. SkinnedMeshRenderer配置
        public string defaultSkinnedMeshPath = "Body";
        public string defaultSkinnedMeshName = "Body";
        public bool directMappingMode = true;

        // 2. 相机动画配置
        public bool enableCameraAnimation = false;

        // 3. 相机层级配置
        public string cameraRootPath = "Camera_root";
        public string cameraComponentPath = "Camera_root/Camera_root_1/Camera";
        public string cameraDistancePath = "Camera_root/Camera_root_1";

        // 4. 自动打包输出路径
        public string bundleOutputPath = "Assets/AnimConverter/Output/";
    }

    public class ToolConfigManager
    {
        private string configFilePath;
        private ToolConfig config;

        public ToolConfig Config => config;

        public ToolConfigManager(string configFileName = "AnimConverterConfig.json")
        {
            // 配置文件路径（放在Editor目录下）
            configFilePath = Path.Combine(Application.dataPath, "AnimConverter/Editor/", configFileName);
            LoadConfig();
        }

        // 加载配置
        public void LoadConfig()
        {
            if (File.Exists(configFilePath))
            {
                try
                {
                    string json = File.ReadAllText(configFilePath);
                    config = JsonUtility.FromJson<ToolConfig>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"加载配置失败，使用默认配置: {e.Message}");
                    config = new ToolConfig();
                }
            }
            else
            {
                config = new ToolConfig(); // 首次使用默认配置
                SaveConfig(); // 创建默认配置文件
            }
        }

        // 保存配置
        public void SaveConfig()
        {
            try
            {
                // 确保目录存在
                string dir = Path.GetDirectoryName(configFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(configFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"保存配置失败: {e.Message}");
            }
        }
    }
}