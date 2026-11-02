using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only checks; no assets, scenes, or temporary GameObjects are created.</summary>
public static class ContentVariantCreatorChecks
{
    [MenuItem("Tools/Art Content Tool/Run M2.1 Safe Checks")]
    public static void Run()
    {
        Check(ContentVariantCreator.GetOutputPath(0, " sample ") ==
            "Assets/Game/GeneratedContent/Type_0/Content_sample.prefab", "ID trimming / type zero");
        
        Check(ContentVariantCreator.GetOutputPath(-2, "sample").Contains("Type_-2/"), "integer type folder");
        
        foreach (var id in new[] { "", " ", "../bad", "a/b", "a\\b", "a:b", "a?b", "a*b", "a\"b", "a<b", "a>b", "a|b", "a\nb", "bad." })
            Check(ContentVariantCreator.GetOutputPath(1, id) == null, "invalid ID: " + id);
        
        Check(ContentVariantCreator.PathOccupied("Assets"), "directory collision");
        
        Check(ContentVariantCreator.PathOccupied("Packages/manifest.json"), "file collision");
        
        var errors = ContentVariantCreator.Validate(new ContentCreationDraft(), out _);
        
        Check(errors.Count >= 4, "missing type, ID, Template and Model");
        
        Check(ContentVariantCreator.Create(new ContentCreationDraft()).Status == VariantCreationStatus.InvalidInput,
            "invalid draft must not enter core");
        
        Debug.Log("M2.1 safe checks passed. These checks do not save assets.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("M2.1 check failed: " + name);
    }
}
