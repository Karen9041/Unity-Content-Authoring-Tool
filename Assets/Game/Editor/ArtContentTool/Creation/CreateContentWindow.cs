using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class CreateContentWindow : EditorWindow
{
	[SerializeField]
	private ContentCreationDraft draft = new();
	[SerializeField]
	private List<int> knownTypes = new();
	[SerializeField]
	private bool customType;
	[SerializeField]
	private string labelSearch = "";
	[SerializeField]
	private ContentRegistrationReceipt pendingRegistration;
	[SerializeField]
	private GameObject createdAsset;
	[SerializeField]
	private string lastMessage = "No variant created yet. Complete the required fields, then click Create Variant.";
	[SerializeField]
	private bool lastSucceeded;
	private VisualElement validationContainer;
	private VisualElement draftPreview;
	private Button createButton;
	private HelpBox creationResult;
	private ContentReferenceAuthoringView referenceView;
	private ContentRegistrationView registrationView;
	private DropdownField typeDropdown;
	private IntegerField customTypeField;
	private List<int> typeChoices;
	private Button retryButton;
	internal System.Func<UnityEditor.AddressableAssets.Settings.AddressableAssetSettings> SettingsProviderForChecks;
	private void OnEnable()
	{
		Undo.undoRedoPerformed += OnDraftUndo;
		EditorApplication.projectChanged += RefreshRegistration;
	}

	private void OnDisable()
	{
		Undo.undoRedoPerformed -= OnDraftUndo;
		EditorApplication.projectChanged -= RefreshRegistration;
	}

	private void OnFocus() => RefreshRegistration();
	private void RefreshRegistration()
	{
		if (registrationView == null || createButton == null)
		{
			return;
		}

		registrationView.RefreshOptions();
		RefreshDraftView();
	}

	private void OnDraftUndo()
	{
		// Rebuild fields too: Undo may replace serialized nested Draft objects.
		if (validationContainer != null)
		{
			CreateGUI();
		}
	}

	public static void Open()
	{
		var window = GetWindow<CreateContentWindow>("Create Content");
		window.minSize = new Vector2(440, 420);
		window.Show();
	}

	public static void OpenWithTypes(IEnumerable<int> values)
	{
		var window = GetWindow<CreateContentWindow>("Create Content");
		window.knownTypes = values.Distinct().OrderBy(value => value).ToList();
		window.minSize = new Vector2(440, 420);
		window.CreateGUI();
		window.Show();
	}

	public void CreateGUI()
	{
		draft ??= new ContentCreationDraft();
		draft.Registration ??= new AddressRegistrationDraft();
		draft.Registration.FollowId(draft.Id);
		rootVisualElement.Clear();
		var scrollView = new ScrollView();
		rootVisualElement.Add(scrollView);
		scrollView.Add(new Label("Create Content — Prefab Variant"));
		scrollView.Add(new HelpBox(
			"Creates a Prefab Variant from the selected Base Template and adds the Source Model as a nested prefab.\n Existing assets will not be overwritten.",
			HelpBoxMessageType.Info));
		var information = new Foldout
		{
			text = "Content Information",
			value = true,
			viewDataKey = "contentInformation"
		};
		scrollView.Add(information);
		AddTypeControls(information);
		var contentId = new TextField("Content ID")
		{
			name = "contentId",
			value = draft.Id,
			isDelayed = false
		};
		contentId.RegisterValueChangedCallback(evt =>
		{
			EditDraft(() =>
			{
				draft.Id = evt.newValue;
				draft.Registration.FollowId(draft.Id);
			});
		});
		information.Add(contentId);
		var prefabSection = new Foldout
		{
			text = "Prefab References",
			value = true,
			viewDataKey = "prefabReferences"
		};
		scrollView.Add(prefabSection);
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
			EditorUtility.SetDirty(this);
			referenceView?.Refresh();
			RefreshDraftView();
		});
		prefabSection.Add(baseTemplate);
		prefabSection.Add(new Label("Changing Template resets the Reference Draft to the new Template contract."));
		var sourceModel = new ObjectField("Source Model Prefab")
		{
			objectType = typeof(GameObject),
			allowSceneObjects = false,
			value = draft.SourceModelPrefab
		};
		sourceModel.RegisterValueChangedCallback(evt => OnSourceModelChanged(evt.newValue as GameObject));
		prefabSection.Add(sourceModel);
		validationContainer = new VisualElement();
		draftPreview = new VisualElement();
		scrollView.Add(validationContainer);
		information.Add(draftPreview);
		draft.References ??= new ReferenceAuthoringDraft();
		if (!draft.References.Inspected && draft.BaseTemplatePrefab != null)
		{
			draft.References = ContentReferenceAuthoring.Inspect(draft.BaseTemplatePrefab);
		}

		referenceView = new ContentReferenceAuthoringView(draft, this, RefreshDraftView);
		prefabSection.Add(referenceView);
		registrationView = new ContentRegistrationView(
			draft,
			this,
			RefreshDraftView,
			labelSearch,
			value => labelSearch = value,
			SettingsProviderForChecks);
		scrollView.Add(registrationView);
		AddCreationControls(scrollView);
		RefreshDraftView();
	}

	private void EditDraft(System.Action edit)
	{
		Undo.RecordObject(this, "Edit content draft");
		edit();
		EditorUtility.SetDirty(this);
		RefreshDraftView();
	}

	private void AddTypeControls(VisualElement parent)
	{
		typeChoices = knownTypes.Distinct().OrderBy(value => value).ToList();
		if (draft.HasContentType && !typeChoices.Contains(draft.ContentType))
		{
			typeChoices.Add(draft.ContentType);
		}

		var names = new List<string>
		{
			"Select Content Type..."
		};
		names.AddRange(typeChoices.Select(value => "Content Type " + value));
		names.Add("Custom integer...");
		int index = customType ? names.Count - 1 : draft.HasContentType ? typeChoices.IndexOf(draft.ContentType) + 1 : 0;
		typeDropdown = new DropdownField("Content Type", names, index)
		{
			name = "contentType"
		};
		customTypeField = new IntegerField("Custom Content Type")
		{
			name = "customContentType",
			value = draft.ContentType,
			isDelayed = false
		};
		typeDropdown.RegisterValueChangedCallback(_ => EditDraft(() =>
		{
			customType = typeDropdown.index == names.Count - 1;
			draft.HasContentType = customType || typeDropdown.index > 0;
			if (!customType && draft.HasContentType)
			{
				draft.ContentType = typeChoices[typeDropdown.index - 1];
			}

			customTypeField.SetValueWithoutNotify(draft.ContentType);
			customTypeField.SetEnabled(customType);
		}));
		customTypeField.RegisterValueChangedCallback(evt => EditDraft(() =>
		{
			draft.ContentType = evt.newValue;
			draft.HasContentType = true;
		}));
		customTypeField.SetEnabled(customType);
		parent.Add(typeDropdown);
		parent.Add(customTypeField);
		parent.Add(new Label("Types are integer values observed in loaded content, not a Pet / Character enum. Custom accepts any 32-bit integer."));
	}

	private void AddCreationControls(VisualElement parent)
	{
		creationResult = new HelpBox(lastMessage, lastSucceeded ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning);
		createButton = new Button(() =>
		{
			registrationView.RefreshOptions();
			var result = ContentCreationFlow.Create(draft, registrationView.Settings);
			if (result.Variant?.Asset != null)
			{
				createdAsset = result.Variant.Asset;
			}

			if (result.Receipt != null)
			{
				pendingRegistration = result.Receipt;
			}

			ShowResult(result.Message, result.Succeeded);
			RefreshDraftView();
		})
		{
			text = "Create Variant"
		};
		parent.Add(createButton);
		parent.Add(creationResult);
		retryButton = new Button(() =>
		{
			var result = ContentCreationFlow.Retry(pendingRegistration, pendingRegistration?.Snapshot?.Registration?.Group?.Settings);
			ShowResult($"Last output GUID {pendingRegistration?.AssetGuid}: {result.Message}", result.Succeeded);
			RefreshDraftView();
		})
		{
			name = "retryRegistration",
			text = "Retry Registration (last output GUID only)"
		};
		parent.Add(retryButton);
		parent.Add(new Button(() =>
		{
			if (createdAsset != null)
			{
				EditorGUIUtility.PingObject(createdAsset);
			}
		})
		{
			text = "Ping Created Variant"
		});
		parent.Add(new Button(() =>
		{
			if (createdAsset != null)
			{
				Selection.activeObject = createdAsset;
			}
		})
		{
			text = "Select Created Variant"
		});
	}

	private void ShowResult(string message, bool succeeded)
	{
		lastMessage = message;
		lastSucceeded = succeeded;
		creationResult.text = message;
		creationResult.messageType = succeeded ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
	}

	private void OnSourceModelChanged(GameObject sourceModel)
	{
		Undo.RecordObject(this, "Change Source Model");
		draft.SourceModelPrefab = sourceModel;
		draft.Renderers.Clear();
		draft.HasDiscoveredRenderers = false;
		EditorUtility.SetDirty(this);
		if (IsPrefabAsset(sourceModel))
		{
			var renderers = DiscoverSourceRenderers(sourceModel);
			if (renderers != null)
			{
				ApplyRendererDiscovery(renderers);
			}
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
		return asset != null &&
			EditorUtility.IsPersistent(asset) &&
			PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.NotAPrefab &&
			asset.transform.parent == null;
	}

	private List<ValidationResult> ValidateDraft()
	{
		var results = new List<ValidationResult>();
		if (!draft.HasContentType)
		{
			AddError(results, "Content Type is required.");
		}

		if (string.IsNullOrWhiteSpace(draft.Id))
		{
			AddError(results, "Content ID is required.");
		}

		ValidatePrefab(draft.BaseTemplatePrefab, "Base Template Prefab", results);
		ValidatePrefab(draft.SourceModelPrefab, "Source Model Prefab", results);
		return results;
	}

	private static void ValidatePrefab(
		GameObject asset,
		string label,
		List<ValidationResult> results)
	{
		if (asset == null)
		{
			AddError(results, $"{label} is required.");
		}
		else if (!IsPrefabAsset(asset))
		{
			AddError(results, $"{label} must be a prefab asset root from the Project window.");
		}
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
		{
			validationContainer.Add(new HelpBox(result.Message, HelpBoxMessageType.Error));
		}

		var creationErrors = ContentVariantCreator.Validate(draft, out var outputPath);
		foreach (var error in creationErrors)
		{
			validationContainer.Add(new HelpBox(error, HelpBoxMessageType.Error));
		}

		validationContainer.Add(new Label($"Output: {outputPath ?? "Invalid ID"}"));
		createButton?.SetEnabled(results.Count == 0 && creationErrors.Count == 0);
		var referenceBlock = ContentReferenceAuthoring.Preflight(draft);
		if (referenceBlock != null)
		{
			validationContainer.Add(new HelpBox(referenceBlock, HelpBoxMessageType.Warning));
			createButton?.SetEnabled(false);
		}

		var registrationErrors = ContentAddressRegistration.Preflight(draft.Registration, registrationView?.Settings);
		if (registrationErrors.Count != 0)
		{
			createButton?.SetEnabled(false);
			validationContainer.Add(new HelpBox("Addressables Registration:\n" + string.Join("\n", registrationErrors), HelpBoxMessageType.Error));
		}

		registrationView?.RefreshPreview();
		retryButton?.SetEnabled(pendingRegistration != null);
		if (pendingRegistration != null)
		{
			retryButton.tooltip = "Frozen output: " + pendingRegistration.AssetPath + " / GUID " + pendingRegistration.AssetGuid;
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
		{
			draftPreview.Add(new Label("Select a valid Source Model prefab asset to begin."));
		}
		else if (!draft.HasDiscoveredRenderers)
		{
			draftPreview.Add(new Label("Renderer discovery: pending guided implementation."));
		}
		else
		{
			draftPreview.Add(new Label($"SkinnedMeshRenderers: {draft.Renderers.Count}"));
			foreach (var renderer in draft.Renderers)
			{
				draftPreview.Add(new Label(renderer != null ? renderer.name : "Missing renderer reference"));
			}
		}
	}
}
