using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class CreateContentWindow : EditorWindow
{
    [SerializeField] private ContentCreationDraft draft = new();
    [SerializeField] private string contentTypeInput = "";

    private VisualElement validationContainer;
    private VisualElement draftPreview;

    public static void Open()
    {
        var window = GetWindow<CreateContentWindow>("Create Content");
        window.minSize = new Vector2(440, 420);
        window.Show();
    }

    public void CreateGUI()
    {
        draft ??= new ContentCreationDraft();
        rootVisualElement.Clear();
        var scrollView = new ScrollView();
        rootVisualElement.Add(scrollView);
        scrollView.Add(new Label("Create Content — M1 Draft"));
        scrollView.Add(new HelpBox(
            "Draft only. Renderer discovery is a guided implementation exercise.",
            HelpBoxMessageType.Info));

        var contentType = new TextField("Content Type (integer)") { value = contentTypeInput };
        contentType.RegisterValueChangedCallback(evt =>
        {
            contentTypeInput = evt.newValue;
            draft.HasContentType = int.TryParse(contentTypeInput, out draft.ContentType);
            RefreshDraftView();
        });
        scrollView.Add(contentType);

        var contentId = new TextField("Content ID") { value = draft.Id };
        contentId.RegisterValueChangedCallback(evt =>
        {
            draft.Id = evt.newValue;
            RefreshDraftView();
        });
        scrollView.Add(contentId);

        var baseTemplate = new ObjectField("Base Template Prefab")
        {
            objectType = typeof(GameObject),
            allowSceneObjects = false,
            value = draft.BaseTemplatePrefab
        };
        baseTemplate.RegisterValueChangedCallback(evt =>
        {
            draft.BaseTemplatePrefab = evt.newValue as GameObject;
            RefreshDraftView();
        });
        scrollView.Add(baseTemplate);

        var sourceModel = new ObjectField("Source Model Prefab")
        {
            objectType = typeof(GameObject),
            allowSceneObjects = false,
            value = draft.SourceModelPrefab
        };
        sourceModel.RegisterValueChangedCallback(evt =>
            OnSourceModelChanged(evt.newValue as GameObject));
        scrollView.Add(sourceModel);

        validationContainer = new VisualElement();
        draftPreview = new VisualElement();
        scrollView.Add(validationContainer);
        scrollView.Add(draftPreview);
        RefreshDraftView();
    }

    // Exercise entry: source selection -> reset old results -> discovery -> state -> preview.
    private void OnSourceModelChanged(GameObject sourceModel)
    {
        draft.SourceModelPrefab = sourceModel;
        draft.Renderers.Clear();
        draft.HasDiscoveredRenderers = false;

        if (IsPrefabAsset(sourceModel))
        {
            var renderers = DiscoverSourceRenderers(sourceModel);
            if (renderers != null)
                ApplyRendererDiscovery(renderers);
        }

        RefreshDraftView();
    }

    /// <summary>
    /// GUIDED TODO: implement discovery here, reading sourceModel without modifying it.
    /// Return the discovered renderers, including an empty list for a completed scan
    /// with no matches. Null means discovery has not been implemented/executed.
    /// Decide how inactive children and nested hierarchy should be handled.
    /// </summary>
    private List<SkinnedMeshRenderer> DiscoverSourceRenderers(GameObject sourceModel)
    {
        return null;
    }

    private void ApplyRendererDiscovery(List<SkinnedMeshRenderer> renderers)
    {
        draft.Renderers.Clear();
        draft.Renderers.AddRange(renderers);
        draft.HasDiscoveredRenderers = true;
    }

    private static bool IsPrefabAsset(GameObject asset)
    {
        return asset != null && EditorUtility.IsPersistent(asset) &&
            PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.NotAPrefab &&
            asset.transform.parent == null;
    }

    private List<ValidationResult> ValidateDraft()
    {
        var results = new List<ValidationResult>();
        if (string.IsNullOrWhiteSpace(contentTypeInput))
            AddError(results, "Content Type is required.");
        else if (!draft.HasContentType)
            AddError(results, "Content Type must be a 32-bit integer (ContentDefinition.ContentType).");

        if (string.IsNullOrWhiteSpace(draft.Id))
            AddError(results, "Content ID is required.");

        ValidatePrefab(draft.BaseTemplatePrefab, "Base Template Prefab", results);
        ValidatePrefab(draft.SourceModelPrefab, "Source Model Prefab", results);
        return results;
    }

    private static void ValidatePrefab(GameObject asset, string label, List<ValidationResult> results)
    {
        if (asset == null)
            AddError(results, $"{label} is required.");
        else if (!IsPrefabAsset(asset))
            AddError(results, $"{label} must be a prefab asset root from the Project window.");
    }

    private static void AddError(List<ValidationResult> results, string message)
    {
        results.Add(new ValidationResult
        {
            Severity = ValidationSeverity.Error,
            Message = message
        });
    }

    private void RefreshDraftView()
    {
        validationContainer.Clear();
        validationContainer.Add(new Label("[Required Field Validation]"));
        var results = ValidateDraft();
        foreach (var result in results)
            validationContainer.Add(new HelpBox(result.Message, HelpBoxMessageType.Error));

        if (results.Count == 0)
            validationContainer.Add(new Label("Required fields are valid. Draft only; no assets are created."));

        RefreshDraftPreview();
    }

    // Preview reads draft state only; it does not discover components.
    private void RefreshDraftPreview()
    {
        draftPreview.Clear();
        draftPreview.Add(new Label("[Draft Preview]"));
        draftPreview.Add(new Label($"Content Type: {(draft.HasContentType ? draft.ContentType.ToString() : "Not set / invalid")}"));
        draftPreview.Add(new Label($"Content ID: {draft.Id}"));
        draftPreview.Add(new Label($"Base Template: {draft.BaseTemplatePrefab?.name ?? "None"}"));
        draftPreview.Add(new Label($"Source Model: {draft.SourceModelPrefab?.name ?? "None"}"));

        if (!IsPrefabAsset(draft.SourceModelPrefab))
            draftPreview.Add(new Label("Select a valid Source Model prefab asset to begin."));
        else if (!draft.HasDiscoveredRenderers)
            draftPreview.Add(new Label("Renderer discovery: pending guided implementation."));
        else
        {
            draftPreview.Add(new Label($"SkinnedMeshRenderers: {draft.Renderers.Count}"));
            foreach (var renderer in draft.Renderers)
                draftPreview.Add(new Label(renderer != null ? renderer.name : "Missing renderer reference"));
        }
    }
}
