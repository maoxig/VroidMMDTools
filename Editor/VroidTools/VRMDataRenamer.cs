using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class VRoidDataRenamer : EditorWindow
{
    private string originalName = "";
    private string newName = "";
    private string selectedFolderPath = "";

    // 用于记录重命名操作，支持撤销
    private static RenameOperation lastOperation;

    // 重命名操作的数据结构
    private class RenameOperation
    {
        public string originalFolderPath;
        public string newFolderPath;
        public List<FileSystemInfo> originalItems = new List<FileSystemInfo>();
        public List<string> newPaths = new List<string>();
    }

    // 顶部菜单
    [MenuItem("VRoidTools/Rename Data Folder")]
    public static void ShowWindow()
    {
        GetWindow<VRoidDataRenamer>("Rename Data Folder");
    }

    // 验证右键菜单是否可用
    [MenuItem("Assets/VRoidTools/Rename Data Folder", true)]
    public static bool ValidateRename()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;
        string folderName = Path.GetFileName(path);
        return folderName != null && folderName.EndsWith(".Data");
    }

    // 右键菜单
    [MenuItem("Assets/VRoidTools/Rename Data Folder")]
    public static void ShowContextMenu()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        ShowWindowWithPath(path);
    }

    // 撤销操作菜单
    [MenuItem("VRoidTools/Undo Last Rename")]
    public static void UndoLastRename()
    {
        if (lastOperation == null)
        {
            EditorUtility.DisplayDialog("提示", "没有可撤销的操作", "确定");
            return;
        }

        // 撤销时需要反向操作，先处理内部文件，再处理主文件夹
        for (int i = lastOperation.originalItems.Count - 1; i >= 0; i--)
        {
            FileSystemInfo item = lastOperation.originalItems[i];
            string newPath = lastOperation.newPaths[i];

            if (Directory.Exists(newPath) && !Directory.Exists(item.FullName))
            {
                Directory.Move(newPath, item.FullName);
            }
            else if (File.Exists(newPath) && !File.Exists(item.FullName))
            {
                File.Move(newPath, item.FullName);
            }
        }

        // 还原主文件夹名称
        if (Directory.Exists(lastOperation.newFolderPath) && !Directory.Exists(lastOperation.originalFolderPath))
        {
            Directory.Move(lastOperation.newFolderPath, lastOperation.originalFolderPath);
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完成", "已撤销上一次重命名操作", "确定");
        lastOperation = null;
    }

    // 带路径显示窗口
    public static void ShowWindowWithPath(string path)
    {
        VRoidDataRenamer window = GetWindow<VRoidDataRenamer>("Rename Data Folder");
        window.selectedFolderPath = path;
        window.originalName = Path.GetFileNameWithoutExtension(path);
        window.newName = window.originalName;
    }

    private void OnGUI()
    {
        GUILayout.Label("Data Folder Renamer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (string.IsNullOrEmpty(selectedFolderPath))
        {
            EditorGUILayout.HelpBox("请在Project窗口中选择一个.Data文件夹，然后通过VRoidTools菜单或右键菜单打开此工具", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("选中的文件夹:", selectedFolderPath);
        originalName = EditorGUILayout.TextField("原始名称:", originalName);
        newName = EditorGUILayout.TextField("新名称:", newName);

        EditorGUILayout.Space();

        if (GUILayout.Button("执行重命名"))
        {
            if (string.IsNullOrEmpty(originalName) || string.IsNullOrEmpty(newName))
            {
                EditorUtility.DisplayDialog("错误", "名称不能为空", "确定");
                return;
            }

            if (originalName == newName)
            {
                EditorUtility.DisplayDialog("提示", "新名称与原始名称相同，无需重命名", "确定");
                return;
            }

            PerformRename();
        }
    }

    private void PerformRename()
    {
        lastOperation = new RenameOperation();
        lastOperation.originalFolderPath = selectedFolderPath;

        string parentDir = Path.GetDirectoryName(selectedFolderPath);
        string newFolderName = newName + ".Data";
        string newFolderPath = Path.Combine(parentDir, newFolderName);
        lastOperation.newFolderPath = newFolderPath;

        // 记录原始文件夹信息
        DirectoryInfo originalDir = new DirectoryInfo(selectedFolderPath);
        lastOperation.originalItems.Add(originalDir);
        lastOperation.newPaths.Add(newFolderPath);

        try
        {
            // 先重命名子文件和子文件夹
            RenameItemsInDirectory(originalDir, originalName, newName, lastOperation);

            // 再重命名主文件夹
            Directory.Move(selectedFolderPath, newFolderPath);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", "重命名操作已完成", "确定");
            Close();
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("错误", $"重命名过程中发生错误: {ex.Message}", "确定");
            lastOperation = null;
        }
    }

    private void RenameItemsInDirectory(DirectoryInfo directory, string original, string replacement, RenameOperation operation)
    {
        // 处理子文件夹
        foreach (DirectoryInfo subDir in directory.GetDirectories())
        {
            if (subDir.Name.StartsWith(original))
            {
                string newDirName = replacement + subDir.Name.Substring(original.Length);
                string newDirPath = Path.Combine(subDir.Parent.FullName, newDirName);

                // 记录操作
                operation.originalItems.Add(subDir);
                operation.newPaths.Add(newDirPath);

                // 递归处理子文件夹内容
                RenameItemsInDirectory(subDir, original, replacement, operation);

                // 重命名文件夹
                subDir.MoveTo(newDirPath);
            }
            else
            {
                // 递归处理子文件夹
                RenameItemsInDirectory(subDir, original, replacement, operation);
            }
        }

        // 处理文件
        foreach (FileInfo file in directory.GetFiles())
        {
            if (file.Name.StartsWith(original))
            {
                string newFileName = replacement + file.Name.Substring(original.Length);
                string newFilePath = Path.Combine(file.Directory.FullName, newFileName);

                // 记录操作
                operation.originalItems.Add(file);
                operation.newPaths.Add(newFilePath);

                // 重命名文件
                file.MoveTo(newFilePath);
            }
        }
    }

}
