using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum VariantCreationStatus { InvalidInput, NotImplemented, Failed, Succeeded }

public sealed class VariantCreationResult
{
    public VariantCreationStatus Status { get; }
    public string Message { get; }
    public string OutputPath { get; }
    public GameObject Asset { get; }
    public VariantCreationResult(VariantCreationStatus status, string message, string path = null, GameObject asset = null)
    { Status = status; Message = message; OutputPath = path; Asset = asset; }
}

/// <summary>Small editor-only boundary. Does not bind references or register content.</summary>
public static class ContentVariantCreator
{
    public static string GetOutputPath(int contentType, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) 
            return null;

        id = id.Trim();

        // Reject rather than silently normalize: distinct IDs must not collapse to one file.
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            id.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || id.EndsWith(".") ||
            id == "." || id == "..") 
            return null;
        
        foreach (char c in id)
        {
            if (char.IsControl(c)) 
                return null;
        }

        return $"Assets/Game/GeneratedContent/Type_{contentType}/Content_{id}.prefab";
    }

    public static List<string> Validate(ContentCreationDraft draft, out string outputPath)
    {
        var errors = new List<string>();
        outputPath = null;

        if (draft == null) 
        { 
            errors.Add("Creation draft is missing."); 
            return errors; 
        }

        if (!draft.HasContentType) 
            errors.Add("Content Type must be a 32-bit integer.");
        
        outputPath = GetOutputPath(draft.ContentType, draft.Id);

        if (outputPath == null) 
            errors.Add("Content ID must contain a filename: no blank names, control characters, <>:\"/\\|?*, or trailing dot.");

        ValidatePrefab(draft.BaseTemplatePrefab, "Base Template", false, errors);
        ValidatePrefab(draft.SourceModelPrefab, "Source Model", true, errors);

        if (outputPath != null)
        {
            if (PathOccupied(outputPath)) 
                errors.Add($"Output already exists; overwriting is forbidden: {outputPath}");

            var parent = Path.GetDirectoryName(outputPath);

            while (!string.IsNullOrEmpty(parent))
            {
                if (File.Exists(parent)) 
                { 
                    errors.Add($"Output folder is blocked by a file: {parent}"); 
                    break; 
                }

                parent = Path.GetDirectoryName(parent);
            }
        }

        return errors;
    }

    public static bool PathOccupied(string path) => File.Exists(path) || Directory.Exists(path) ||
        File.Exists(path + ".meta") || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));

    private static void ValidatePrefab(GameObject asset, string label, bool allowModel, List<string> errors)
    {
        var type = asset == null ? PrefabAssetType.NotAPrefab : PrefabUtility.GetPrefabAssetType(asset);

        if (asset == null || !EditorUtility.IsPersistent(asset) || asset.transform.parent != null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(asset)) != asset ||
            !(type == PrefabAssetType.Regular || type == PrefabAssetType.Variant || (allowModel && type == PrefabAssetType.Model)))
            errors.Add($"{label} must be a Project prefab root ({(allowModel ? "Regular, Variant or Model" : "Regular or Variant")}).");
    }

    public static VariantCreationResult Create(ContentCreationDraft draft)
    {
        var errors = Validate(draft, out var path);

        if (errors.Count != 0) 
            return new VariantCreationResult(VariantCreationStatus.InvalidInput, string.Join("\n", errors), path);

        var stage = "capture input fingerprints";

        try
        {
            var templatePath = AssetDatabase.GetAssetPath(draft.BaseTemplatePrefab);
            var modelPath = AssetDatabase.GetAssetPath(draft.SourceModelPrefab);
            var templateHash = AssetDatabase.GetAssetDependencyHash(templatePath);
            var modelHash = AssetDatabase.GetAssetDependencyHash(modelPath);

            stage = "create variant";

            var result = CreateVariantFromTemplate(draft.BaseTemplatePrefab, draft.SourceModelPrefab, path);

            if (result.Status != VariantCreationStatus.Succeeded) 
                return result;

            stage = "verify saved variant";

            return VerifyCreatedVariant(path, draft.BaseTemplatePrefab, draft.SourceModelPrefab, templateHash, modelHash);
        }
        catch (Exception ex)
        {
            return new VariantCreationResult(VariantCreationStatus.Failed,
                $"Could not {stage} at '{path}' ({ex.GetType().Name}): {ex.Message}", path);
        }
    }

    // USER EXERCISE: instantiate both assets, parent the model, save the connected template
    // instance as a variant, and clean up both temporary instances in finally.
    // Recheck PathOccupied immediately before saving. Never unpack or apply to the inputs.
    public static VariantCreationResult CreateVariantFromTemplate(GameObject baseTemplate, GameObject sourceModel, string outputPath)
    {
        Scene previewScene = default;
        GameObject templateInstance = null;
        GameObject modelInstance = null;
        GameObject Variant = null;

        try
        {
            previewScene = EditorSceneManager.NewPreviewScene();

            string parentFolder = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(parentFolder))
                throw new InvalidOperationException($"Invalid output path: {outputPath}");

            EnsureAssetFolderExists(parentFolder);

            templateInstance = PrefabUtility.InstantiatePrefab(baseTemplate, previewScene) as GameObject;

            if(templateInstance == null)
                throw new NullReferenceException($"Failed to instantiate prefab: {baseTemplate.name}");

            modelInstance = PrefabUtility.InstantiatePrefab(sourceModel, previewScene) as GameObject;

            if(modelInstance == null)
                throw new NullReferenceException($"Failed to instantiate prefab: {sourceModel.name}");

            modelInstance.transform.SetParent(templateInstance.transform, false);

            if (PathOccupied(outputPath))
                return new VariantCreationResult(
                    VariantCreationStatus.Failed,
                    $"outputPath already exit: {outputPath}",
                    outputPath);
            
            Variant = PrefabUtility.SaveAsPrefabAsset(templateInstance, outputPath, out bool isSucceeded );

            if(Variant == null || !isSucceeded)
                return new VariantCreationResult(
                    VariantCreationStatus.Failed,
                    $"Failed to save {templateInstance.name} as prefab",
                    outputPath);
        }
        finally
        {
            if(templateInstance != null)
                GameObject.DestroyImmediate(templateInstance);

            if(modelInstance != null)
                GameObject.DestroyImmediate(modelInstance);

            if (previewScene.IsValid())
                EditorSceneManager.ClosePreviewScene(previewScene);
        }


        return new VariantCreationResult(
                VariantCreationStatus.Succeeded,
                "Asset was created.",
                outputPath,
                Variant);
    }

    public static VariantCreationResult VerifyCreatedVariant(string path, GameObject template, GameObject model,
        Hash128 templateHashBefore, Hash128 modelHashBefore)
    {
        var errors = new List<string>();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!File.Exists(path) || asset == null) errors.Add("Saved prefab file cannot be loaded.");
        else
        {
            if (PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.Variant) errors.Add("Saved asset is not a Prefab Variant.");
            if (PrefabUtility.GetCorrespondingObjectFromSource(asset) != template) errors.Add("Variant base link does not match the selected Template.");
            bool linkedModel = false;
            foreach (var child in asset.GetComponentsInChildren<Transform>(true))
                if (child != asset.transform && PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
                    PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == model) linkedModel = true;
            if (!linkedModel) errors.Add("No nested prefab root retains a source link to the selected Model.");
        }
        if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(template)) != templateHashBefore)
            errors.Add("Template dependency fingerprint changed during creation.");
        if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(model)) != modelHashBefore)
            errors.Add("Model dependency fingerprint changed during creation.");
        return errors.Count == 0
            ? new VariantCreationResult(VariantCreationStatus.Succeeded, "Variant and nested Model source links verified.", path, asset)
            : new VariantCreationResult(VariantCreationStatus.Failed, string.Join("\n", errors), path);
    }

    private static void EnsureAssetFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;
        
        string parentPath = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = Path.GetFileName(folderPath);

        if(string.IsNullOrWhiteSpace(folderName) || !parentPath.StartsWith("Assets", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid asset folder: {folderPath}");
        }

        EnsureAssetFolderExists(parentPath);

        string guid = AssetDatabase.CreateFolder(parentPath, folderName);

        if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(folderPath))
        {
            throw new IOException(
                $"Failed to create asset folder: {folderPath}");
        }
    }
}
