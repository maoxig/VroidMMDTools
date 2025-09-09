#if USE_VROID_MOD
using VRoidModSpringBones;  // 仅在VROID模式下引用
using MySpringBoneColliderGroup = VRoidModSpringBones.VRoidSpringBoneColliderGroup;
using MySpringBone = VRoidModSpringBones.VRoidSpringBone;
#elif USE_VRM
using VRM;  // 仅在VRM模式下引用
using MySpringBoneColliderGroup = VRM.VRMSpringBoneColliderGroup;
using MySpringBone = VRM.VRMSpringBone;
#else
// 未定义符号时提示用户配置
#error 请定义 USE_VROID_MOD 或 USE_VRM 符号
#endif

#if USE_VROID_MOD // VRM自带了保存动骨，因此不需要启用
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

using System.Linq;


namespace VRMSpringAutoCreator
{


    public class VRMSpringBoneHelper : MonoBehaviour
    {
        public float m_stiffnessForce;
        public float m_gravityPower;
        public Vector3 m_gravityDir;
        public float m_dragForce;
        public string m_center;
        public List<string> RootBones = new List<string>();
        public float m_hitRadius = 0.02f;
        public List<string> ColliderGroups = new List<string>();
        public int m_updateType;
    }
    internal static class VRoidSpringBoneSaverEditor
    {

        private const string UserMenuPrefix = "VRoidTools";
        // 添加保存按钮（已实现）
        [MenuItem(UserMenuPrefix + "/Save VRoid SpringBone", validate = true, priority = 53)]
        private static bool SaveVRoidSpringBoneValidation() => SaveSpringBoneToJsonValidation();
        [MenuItem(UserMenuPrefix + "/Save VRoid SpringBone", priority = 53)]
        private static void SaveVRoidSpringBone() => SaveSpringBoneToJson();

        // 添加加载按钮（新增）
        [MenuItem(UserMenuPrefix + "/Load VRoid SpringBone", validate = true, priority = 54)]
        private static bool LoadVRoidSpringBoneValidation() => VRoidSpringBoneLoaderEditor.LoadSpringBoneFromJsonValidation();
        [MenuItem(UserMenuPrefix + "/Load VRoid SpringBone", priority = 54)]
        private static void LoadVRoidSpringBone() => VRoidSpringBoneLoaderEditor.LoadSpringBoneFromJson();

        public static bool SaveSpringBoneToJsonValidation()
        {
            if (Selection.activeGameObject == null) return false;
            var springManager = FindSpringManager(Selection.activeGameObject.transform);
            return springManager != null && springManager.GetComponent<MySpringBone>() != null;
        }

        public static void SaveSpringBoneToJson()
        {
            var rootObj = Selection.activeGameObject;
            if (rootObj == null) return;

            var springManager = FindSpringManager(rootObj.transform);
            if (springManager == null)
            {
                EditorUtility.DisplayDialog("错误", "未找到SpringManager", "确定");
                return;
            }

            var springBones = springManager.GetComponents<MySpringBone>();
            if (springBones.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "SpringManager中没有VRoidSpringBone组件", "确定");
                return;
            }

            var helperList = new List<VRMSpringBoneHelper>();
            foreach (var springBone in springBones)
            {
                var helper = new VRMSpringBoneHelper();
                helper.m_stiffnessForce = springBone.m_stiffnessForce;
                helper.m_gravityPower = springBone.m_gravityPower;
                helper.m_gravityDir = springBone.m_gravityDir;
                helper.m_dragForce = springBone.m_dragForce;
                helper.m_hitRadius = springBone.m_hitRadius;

                if (springBone.m_center != null)
                {
                    helper.m_center = GetRelativePath(rootObj.transform, springBone.m_center);
                }

                helper.RootBones = new List<string>();
                foreach (var rootBone in springBone.RootBones)
                {
                    if (rootBone != null)
                    {
                        helper.RootBones.Add(GetRelativePath(rootObj.transform, rootBone));
                    }
                }

                helper.ColliderGroups = new List<string>();
                foreach (var colliderGroup in springBone.ColliderGroups)
                {
                    if (colliderGroup == null) continue;
                    var colliderPath = GetRelativePath(rootObj.transform, colliderGroup.transform);
                    var colliderParams = new List<string>();
                    foreach (var sphere in colliderGroup.Colliders)
                    {
                        colliderParams.Add($"{sphere.Offset.x},{sphere.Offset.y},{sphere.Offset.z};{sphere.Radius}");
                    }
                    helper.ColliderGroups.Add($"{colliderPath}={string.Join("~", colliderParams)}");
                }

                helperList.Add(helper);
            }

            var springBoneJsons = new List<string>();
            foreach (var helper in helperList)
            {
                springBoneJsons.Add(JsonUtility.ToJson(helper));
            }
            var finalContent = $"Spring Bones: {string.Join("|", springBoneJsons)}";

            var path = EditorUtility.SaveFilePanel("Save VRoid SpringBone", null, "VRoidSpringBones.json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, finalContent, Encoding.UTF8);
                EditorUtility.DisplayDialog("成功", "SpringBone数据保存完成", "确定");
            }
        }

        private static GameObject FindSpringManager(Transform root)
        {
            var manager = root.FindInChilds2("SpringManager", false)?.gameObject;
            if (manager != null) return manager;

            var springBones = root.GetComponentsInChildren<MySpringBone>();
            return springBones.Length > 0 ? springBones[0].gameObject : null;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var path = target.name;
            var current = target.parent;
            while (current != root && current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }
            return path;
        }
    }
    internal static class VRoidSpringBoneLoaderEditor
    {
        public static bool LoadSpringBoneFromJsonValidation()
        {
            return Selection.activeGameObject != null;
        }

        public static void LoadSpringBoneFromJson()
        {
            var rootObj = Selection.activeGameObject;
            if (rootObj == null) return;

            var path = EditorUtility.OpenFilePanel("Load VRoid SpringBone", null, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var jsonContent = File.ReadAllText(path, Encoding.UTF8);
                var springBoneJsons = ParseSpringBoneJsons(jsonContent);
                if (springBoneJsons == null || springBoneJsons.Length == 0)
                {
                    EditorUtility.DisplayDialog("错误", "未找到有效的SpringBone数据", "确定");
                    return;
                }

                var springManager = GetOrCreateSpringManager(rootObj.transform);
                // 清空现有组件（确保重新加载）
                foreach (var existing in springManager.GetComponents<MySpringBone>())
                {
                    UnityEngine.Object.DestroyImmediate(existing);
                }

                // 临时GameObject，用于正确创建VRMSpringBoneHelper实例（因为它是MonoBehaviour）
                var tempGO = new GameObject("TempVRMSpringBoneHelper");
                foreach (var springBoneJson in springBoneJsons)
                {
                    // 关键修正：通过AddComponent创建MonoBehaviour实例（而非new）
                    var helper = tempGO.AddComponent<VRMSpringBoneHelper>();
                    JsonUtility.FromJsonOverwrite(springBoneJson, helper); // 反序列化数据到实例

                    // 创建并配置VRoidSpringBone组件
                    var springBone = springManager.AddComponent<MySpringBone>();
                    springBone.m_stiffnessForce = helper.m_stiffnessForce;
                    springBone.m_gravityPower = helper.m_gravityPower;
                    springBone.m_gravityDir = helper.m_gravityDir;
                    springBone.m_dragForce = helper.m_dragForce;
                    springBone.m_hitRadius = helper.m_hitRadius;

                    // 设置中心骨骼
                    if (!string.IsNullOrEmpty(helper.m_center))
                    {
                        springBone.m_center = FindTransformByPath(rootObj.transform, helper.m_center);
                    }

                    // 设置根骨骼列表
                    springBone.RootBones.Clear();
                    foreach (var bonePath in helper.RootBones)
                    {
                        var bone = FindTransformByPath(rootObj.transform, bonePath);
                        if (bone != null)
                        {
                            springBone.RootBones.Add(bone);
                        }
                    }

                    // 设置碰撞体组
                    var colliderGroups = new List<MySpringBoneColliderGroup>();
                    foreach (var colliderStr in helper.ColliderGroups)
                    {
                        var parts = colliderStr.Split('=');
                        if (parts.Length != 2) continue;

                        var colliderPath = parts[0];
                        var colliderParams = parts[1].Split('~');
                        var colliderTransform = FindTransformByPath(rootObj.transform, colliderPath);

                        // 找不到碰撞体路径时创建新对象
                        if (colliderTransform == null)
                        {
                            colliderTransform = new GameObject(colliderPath).transform;
                            colliderTransform.SetParent(rootObj.transform, false);
                        }

                        // 添加或获取碰撞体组件
                        var colliderGroup = colliderTransform.GetComponent<MySpringBoneColliderGroup>();
                        if (colliderGroup == null)
                        {
                            colliderGroup = colliderTransform.gameObject.AddComponent<MySpringBoneColliderGroup>();
                        }

                        // 配置碰撞体参数
                        colliderGroup.Colliders = colliderParams
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Select(p =>
                            {
                                var paramParts = p.Split(';');
                                if (paramParts.Length != 2) return null;
                                var offsetParts = paramParts[0].Split(',');
                                if (offsetParts.Length != 3) return null;
                                return new MySpringBoneColliderGroup.SphereCollider
                                {
                                    Offset = new Vector3(
                                        float.Parse(offsetParts[0]),
                                        float.Parse(offsetParts[1]),
                                        float.Parse(offsetParts[2])
                                    ),
                                    Radius = float.Parse(paramParts[1])
                                };
                            })
                            .Where(c => c != null)
                            .ToArray();

                        colliderGroups.Add(colliderGroup);
                    }
                    springBone.ColliderGroups = colliderGroups.ToArray();

                    // 清理临时组件（避免数据残留）
                    UnityEngine.Object.DestroyImmediate(helper);
                }

                // 销毁临时GameObject
                UnityEngine.Object.DestroyImmediate(tempGO);

                EditorUtility.DisplayDialog("成功", "SpringBone数据已正确加载", "确定");
            }
            catch (Exception e)
            {
                Debug.LogError($"加载失败: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"加载失败: {e.Message}", "确定");
            }
        }

        // 以下方法与之前一致，确保路径查找正确
        private static string[] ParseSpringBoneJsons(string content)
        {
            var startIndex = content.IndexOf("Spring Bones:");
            if (startIndex < 0) return null;
            var jsonPart = content.Substring(startIndex + "Spring Bones:".Length).Trim();
            return jsonPart.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static GameObject GetOrCreateSpringManager(Transform root)
        {
            var manager = root.FindInChilds2("SpringManager", false)?.gameObject;
            if (manager == null)
            {
                manager = new GameObject("SpringManager");
                manager.transform.SetParent(root, false);
            }
            return manager;
        }

        private static Transform FindTransformByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return path.Contains("/")
                ? path.Split('/').Aggregate(root, (current, part) => current?.FindInChilds2(part, false))
                : root.FindInChilds2(path, false);
        }
    }

}
#endif