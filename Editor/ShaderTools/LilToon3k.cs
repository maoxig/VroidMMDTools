using UnityEditor;
using UnityEngine;
public class SetLilToonRenderQueue : EditorWindow
{
    [MenuItem("ShaderTools/Set LilToon Render Queue to 3000")]
    public static void SetRenderQueue()
    {
        // 获取当前选中的GameObject
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            Debug.LogError("未选择GameObject！");
            return;
        }
        // 获取对象及其所有子对象上的Renderer
        Renderer[] renderers = selectedObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError("选中的GameObject或其子对象未找到Renderer！");
            return;
        }
        bool modified = false;
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }
                string shaderName = material.shader.name.ToLower();
                if (!shaderName.Contains("liltoon"))
                {
                    continue;
                }
                // 记录Undo
                Undo.RecordObject(material, "Set Render Queue to 3000");
                // 强制设置渲染队列为3000
                material.renderQueue = 3000;
                modified = true;
            }
        }
        if (modified)
        {
            // 更新资产数据库
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("已将所有lilToon材质的渲染队列设置为3000！");
        }
        else
        {
            Debug.Log("未找到lilToon材质，无需修改。");
        }
    }
}