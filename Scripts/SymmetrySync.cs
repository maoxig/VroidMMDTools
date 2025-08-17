using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions; // 引入正则表达式用于处理前缀
[ExecuteInEditMode]
public class SymmetrySync : MonoBehaviour
{
    [Header("对称同步设置")]
    public bool enableSymmetry = false;
    public SymmetryAxis symmetryAxis = SymmetryAxis.X;
    // 新增：忽略名称前缀序号的开关（默认开启）
    public bool ignorePrefixNumbers = true;
    public List<SymmetryKeywordPair> keywordPairs = new List<SymmetryKeywordPair>
{
new SymmetryKeywordPair ("Left", "Right"),
new SymmetryKeywordPair ("左", "右"),
new SymmetryKeywordPair ("L", "R"),
new SymmetryKeywordPair ("左側", "右側"),
new SymmetryKeywordPair ("左の", "右の")
};

    [Serializable]
    public class SymmetryKeywordPair
    {
        public string a;
        public string b;

        public SymmetryKeywordPair(string a, string b)
        {
            this.a = a;
            this.b = b;
        }
    }

    public enum SymmetryAxis
    {
        X, Y, Z
    }

    private void OnEnable()
    {
        if (enableSymmetry)
        {
            SymmetrySyncEditor.RegisterSymmetryObject(this);
        }
    }

    private void OnDisable()
    {
        SymmetrySyncEditor.UnregisterSymmetryObject(this);
    }

    private void OnValidate()
    {
        if (enableSymmetry)
        {
            SymmetrySyncEditor.RegisterSymmetryObject(this);
        }
        else
        {
            SymmetrySyncEditor.UnregisterSymmetryObject(this);
        }
    }

    // 查找对称对象（修改：先处理名称前缀）
    public Transform FindSymmetricTransform(Transform target)
    {
        if (target == null || !IsChildOfThis(target))
            return null;

        // 1. 处理名称：如果开启开关，先去除前缀序号
        string targetName = target.name;
        if (ignorePrefixNumbers)
        {
            targetName = RemovePrefixNumbers(targetName);
        }

        // 2. 尝试用每个关键词对查找对称对象
        foreach (var pair in keywordPairs)
        {
            string symmetricName;
            bool isASide = targetName.IndexOf(pair.a, StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBSide = targetName.IndexOf(pair.b, StringComparison.OrdinalIgnoreCase) >= 0;

            if (isASide && !isBSide)
            {
                // 生成对称名称（基于处理后的名称）
                symmetricName = ReplaceFirstOccurrence(targetName, pair.a, pair.b, StringComparison.OrdinalIgnoreCase);
            }
            else if (isBSide && !isASide)
            {
                symmetricName = ReplaceFirstOccurrence(targetName, pair.b, pair.a, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                continue;
            }

            // 3. 查找对称对象（注意：原名称可能带前缀，需遍历所有子对象匹配处理后的名称）
            Transform symmetricTransform = FindInChildrenByCleanName(transform, symmetricName);
            if (symmetricTransform != null && symmetricTransform != target)
            {
                return symmetricTransform;
            }
        }

        return null;
    }

    // 新增：去除名称开头的「数字 + 分隔符」前缀（如 79.!、123_、45.）
    private string RemovePrefixNumbers(string originalName)
    {
        if (string.IsNullOrEmpty(originalName))
            return originalName;

        // 正则表达式：匹配开头的「1 个以上数字」+「. 或！或 」（可根据需求扩展分隔符）
        string pattern = @"^\d+[.!]";
        return Regex.Replace(originalName, pattern, "", RegexOptions.IgnoreCase);
    }

    // 新增：根据「处理后的名称」查找子对象（支持带前缀的原名称匹配）
    private Transform FindInChildrenByCleanName(Transform parent, string cleanTargetName)
    {
        if (parent == null || string.IsNullOrEmpty(cleanTargetName))
            return null;

        // 处理当前父对象的名称，判断是否匹配
        string parentCleanName = ignorePrefixNumbers ? RemovePrefixNumbers(parent.name) : parent.name;
        if (parentCleanName.Equals(cleanTargetName, StringComparison.OrdinalIgnoreCase))
            return parent;

        // 递归检查所有子对象
        foreach (Transform child in parent)
        {
            Transform found = FindInChildrenByCleanName(child, cleanTargetName);
            if (found != null)
                return found;
        }

        return null;
    }

    // 检查是否是当前对象的子物体（原有逻辑不变）
    private bool IsChildOfThis(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current == transform)
                return true;
            current = current.parent;
        }
        return false;
    }

    // 替换第一次出现的字符串（原有逻辑不变）
    private string ReplaceFirstOccurrence(string source, string oldValue, string newValue, StringComparison comparisonType)
    {
        int index = source.IndexOf(oldValue, comparisonType);
        if (index < 0)
            return source;

        return source.Remove(index, oldValue.Length).Insert(index, newValue);
    }

    // 计算对称向量（原有逻辑不变）
    public Vector3 GetSymmetricVector(Vector3 original)
    {
        switch (symmetryAxis)
        {
            case SymmetryAxis.X:
                return new Vector3(-original.x, original.y, original.z);
            case SymmetryAxis.Y:
                return new Vector3(original.x, -original.y, original.z);
            case SymmetryAxis.Z:
                return new Vector3(original.x, original.y, -original.z);
            default:
                return original;
        }
    }

    // 计算对称四元数（原有逻辑不变）
    public Quaternion GetSymmetricQuaternion(Quaternion original)
    {
        switch (symmetryAxis)
        {
            case SymmetryAxis.X:
                return new Quaternion(-original.x, original.y, original.z, -original.w);
            case SymmetryAxis.Y:
                return new Quaternion(original.x, -original.y, original.z, -original.w);
            case SymmetryAxis.Z:
                return new Quaternion(original.x, original.y, -original.z, -original.w);
            default:
                return original;
        }
    }
}