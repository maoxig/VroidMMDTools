using UnityEditor;
using UnityEngine;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;

// 有一些bug，先不启用
public class PMXProcessingTool
{
    // PMX2FBX工具路径
    private const string PMX2FBXPath = "Assets/MMD4Mecanim/Editor/PMX2FBX/pmx2fbx.exe";
    private const string PMX2FBXConfigPath = "Assets/MMD4Mecanim/Editor/PMX2FBX/pmx2fbx.xml";
    private const string PMX2FBXConfigBackupPath = "Assets/MMD4Mecanim/Editor/PMX2FBX/pmx2fbx_backup.xml";

    // 配置文件内容
    private const string ConfigTemplate = @"
<PMX2FBXConfig  xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <globalSettings>
        <editorAdvancedMode>true</editorAdvancedMode>
        <morphRenameFlag>0</morphRenameFlag>
        <prefixMorphNoNameFlag>0</prefixMorphNoNameFlag>
        <splitMeshFlag>0</splitMeshFlag>
    </globalSettings>
    <bulletPhysics>
        <enabled>0</enabled>
    </bulletPhysics>
</PMX2FBXConfig>";

    //[MenuItem("Assets/MMD/处理选中的PMX模型", false, 1000)]
    public static void ProcessSelectedPMXModel_Context()
    {
        ProcessSelectedPMXModel();
    }

    //[MenuItem("VRoidTools/处理选中的PMX模型")]
    public static void ProcessSelectedPMXModel()
    {

        // 获取选中的PMX文件
        Object[] selectedObjects = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("错误", "请先在项目视图中选择一个PMX文件", "确定");
            return;
        }

        string pmxPath = AssetDatabase.GetAssetPath(selectedObjects[0]);
        if (string.IsNullOrEmpty(pmxPath) || !pmxPath.EndsWith(".pmx"))
        {
            EditorUtility.DisplayDialog("错误", "请选择一个有效的PMX文件", "确定");
            return;
        }

        // 将项目路径转换为文件系统路径
        string fullPmxPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, pmxPath);

        // 处理PMX文件
        try
        {
            // 1. 使用PMX2FBX转换为FBX（会备份配置文件并恢复）
            string fbxPath = ConvertToFBX(fullPmxPath);

            // 2. 设置类人骨骼和外部材质
            if (!string.IsNullOrEmpty(fbxPath))
            {
                SetupFBXImportSettings(fbxPath);
                AssetDatabase.Refresh();

                // 3. 检查FBX中的重复morph
                CheckFBXForDuplicateMorphs(fbxPath);

                EditorUtility.DisplayDialog("成功", "PMX模型处理完成!", "确定");
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("错误", "处理PMX模型时出错: " + e.Message, "确定");
        }
    }

    // 使用PMX2FBX工具将PMX转换为FBX
    private static string ConvertToFBX(string pmxPath)
    {
        try
        {
            // 检查PMX2FBX工具是否存在
            string fullToolPath = Path.Combine(Application.dataPath, PMX2FBXPath.Substring(7));
            if (!File.Exists(fullToolPath))
            {
                EditorUtility.DisplayDialog("错误", "找不到PMX2FBX工具: " + fullToolPath, "确定");
                return null;
            }

            // 确定输出FBX路径（与PMX在同一目录）
            string fbxPath = Path.ChangeExtension(pmxPath, ".fbx");

            // 备份当前配置文件
            string fullConfigPath = Path.Combine(Application.dataPath, PMX2FBXConfigPath.Substring(7));
            string fullBackupPath = Path.Combine(Application.dataPath, PMX2FBXConfigBackupPath.Substring(7));

            // 备份现有配置文件
            if (File.Exists(fullConfigPath))
            {
                File.Copy(fullConfigPath, fullBackupPath, true);
                UnityEngine.Debug.Log("已备份PMX2FBX配置文件");
            }

            try
            {
                // 写入新配置文件
                File.WriteAllText(fullConfigPath, ConfigTemplate);
                UnityEngine.Debug.Log("已写入自定义PMX2FBX配置文件");

                // 执行PMX2FBX
                // 构造同级同名的config路径
                string pmxConfigPath = Path.ChangeExtension(pmxPath, ".xml");
                // Debug.Log("PMX配置文件路径: " + pmxConfigPath);
                Debug.Log("PMX转换为FBX路径: " + fbxPath + ", 使用配置: " + pmxConfigPath);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fullToolPath,
                    Arguments = $"\"{pmxPath}\" /config:\"{pmxConfigPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    // string output = process.StandardOutput.ReadToEnd();
                    // string error = process.StandardError.ReadToEnd();
                    // process.WaitForExit();

                    // if (process.ExitCode != 0)
                    // {
                    //     Debug.LogError("PMX2FBX转换失败: " + error);
                    //     EditorUtility.DisplayDialog("错误", "PMX2FBX转换失败: " + error, "确定");
                    //     return null;
                    // }

                    Debug.Log("PMX转换为FBX成功: " + fbxPath);
                    return fbxPath;
                }
            }
            finally
            {
                // 恢复原始配置文件
                if (File.Exists(fullBackupPath))
                {
                    File.Copy(fullBackupPath, fullConfigPath, true);
                    File.Delete(fullBackupPath);
                    UnityEngine.Debug.Log("已恢复PMX2FBX原始配置文件");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("转换PMX为FBX时出错: " + e.Message);
            EditorUtility.DisplayDialog("错误", "转换PMX为FBX时出错: " + e.Message, "确定");
            return null;
        }
    }

    // 设置FBX导入设置
    private static void SetupFBXImportSettings(string fbxPath)
    {
        try
        {
            // 将绝对路径转换为Unity资源路径
            string assetPath = "Assets" + fbxPath.Substring(Application.dataPath.Length);

            // 获取并修改导入设置
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("无法获取FBX导入设置: " + assetPath);
                return;
            }

            // 设置为类人骨骼
            importer.animationType = ModelImporterAnimationType.Human;
            // 不需要手动设置humanDescription，Unity会自动处理

            // 设置使用外部材质
            importer.materialLocation = ModelImporterMaterialLocation.External;

            // 应用设置
            importer.SaveAndReimport();
            Debug.Log("FBX导入设置已更新: " + assetPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("设置FBX导入设置时出错: " + e.Message);
        }
    }

    // 检查FBX中的重复morph
    private static void CheckFBXForDuplicateMorphs(string fbxPath)
    {
        try
        {
            // 等待资源导入完成
            AssetDatabase.Refresh();
            EditorUtility.UnloadUnusedAssetsImmediate();

            // 将绝对路径转换为Unity资源路径
            string assetPath = "Assets" + fbxPath.Substring(Application.dataPath.Length);

            // 加载FBX预制体
            GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (fbxPrefab == null)
            {
                Debug.LogError("无法加载FBX预制体: " + assetPath);
                return;
            }

            // 查找U_char根对象
            Transform uCharRoot = FindUCharRoot(fbxPrefab.transform);
            if (uCharRoot == null)
            {
                Debug.Log("在FBX中未找到U_char根对象");
                return;
            }

            // 检查所有子对象中的SkinnedMeshRenderer
            Dictionary<string, int> morphCounts = new Dictionary<string, int>();
            SkinnedMeshRenderer[] renderers = uCharRoot.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer.sharedMesh == null)
                    continue;

                int blendShapeCount = renderer.sharedMesh.blendShapeCount;
                for (int i = 0; i < blendShapeCount; i++)
                {
                    string blendShapeName = renderer.sharedMesh.GetBlendShapeName(i);
                    if (morphCounts.ContainsKey(blendShapeName))
                        morphCounts[blendShapeName]++;
                    else
                        morphCounts[blendShapeName] = 1;
                }
            }

            // 收集所有重复的morph
            List<string> duplicateMorphs = new List<string>();
            foreach (var pair in morphCounts)
            {
                if (pair.Value > 1)
                    duplicateMorphs.Add(pair.Key);
            }

            // 如果有重复的morph，显示警告
            if (duplicateMorphs.Count > 0)
            {
                string message = "发现 " + duplicateMorphs.Count + " 个重复的morph:\n";
                foreach (string morph in duplicateMorphs)
                    message += "- " + morph + "\n";

                EditorUtility.DisplayDialog("警告", message, "确定");
            }
        }
        catch (System.Exception e)
        {
            // 记录错误但不中断处理
            Debug.LogError("检查FBX重复morph时出错: " + e.Message);
        }
    }

    // 查找U_char根对象
    private static Transform FindUCharRoot(Transform root)
    {
        // 检查自身
        if (root.name == "U_char" || root.name.StartsWith("U_char_"))
            return root;

        // 检查子对象
        foreach (Transform child in root)
        {
            Transform result = FindUCharRoot(child);
            if (result != null)
                return result;
        }

        return null;
    }
}