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
    private Button createButton;
    private HelpBox creationResult;
    private GameObject createdAsset;
    private ContentReferenceAuthoringView referenceView;

    private void OnEnable() => Undo.undoRedoPerformed += OnDraftUndo;
    private void OnDisable() => Undo.undoRedoPerformed -= OnDraftUndo;
    private void OnDraftUndo()
    {
        // Rebuild fields too: Undo may replace serialized nested Draft objects.
        if (validationContainer != null) CreateGUI();
    }

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
        scrollView.Add(new Label("Create Content — Prefab Variant"));
        scrollView.Add(new HelpBox(
            "Creates a Prefab Variant from the selected Base Template and adds the Source Model as a nested prefab.\n Existing assets will not be overwritten.",
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
            Undo.RecordObject(this, "Change content Template");
            draft.BaseTemplatePrefab = evt.newValue as GameObject;
            draft.References = ContentReferenceAuthoring.Inspect(draft.BaseTemplatePrefab);
            referenceView?.Refresh();
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
        draft.References ??= new ReferenceAuthoringDraft();
        if (!draft.References.Inspected && draft.BaseTemplatePrefab != null)
            draft.References = ContentReferenceAuthoring.Inspect(draft.BaseTemplatePrefab);
        referenceView = new ContentReferenceAuthoringView(draft, this, RefreshDraftView);
        scrollView.Add(referenceView);
        AddCreationControls(scrollView);
        RefreshDraftView();
    }

    private void AddCreationControls(VisualElement parent)
    {
        creationResult = new HelpBox("No variant created yet. Complete the required fields, then click Create Variant.", HelpBoxMessageType.Info);
        createButton = new Button(() =>
        {
            var result = ContentVariantCreator.Create(draft);
            createdAsset = result.Asset;
            creationResult.text = result.Message;
            creationResult.messageType = result.Status == VariantCreationStatus.Succeeded
                ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
            RefreshDraftView();
        }) { text = "Create Variant" };
        parent.Add(createButton);
        parent.Add(creationResult);
        parent.Add(new Button(() => { if (createdAsset != null) EditorGUIUtility.PingObject(createdAsset); }) { text = "Ping Created Variant" });
        parent.Add(new Button(() => { if (createdAsset != null) Selection.activeObject = createdAsset; }) { text = "Select Created Variant" });
    }
    private void OnSourceModelChanged(GameObject sourceModel)
    {
        Undo.RecordObject(this, "Change Source Model");
        draft.SourceModelPrefab = sourceModel;
        draft.Renderers.Clear();
        draft.HasDiscoveredRenderers = false;

        if (IsPrefabAsset(sourceModel))
        {
            var renderers = DiscoverSourceRenderers(sourceModel);
            if (renderers != null)
                ApplyRendererDiscovery(renderers);
        }

        referenceView?.Refresh();
        RefreshDraftView();
    }
    
    private List<SkinnedMeshRenderer> DiscoverSourceRenderers(GameObject sourceModel)
    {
        var results = new List<SkinnedMeshRenderer>();
        sourceModel.GetComponentsInChildren(true, results);

        return results;
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

        var creationErrors = ContentVariantCreator.Validate(draft, out var outputPath);
        foreach (var error in creationErrors)
            validationContainer.Add(new HelpBox(error, HelpBoxMessageType.Error));
        validationContainer.Add(new Label($"Output: {outputPath ?? "Invalid ID"}"));
        createButton?.SetEnabled(results.Count == 0 && creationErrors.Count == 0);
        var referenceBlock = ContentReferenceAuthoring.Preflight(draft);
        if (referenceBlock != null)
        {
            validationContainer.Add(new HelpBox(referenceBlock, HelpBoxMessageType.Warning));
            createButton?.SetEnabled(false);
        }

        RefreshDraftPreview();
    }

    // Preview reads draft state only; it does not discover components.
    private void RefreshDraftPreview()
    {
        draftPreview.Clear();
        draftPreview.Add(new Label("[Draft Preview]"));
        draftPreview.Add(new Label($"Content Type: {(draft.HasContentType ? draft.ContentType.ToString() : "Not set / invalid")}"));
        draftPreview.Add(new Label($"Content ID: {draft.Id}"));
        draftPreview.Add(new Label($"Base Template: {(draft.BaseTemplatePrefab != null ? draft.BaseTemplatePrefab.name : "None")}"));
        draftPreview.Add(new Label($"Source Model: {(draft.SourceModelPrefab != null ? draft.SourceModelPrefab.name : "None")}"));

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
