using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRM;

public class VRMBlendShapeConfigurator
{
    [MenuItem("VRoidTools/AutoSetupVRMBlendShape/Configure VRM BlendShapes - MMD")]
    static void ConfigureMMD()
    {
        Configure(GetMappingsMMD(), "MMD");
    }

    [MenuItem("VRoidTools/AutoSetupVRMBlendShape/Configure VRM BlendShapes - VRC")]
    static void ConfigureVRC()
    {
        Configure(GetMappingsVRC(), "VRC");
    }

    static Dictionary<BlendShapePreset, string[]> GetMappingsMMD()
    {
        return new Dictionary<BlendShapePreset, string[]>
        {
            { BlendShapePreset.Blink, new[] { "まばたき", "瞬き" } },
            { BlendShapePreset.A, new[] { "あ" } },
            { BlendShapePreset.I, new[] { "い" } },
            { BlendShapePreset.U, new[] { "う" } },
            { BlendShapePreset.E, new[] { "え" } },
            { BlendShapePreset.O, new[] { "お" } },
            { BlendShapePreset.Joy, new[] { "にこり", "笑い", "にやり" } },
            { BlendShapePreset.Angry, new[] { "怒り" } },
            { BlendShapePreset.Sorrow, new[] { "困る" } },
            { BlendShapePreset.Fun, new[] { "わい", "はんがん", "じと目" } }, // Fun常映射到有趣/惊讶类
            { BlendShapePreset.LookUp, new[] { "上向き" } },
            { BlendShapePreset.LookDown, new[] { "下向き" } },
            { BlendShapePreset.LookLeft, new[] { "左向き" } },
            { BlendShapePreset.LookRight, new[] { "右向き" } },
            { BlendShapePreset.Blink_L, new[] { "ウィンク", "ウィンク２", "ｳｨﾝｸ" } },
            { BlendShapePreset.Blink_R, new[] { "わぃんく", "わぃんく２", "ｳｨﾝｸ２" } },
        };
    }

    static Dictionary<BlendShapePreset, string[]> GetMappingsVRC()
    {
        return new Dictionary<BlendShapePreset, string[]>
        {
            { BlendShapePreset.Blink, new[] { "blink", "eyeBlink", "eyeClosed", "eyeClosedLeft", "eyeClosedRight", "v_sil" } },
            { BlendShapePreset.A, new[] { "aa", "v_aa", "jawOpen", "mouthOpen" } },
            { BlendShapePreset.I, new[] { "ih", "v_ih", "E", "v_E", "mouthStretchLeft", "mouthStretchRight" } },
            { BlendShapePreset.U, new[] { "ou", "v_ou", "u", "v_u", "lipPucker", "mouthPucker" } },
            { BlendShapePreset.E, new[] { "E", "v_E", "th", "v_th", "mouthStretch", "lipFunnel" } },
            { BlendShapePreset.O, new[] { "oh", "v_oh", "o", "v_o", "mouthFunnel" } },
            { BlendShapePreset.Joy, new[] { "mood_happy", "mouthSmile", "mouthSmileLeft", "mouthSmileRight", "cheekSquintLeft", "cheekSquintRight" } },
            { BlendShapePreset.Angry, new[] { "mood_angry", "browDown", "browDownLeft", "browDownRight", "mouthFrownLeft", "mouthFrownRight", "noseSneerLeft", "noseSneerRight" } },
            { BlendShapePreset.Sorrow, new[] { "mood_sad", "mouthFrown", "browOuterUp", "browOuterUpLeft", "browOuterUpRight" } },
            { BlendShapePreset.Fun, new[] { "mood_surprised", "eyeWide", "eyeWideLeft", "eyeWideRight", "mouthOpen", "browInnerUp" } },
            { BlendShapePreset.LookUp, new[] { "eyeLookUp", "eyeLookUpLeft", "eyeLookUpRight" } },
            { BlendShapePreset.LookDown, new[] { "eyeLookDown", "eyeLookDownLeft", "eyeLookDownRight" } },
            { BlendShapePreset.LookLeft, new[] { "eyeLookLeft", "eyeLookInLeft", "eyeLookOutRight" } },
            { BlendShapePreset.LookRight, new[] { "eyeLookRight", "eyeLookOutLeft", "eyeLookInRight" } },
            { BlendShapePreset.Blink_L, new[] { "blinkLeft", "eyeClosedLeft", "eyeBlinkLeft" } },
            { BlendShapePreset.Blink_R, new[] { "blinkRight", "eyeClosedRight", "eyeBlinkRight" } },
        };
    }

    static void Configure(Dictionary<BlendShapePreset, string[]> mappings, string norm)
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogError("No object selected in Hierarchy.");
            return;
        }

        GameObject root = Selection.activeGameObject;
        VRMBlendShapeProxy proxy = root.GetComponent<VRMBlendShapeProxy>();
        if (proxy == null)
        {
            Debug.LogError("No VRMBlendShapeProxy component found on selected object.");
            return;
        }

        // 获取模型路径（如果是场景实例，追溯预制体）
        UnityEngine.Object sourcePrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
        string modelPath = AssetDatabase.GetAssetPath(sourcePrefab != null ? sourcePrefab : root);
        string folder = string.IsNullOrEmpty(modelPath) ? "Assets/" + root.name + ".BlendShapes" : Path.GetDirectoryName(modelPath) + "/" + root.name + ".BlendShapes";

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        // 创建或加载BlendShapeAvatar
        string avatarPath = Path.Combine(folder, root.name + "_BlendShapeAvatar.asset");
        BlendShapeAvatar blendShapeAvatar = AssetDatabase.LoadAssetAtPath<BlendShapeAvatar>(avatarPath);
        if (blendShapeAvatar == null)
        {
            blendShapeAvatar = ScriptableObject.CreateInstance<BlendShapeAvatar>();
            AssetDatabase.CreateAsset(blendShapeAvatar, avatarPath);
        }

        // 清空原有clips
        blendShapeAvatar.Clips.Clear();

        // 分配BlendShapeAvatar到proxy
        proxy.BlendShapeAvatar = blendShapeAvatar;

        SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var preset in mappings.Keys)
        {
            BlendShapeClip clip = ScriptableObject.CreateInstance<BlendShapeClip>();
            clip.Preset = preset;
            clip.BlendShapeName = preset.ToString(); // 设置BlendShapeName以便识别

            List<BlendShapeBinding> bindings = new List<BlendShapeBinding>();

            foreach (var smr in smrs)
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string bsName = mesh.GetBlendShapeName(i);
                    if (mappings[preset].Any(m => string.Equals(bsName, m, StringComparison.OrdinalIgnoreCase)))
                    {
                        string relPath = AnimationUtility.CalculateTransformPath(smr.transform, root.transform);
                        bindings.Add(new BlendShapeBinding
                        {
                            RelativePath = relPath,
                            Index = i,
                            Weight = 100f
                        });
                    }
                }
            }

            if (bindings.Count > 0)
            {
                clip.Values = bindings.ToArray();
                string clipPath = Path.Combine(folder, preset.ToString() + ".asset");
                AssetDatabase.CreateAsset(clip, clipPath);
                blendShapeAvatar.Clips.Add(clip);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        // 创建Neutral clip（VRM 0.x通常需要）
        BlendShapeClip neutralClip = ScriptableObject.CreateInstance<BlendShapeClip>();
        neutralClip.Preset = BlendShapePreset.Neutral;
        neutralClip.BlendShapeName = "Neutral";
        string neutralClipPath = Path.Combine(folder, "Neutral.asset");
        AssetDatabase.CreateAsset(neutralClip, neutralClipPath);
        blendShapeAvatar.Clips.Add(neutralClip);

        EditorUtility.SetDirty(blendShapeAvatar);
        EditorUtility.SetDirty(proxy);
        AssetDatabase.SaveAssets();
        Debug.Log($"Configured VRM BlendShapes for {norm} norm. Clips saved in: {folder}, BlendShapeAvatar: {avatarPath}");
    }
}