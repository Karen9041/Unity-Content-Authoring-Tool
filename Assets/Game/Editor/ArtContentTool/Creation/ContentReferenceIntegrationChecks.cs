using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Reusable public fixtures; only this run's unreferenced outputs are cleaned after success.</summary>
public static class ContentReferenceIntegrationChecks
{
    public const string FixtureFolder = "Assets/Game/Tests/Fixtures/ReferenceAuthoring";
    public const string SmokeTemplatePath = FixtureFolder + "/SmokeTemplate.prefab";
    private const string PublicTemplate = SmokeTemplatePath;
    private const string PublicModel = "Assets/Game/Arts/TestModels/Quaternius/Pirate Kit/FBX/Characters_Shark.fbx";
    private static readonly List<string> passed = new();

    [MenuItem("Tools/Art Content Tool/Run M2.2 Apply Preview Checks")]
    public static void RunPreview()
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(PublicTemplate);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PublicModel);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(template, scene);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            var target = template.GetComponent<ContentPrefabReferences>();
            var container = instance.GetComponent<ContentPrefabReferences>();
            SetEntries(container, new ContentReferenceEntry { Key = "sentinel", ValueType = ContentReferenceValueType.String, StringValue = "keep" },
                new ContentReferenceEntry { Key = "second_slot", ValueType = ContentReferenceValueType.String });
            var renderer = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var draft = new ContentCreationDraft { BaseTemplatePrefab = template, SourceModelPrefab = source };
            draft.References.Entries.Add(new ReferenceDraftEntry { Target = target, FixedProperty = "mainRenderer", Key = "mainRenderer",
                ValueType = ContentReferenceValueType.SkinnedMeshRenderer, Origin = ReferenceValueOrigin.SourceModel,
                ObjectValue = renderer, SelectedSourceRoot = source });
            draft.References.Entries.Add(new ReferenceDraftEntry { Target = target, Key = "text", ValueType = ContentReferenceValueType.String,
                Origin = ReferenceValueOrigin.SourceModel, StringValue = "string bypasses resolver" });
            draft.References.Entries.Add(new ReferenceDraftEntry { Target = target, Key = "generic_node", ValueType = ContentReferenceValueType.Object,
                Origin = ReferenceValueOrigin.SourceModel, ObjectValue = source.transform, SelectedSourceRoot = source });
            ContentReferenceAuthoring.ApplyEntry(draft, 0, instance, model);
            Check(container.MainRenderer != renderer && ContentReferenceAuthoring.Within(container.MainRenderer, model), "Fixed field resolves instance renderer");
            Check(container.Entries.Count == 2 && container.Entries[0].Key == "sentinel", "Fixed field does not write dynamic entries");
            ContentReferenceAuthoring.ApplyEntry(draft, 1, instance, model);
            Check(container.TryGetString("text", out var text) && text == "string bypasses resolver", "String branches before hierarchy resolver");
            ContentReferenceAuthoring.ApplyEntry(draft, 2, instance, model);
            Check(container.TryGetObject<Transform>("generic_node", out var node) && node == model.transform, "Generic Object resolves actual Transform type");
            var error = ExpectFailure(() => ContentReferenceAuthoring.ApplyEntry(draft, 2, instance, instance));
            Check(error.Contains("generic_node") && error.Contains("SourceModel") && error.Contains("Property=") &&
                error.Contains("Expected=") && error.Contains("Actual="), "No-match diagnostics retain entry context");
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            copy.transform.SetParent(model.transform, false);
            Check(ExpectFailure(() => ContentReferenceAuthoring.ApplyEntry(draft, 2, instance, model)).Contains("Ambiguous reference"),
                "Multiple source matches rejected");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("M2.2 Apply preview regression checks passed (6 checks).");
    }

    [MenuItem("Tools/Art Content Tool/Run M2.2 Save Reload Checks")]
    public static void RunMenu() => Run();

    public static string Run(bool keepOutputs = false)
    {
        passed.Clear();
        if (!ContentReferenceAuthoring.ApplyImplementationReady) throw new InvalidOperationException("Apply implementation is still disabled.");
        string token = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string prefix = "RA_Test_" + token;
        var fixtures = new[] { "BaseTemplate", "SourceModel", "SmokeTemplate" }
            .Select(name => AssetDatabase.LoadAssetAtPath<GameObject>(FixtureFolder + "/" + name + ".prefab")).ToArray();
        if (fixtures.Any(asset => asset == null)) throw new InvalidOperationException("Reference Authoring fixtures are missing from " + FixtureFolder);
        var filesBefore = AssetDatabase.FindAssets("", new[] { "Assets/Game/GeneratedContent" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(File.Exists).SelectMany(path => new[] { path, path + ".meta" })
            .Where(File.Exists).Distinct().ToArray();
        var texturePath = AssetDatabase.GetAssetPath(fixtures[0].GetComponent<ContentPrefabReferences>().Entries.First(e => e.Key == "texture").ObjectValue);
        var protectedPaths = filesBefore.Concat(new[] { PublicTemplate, PublicModel, texturePath })
            .Concat(new[] { PublicTemplate + ".meta", PublicModel + ".meta", texturePath + ".meta" }).Distinct().ToArray();
        var hashes = protectedPaths.ToDictionary(path => path, Hash);
        var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
        int sceneCount = SceneManager.sceneCount; int previewCount = EditorSceneManager.previewSceneCount;
        var outputs = new List<string>();
        string outputFolder = Path.GetDirectoryName(ContentVariantCreator.GetOutputPath(2202, prefix)).Replace('\\', '/');
        bool outputFolderExisted = AssetDatabase.IsValidFolder(outputFolder);
        try
        {
            var template = fixtures[0]; var model = fixtures[1];
            foreach (var fixture in fixtures)
            {
                string fixturePath = AssetDatabase.GetAssetPath(fixture);
                hashes[fixturePath] = Hash(fixturePath);
                hashes[fixturePath + ".meta"] = Hash(fixturePath + ".meta");
            }
            protectedPaths = hashes.Keys.ToArray();
            ContentVariantCreatorChecks.Run();
            ContentReferenceAuthoringChecks.RunForTemplate(AssetDatabase.GetAssetPath(fixtures[2]));
            RunPreview();

            var inherited = Draft(template, model, prefix + "_Inherited");
            Check(!inherited.References.NeedsApply, "Unchanged Draft keeps baseline");
            var inheritedAsset = CreateAndReload(inherited, outputs);
            Check(inheritedAsset.GetComponents<ContentPrefabReferences>()[0].TryGetString("keep_text", out var inheritedText) && inheritedText == "inherited",
                "Nonempty inherited dynamic string survives reload");
            Check(!PrefabUtility.GetPropertyModifications(inheritedAsset).Any(p => p.propertyPath.StartsWith("entries.") || p.propertyPath == "entries"),
                "Unchanged dynamic list has no new entry overrides");

            var edited = Draft(template, model, prefix + "_Edited");
            var collectors = template.GetComponents<ContentPrefabReferences>();
            var target = collectors[0]; var second = collectors[1];
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(renderers.Length >= 2, "Reusable public model fixture contains multiple renderers");
            var selectedRenderer = renderers[renderers.Length - 1];
            SetSource(edited.References.Entries.Find(r => r.Target == target && r.FixedProperty == "mainRenderer"), selectedRenderer, model);
            SetSource(edited.References.Entries.Find(r => r.Target == target && r.FixedProperty == "animator"), model.GetComponent<Animator>(), model);
            edited.References.Entries.RemoveAll(r => r.Target == target && r.Key == "remove_me");
            edited.References.Entries.Find(r => r.Target == target && r.Key == "rename_me").Key = "renamed_text";
            var toObject = edited.References.Entries.Find(r => r.Target == target && r.Key == "to_object");
            toObject.ValueType = ContentReferenceValueType.Texture; toObject.Origin = ReferenceValueOrigin.Asset;
            toObject.StringValue = ""; toObject.ObjectValue = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            var toString = edited.References.Entries.Find(r => r.Target == target && r.Key == "to_string");
            toString.ValueType = ContentReferenceValueType.String; toString.ObjectValue = null;
            toString.StringValue = "literal value"; toString.Origin = ReferenceValueOrigin.SourceModel;
            Add(edited, target, "source_renderer", ContentReferenceValueType.SkinnedMeshRenderer, selectedRenderer, ReferenceValueOrigin.SourceModel);
            Add(edited, target, "source_transform", ContentReferenceValueType.Transform, selectedRenderer.transform, ReferenceValueOrigin.SourceModel);
            Add(edited, target, "source_gameobject", ContentReferenceValueType.GameObject, model, ReferenceValueOrigin.SourceModel);
            Add(edited, target, "source_animator", ContentReferenceValueType.Animator, model.GetComponent<Animator>(), ReferenceValueOrigin.SourceModel);
            Add(edited, target, "generic_gameobject", ContentReferenceValueType.Object, model, ReferenceValueOrigin.SourceModel);
            var colliders = model.GetComponents<BoxCollider>();
            Add(edited, target, "second_component", ContentReferenceValueType.Object, colliders.Last(), ReferenceValueOrigin.SourceModel);
            Add(edited, target, "explicit_none", ContentReferenceValueType.Object, null, ReferenceValueOrigin.None);
            Add(edited, second, "source_renderer", ContentReferenceValueType.SkinnedMeshRenderer, renderers[0], ReferenceValueOrigin.SourceModel);
            Check(ContentReferenceAuthoring.Validate(edited).Count == 0, "Edited multi-container Draft is valid");
            var variant = CreateAndReload(edited, outputs);
            var resultCollector = variant.GetComponents<ContentPrefabReferences>()[0];
            var nested = Nested(variant, model);
            Check(resultCollector.MainRenderer != selectedRenderer && ContentReferenceAuthoring.Within(resultCollector.MainRenderer, nested),
                "Fixed renderer stores nested instance, not source asset");
            Check(resultCollector.Animator == nested.GetComponent<Animator>(), "Fixed Animator uses nested instance");
            Check(resultCollector.TryGetObject<SkinnedMeshRenderer>("source_renderer", out var actualRenderer) &&
                PrefabUtility.GetCorrespondingObjectFromSourceAtPath(actualRenderer, AssetDatabase.GetAssetPath(model)) == selectedRenderer,
                "Explicit second renderer source identity survives reload");
            Check(resultCollector.TryGetObject<Texture>("to_object", out var texture) && AssetDatabase.GetAssetPath(texture) == texturePath,
                "Texture keeps external asset identity");
            Check(resultCollector.TryGetString("to_string", out var literal) && literal == "literal value" && !resultCollector.TryGetObject<Object>("to_string", out _),
                "Runtime String lookup and object separation after reload");
            Check(resultCollector.Entries.All(e => e.Key != "remove_me" && e.Key != "rename_me") &&
                resultCollector.TryGetString("renamed_text", out _), "Remove and rename preserve destination indices");
            Check(resultCollector.TryGetObject<Transform>("template_node", out var templateNode) &&
                ContentReferenceAuthoring.Within(templateNode, variant) && !ContentReferenceAuthoring.Within(templateNode, nested),
                "Template reference remaps into retained Template hierarchy");
            Check(resultCollector.TryGetObject<BoxCollider>("second_component", out var actualCollider) &&
                actualCollider == nested.GetComponents<BoxCollider>().Last(), "Second same-type component retains identity");
            Check(variant.GetComponents<ContentPrefabReferences>()[1].TryGetObject<SkinnedMeshRenderer>("source_renderer", out var otherRenderer) &&
                otherRenderer != actualRenderer, "Same key in second original Target writes independent container");
            Check(resultCollector.Entries.First(e => e.Key == "explicit_none").ObjectValue == null, "Explicit None remains null");
            var mods = PrefabUtility.GetPropertyModifications(variant);
            Check(mods.Any(p => p.propertyPath == "mainRenderer") && mods.Any(p => p.propertyPath == "animator"), "Fixed fields persist as Variant overrides");

            string editedPath = AssetDatabase.GetAssetPath(variant);
            string prefabHash = Hash(editedPath), metaHash = Hash(editedPath + ".meta");
            Check(ContentVariantCreator.Create(edited).Status == VariantCreationStatus.InvalidInput && Hash(editedPath) == prefabHash &&
                Hash(editedPath + ".meta") == metaHash, "Duplicate creation rejected without overwrite");
            var duplicate = edited.References.Entries.First(r => string.IsNullOrEmpty(r.FixedProperty)).Copy();
            edited.References.Entries.Add(duplicate);
            Check(ContentReferenceAuthoring.Validate(edited).Any(e => e.Contains("Duplicate")), "Duplicate key rejected");
            edited.References.Entries.Remove(duplicate);
            var selected = edited.References.Entries.First(r => r.Key == "source_renderer");
            var previousType = selected.ValueType; selected.ValueType = ContentReferenceValueType.Texture;
            Check(ContentReferenceAuthoring.ValidateEntry(edited, selected) != null, "Wrong type rejected"); selected.ValueType = previousType;
            edited.SourceModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PublicModel);
            Check(ContentReferenceAuthoring.ValidateEntry(edited, selected)?.Contains("Source Model changed") == true, "Old model selection rejected");
            edited.SourceModelPrefab = model;

            var cleared = Draft(template, model, prefix + "_Cleared");
            cleared.References.Entries.RemoveAll(r => string.IsNullOrEmpty(r.FixedProperty));
            var empty = CreateAndReload(cleared, outputs);
            Check(empty.GetComponents<ContentPrefabReferences>().All(c => c.Entries.Count == 0), "Delete all dynamic entries including final row persists");

            var failure = Draft(template, template, prefix + "_ApplyFailure");
            failure.References.Entries.First(r => r.ValueType == ContentReferenceValueType.String).StringValue = "force apply";
            var failed = ContentVariantCreator.Create(failure);
            Check(failed.Status == VariantCreationStatus.Failed && failed.Asset == null && failed.Message.Contains("Ambiguous target") &&
                !File.Exists(ContentVariantCreator.GetOutputPath(failure.ContentType, failure.Id)), "Ambiguous Apply cannot save or report Success");
            Check(protectedPaths.All(path => Hash(path) == hashes[path]), "All original assets, metas and prior generated files are byte-identical");
            Check(SceneManager.GetActiveScene() == active && active.isDirty == dirty && SceneManager.sceneCount == sceneCount &&
                EditorSceneManager.previewSceneCount == previewCount, "Preview scenes and temporary instances cleaned on success/failure");
            if (!keepOutputs) CleanupOutputs(outputs, prefix);
            if (!outputFolderExisted && AssetDatabase.IsValidFolder(outputFolder) && Directory.GetFileSystemEntries(outputFolder).Length == 0)
                AssetDatabase.DeleteAsset(outputFolder);
            var retained = outputs.Where(File.Exists).ToArray();
            string result = $"M2.2 integration PASS: {passed.Count} assertions. Retained outputs: {retained.Length}.";
            Debug.Log(result + (retained.Length > 0 ? "\n" + string.Join("\n", retained) : ""));
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError($"M2.2 integration failed after {passed.Count} checks: {ex.Message}\n" +
                "New outputs retained for diagnosis:\n" + string.Join("\n", outputs));
            throw;
        }
    }

    private static void CleanupOutputs(List<string> outputs, string prefix)
    {
        // Only paths successfully created by this run qualify. Keep anything newly referenced elsewhere.
        var ownPaths = new HashSet<string>(outputs, StringComparer.Ordinal);
        var externalPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
            File.Exists(path) && !ownPaths.Contains(path)).ToArray();
        var referenced = new HashSet<string>(AssetDatabase.GetDependencies(externalPaths, true), StringComparer.Ordinal);
        foreach (var path in outputs)
        {
            if (!path.StartsWith("Assets/Game/GeneratedContent/Type_2202/Content_" + prefix + "_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test output ownership: " + path);
            if (referenced.Contains(path))
            {
                Debug.LogWarning("Referenced test output retained: " + path);
                continue;
            }
            if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not clean this run's test output: " + path);
        }
    }
    private static GameObject CreateAndReload(ContentCreationDraft draft, List<string> outputs)
    {
        Check(!ContentVariantCreator.PathOccupied(ContentVariantCreator.GetOutputPath(draft.ContentType, draft.Id)), "Test output path is unused");
        var result = ContentVariantCreator.Create(draft);
        Check(result.Status == VariantCreationStatus.Succeeded, "Create " + draft.Id + ": " + result.Message);
        outputs.Add(result.OutputPath);
        AssetDatabase.ImportAsset(result.OutputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(result.OutputPath);
        Check(PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant &&
            PrefabUtility.GetCorrespondingObjectFromSource(asset) == draft.BaseTemplatePrefab, "Variant type and exact base link after reload");
        Check(Nested(asset, draft.SourceModelPrefab) != null, "Nested Model source link after reload");
        Check(ContentReferenceAuthoring.Verify(draft, asset, Nested(asset, draft.SourceModelPrefab)).Count == 0, "Persisted asset Reference identity matches Draft");
        var loaded = PrefabUtility.LoadPrefabContents(result.OutputPath);
        try
        {
            Check(ContentReferenceAuthoring.Verify(draft, loaded, Nested(loaded, draft.SourceModelPrefab)).Count == 0,
                "LoadPrefabContents independently verifies saved references");
        }
        finally { PrefabUtility.UnloadPrefabContents(loaded); }
        return asset;
    }

    private static GameObject Nested(GameObject root, GameObject source)
    {
        return root.transform.Cast<Transform>().Select(t => t.gameObject).Single(go =>
            PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetCorrespondingObjectFromSource(go) == source);
    }
    private static ContentCreationDraft Draft(GameObject template, GameObject model, string id) => new ContentCreationDraft
    { BaseTemplatePrefab = template, SourceModelPrefab = model, HasContentType = true, ContentType = 2202, Id = id, References = ContentReferenceAuthoring.Inspect(template) };
    private static void SetSource(ReferenceDraftEntry entry, Object value, GameObject model)
    { entry.Origin = ReferenceValueOrigin.SourceModel; entry.ObjectValue = value; entry.StringValue = ""; entry.SelectedSourceRoot = model; }
    private static void Add(ContentCreationDraft draft, ContentPrefabReferences target, string key, ContentReferenceValueType type, Object value, ReferenceValueOrigin origin)
    { draft.References.Entries.Add(new ReferenceDraftEntry { Target = target, Key = key, ValueType = type, ObjectValue = value,
        Origin = origin, SelectedSourceRoot = origin == ReferenceValueOrigin.SourceModel ? draft.SourceModelPrefab : null }); }
    private static void SetEntries(ContentPrefabReferences target, params ContentReferenceEntry[] entries)
    {
        using (var serialized = new SerializedObject(target))
        {
            var list = serialized.FindProperty("entries"); list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var item = list.GetArrayElementAtIndex(i); var entry = entries[i];
                item.FindPropertyRelative("Key").stringValue = entry.Key;
                item.FindPropertyRelative("ValueType").enumValueIndex = (int)entry.ValueType;
                item.FindPropertyRelative("ObjectValue").objectReferenceValue = entry.ObjectValue;
                item.FindPropertyRelative("StringValue").stringValue = entry.StringValue ?? "";
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    private static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)); }
    private static string ExpectFailure(Action action)
    { try { action(); } catch (InvalidOperationException ex) { return ex.Message; } throw new Exception("Expected failure did not occur."); }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("M2.2 integration check failed: " + name); passed.Add(name); }
}
