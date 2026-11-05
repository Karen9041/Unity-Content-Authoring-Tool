using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Editor checks using public assets and disposable preview instances. No asset saves or deletes.</summary>
public static class ContentReferenceAuthoringChecks
{
	[MenuItem("Tools/Art Content Tool/Run M2.2 Reference Checks")]
	public static void Run()
	{
		RunForTemplate(ContentReferenceIntegrationChecks.SmokeTemplatePath);
	}

	// Integration review supplies a fresh, valid fixture instead of rewriting user-authored Template entries.
	public static void RunForTemplate(string templatePath)
	{
		const string modelPath = "Assets/Game/Arts/TestModels/Quaternius/Pirate Kit/FBX/Characters_Shark.fbx";
		var template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
		var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
		Check(template != null && model != null, "Public fixture assets exist");
		var templateHash = AssetDatabase.GetAssetDependencyHash(templatePath);
		var modelHash = AssetDatabase.GetAssetDependencyHash(modelPath);
		var activeScene = SceneManager.GetActiveScene();
		bool dirtyBefore = activeScene.isDirty;
		int sceneCount = SceneManager.sceneCount;
		int previewCount = EditorSceneManager.previewSceneCount;
		var preview = EditorSceneManager.NewPreviewScene();
		int assertions = 0;
		try
		{
			var draft = new ContentCreationDraft
			{
				BaseTemplatePrefab = template,
				SourceModelPrefab = model,
				HasContentType = true,
				ContentType = 0,
				Id = "m22_guard_" + Guid.NewGuid().ToString("N"),
				References = ContentReferenceAuthoring.Inspect(template)
			};
			Check(!draft.References.NeedsApply, "Inspection preserves baseline");
			assertions++;
			var owner = draft.References.Entries[0].Target;
			var contractIssues = ContentReferenceAuthoring.Validate(draft);
			Check(
				contractIssues.Count == 0,
				"Template fixture must have valid Entries: " + string.Join("; ", contractIssues));
			assertions++;
			draft.References.Entries.Insert(0, null);
			Check(
				ContentReferenceAuthoring.Validate(draft).Exists(error => error.Contains("null Entry")),
				"Null entry returns a diagnostic");
			assertions++;
			draft.References.Entries.RemoveAt(0);
			var inherited = (GameObject)PrefabUtility.InstantiatePrefab(template, preview);
			Check(
				ContentReferenceAuthoring.Verify(draft, inherited, null).Count == 0,
				"Template fixed references inherited on preview instance");
			assertions++;
			Check(ContentReferenceAuthoring.Verify(draft, template, null).Count == 0, "Read-back of unchanged contract");
			assertions++;
			var entry = new ReferenceDraftEntry
			{
				Target = owner,
				Key = "public_note",
				ValueType = ContentReferenceValueType.String,
				StringValue = "example string value"
			};
			draft.References.Entries.Add(entry);
			Check(draft.References.NeedsApply, "Add entry changes Draft");
			assertions++;
			Check(ContentReferenceAuthoring.ValidateEntry(draft, entry) == null, "String value is valid");
			assertions++;
			entry.Key = "renamed_note";
			Check(draft.References.Entries.Exists(row => row.Key == "renamed_note"), "Rename is Draft only");
			assertions++;
			draft.References.Entries.Add(entry.Copy());
			Check(
				ContentReferenceAuthoring.Validate(draft).Exists(error => error.Contains("Duplicate")),
				"Duplicate keys rejected");
			assertions++;
			draft.References.Entries.RemoveAt(draft.References.Entries.Count - 1);
			entry.ObjectValue = model;
			Check(ContentReferenceAuthoring.ValidateEntry(draft, entry) != null, "String/object separation enforced");
			assertions++;
			entry.ObjectValue = null;
			if (!ContentReferenceAuthoring.ApplyImplementationReady)
			{
				string output = ContentVariantCreator.GetOutputPath(draft.ContentType, draft.Id);
				bool folderExisted = Directory.Exists(Path.GetDirectoryName(output));
				var blocked = ContentVariantCreator.Create(draft);
				Check(
					blocked.Status == VariantCreationStatus.NotImplemented &&
						!File.Exists(output) &&
						Directory.Exists(Path.GetDirectoryName(output)) == folderExisted,
					"Edited Draft blocked before any output/folders");
				assertions++;
				Check(
					ContentReferenceAuthoring.Verify(draft, inherited, null).Count > 0,
					"Unapplied additions cannot pass read-back");
				assertions++;
			}

			draft.References.Entries.Remove(entry);
			Check(!draft.References.NeedsApply, "Remove newly added entry restores inherited baseline");
			assertions++;
			// Two copies intentionally have the same names; selection remains exact Unity Object identity.
			var firstModel = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
			var secondModel = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
			firstModel.transform.SetParent(inherited.transform, false);
			secondModel.transform.SetParent(inherited.transform, false);
			draft.SourceModelPrefab = inherited;
			var renderers = ContentReferenceAuthoring.Candidates(inherited, ContentReferenceValueType.SkinnedMeshRenderer);
			Check(
				renderers.Count >= 2 && renderers[0] != renderers[1],
				"Two independently selectable renderers including same names");
			assertions++;
			entry = new ReferenceDraftEntry
			{
				Target = owner,
				Key = "chosen_mesh",
				ValueType = ContentReferenceValueType.SkinnedMeshRenderer,
				Origin = ReferenceValueOrigin.SourceModel,
				SelectedSourceRoot = inherited,
				ObjectValue = renderers[1]
			};
			Check(ContentReferenceAuthoring.ValidateEntry(draft, entry) == null, "Explicit second renderer is valid");
			assertions++;
			Check(
				ContentReferenceAuthoring.Describe(renderers[0]) != ContentReferenceAuthoring.Describe(renderers[1]),
				"Picker labels distinguish hierarchy identities");
			assertions++;
			entry.ValueType = ContentReferenceValueType.Animator;
			Check(ContentReferenceAuthoring.ValidateEntry(draft, entry) != null, "Wrong type rejected");
			assertions++;
			entry.ValueType = ContentReferenceValueType.SkinnedMeshRenderer;
			draft.SourceModelPrefab = model;
			Check(
				ContentReferenceAuthoring.ValidateEntry(draft, entry)?.Contains("Source Model changed") == true,
				"Model switch invalidates selection");
			assertions++;
			var textures = AssetDatabase.FindAssets("t:Texture2D", new[] {
				"Assets"
			});
			Check(textures.Length > 0, "Public texture fixture exists");
			assertions++;
			entry.ValueType = ContentReferenceValueType.Texture;
			entry.Origin = ReferenceValueOrigin.Asset;
			entry.ObjectValue = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(textures[0]));
			Check(ContentReferenceAuthoring.ValidateEntry(draft, entry) == null, "External texture asset accepted");
			assertions++;
			entry.ObjectValue = model.transform;
			Check(
				ContentReferenceAuthoring.ValidateEntry(draft, entry) != null,
				"Source components cannot masquerade as texture assets");
			assertions++;
			// Fixture serialization checks runtime string/object access without implementing the guided Apply method.
			var fixture = inherited.GetComponent<ContentPrefabReferences>();
			using (var serialized = new SerializedObject(fixture))
			{
				var items = serialized.FindProperty("entries");
				items.arraySize = 2;
				var text = items.GetArrayElementAtIndex(0);
				text.FindPropertyRelative("Key").stringValue = "note";
				text.FindPropertyRelative("ValueType").enumValueIndex = (int)ContentReferenceValueType.String;
				text.FindPropertyRelative("StringValue").stringValue = "literal";
				text.FindPropertyRelative("ObjectValue").objectReferenceValue = null;
				var texture = items.GetArrayElementAtIndex(1);
				texture.FindPropertyRelative("Key").stringValue = "image";
				texture.FindPropertyRelative("ValueType").enumValueIndex = (int)ContentReferenceValueType.Texture;
				texture.FindPropertyRelative("StringValue").stringValue = "";
				texture.FindPropertyRelative("ObjectValue").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(textures[0]));
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			Check(
				fixture.TryGetString("note", out var literal) &&
					literal == "literal" &&
					!fixture.TryGetObject<Object>("note", out _) &&
					fixture.TryGetObject<Texture>("image", out _) &&
					!fixture.TryGetString("image", out _),
				"Runtime typed string/object lookup");
			assertions++;
			var scanned = ContentReferenceAuthoring.Inspect(inherited);
			Check(
				scanned.Entries.Exists(row => row.Key == "note" && row.StringValue == "literal") &&
					!scanned.NeedsApply,
				"Dynamic Template entries inspected without edits");
			assertions++;
			var baselineNote = scanned.Entries.Find(row => row.Key == "note");
			baselineNote.Key = "renamed_inherited";
			Check(
				scanned.NeedsApply && scanned.Baseline.Exists(row => row.Key == "note"),
				"Rename inherited entry preserves baseline");
			assertions++;
			scanned.Entries.Remove(baselineNote);
			Check(
				scanned.NeedsApply && scanned.Entries.Count == scanned.Baseline.Count - 1,
				"Remove inherited entry is a pending list override");
			assertions++;
			// Exercise the independent verifier with a hand-assigned fixture, never a production Apply implementation.
			draft.References.Entries.Add(new ReferenceDraftEntry
			{
				Target = owner,
				Key = "model_root",
				ValueType = ContentReferenceValueType.Transform,
				Origin = ReferenceValueOrigin.SourceModel,
				SelectedSourceRoot = model,
				ObjectValue = model.transform
			});
			using (var serialized = new SerializedObject(fixture))
			{
				var items = serialized.FindProperty("entries");
				items.arraySize = 1;
				var item = items.GetArrayElementAtIndex(0);
				item.FindPropertyRelative("Key").stringValue = "model_root";
				item.FindPropertyRelative("ValueType").enumValueIndex = (int)ContentReferenceValueType.Transform;
				item.FindPropertyRelative("StringValue").stringValue = "";
				item.FindPropertyRelative("ObjectValue").objectReferenceValue = model.transform;
				serialized.ApplyModifiedPropertiesWithoutUndo();
				Check(
					ContentReferenceAuthoring.Verify(draft, inherited, firstModel).Count > 0,
					"Source asset component cannot pass as nested instance reference");
				assertions++;
				item.FindPropertyRelative("ObjectValue").objectReferenceValue = firstModel.transform;
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}

			Check(
				ContentReferenceAuthoring.Verify(draft, inherited, firstModel).Count == 0,
				"Exact nested instance identity passes independent verifier");
			assertions++;
			Check(
				ContentReferenceAuthoring.Verify(draft, inherited, secondModel).Count > 0,
				"Wrong copy of same Model cannot pass identity verification");
			assertions++;
			var window = ScriptableObject.CreateInstance<CreateContentWindow>();
			try
			{
				window.CreateGUI();
				Check(
					window.rootVisualElement.Query<Button>().ToList().Count > 0 &&
						window.rootVisualElement.Query<HelpBox>().ToList().Count > 0,
					"Create Window constructs controls and initialized HelpBoxes");
				assertions++;
			}
			finally
			{
				Object.DestroyImmediate(window);
			}
		}
		finally
		{
			if (preview.IsValid())
			{
				EditorSceneManager.ClosePreviewScene(preview);
			}
		}

		Check(
			AssetDatabase.GetAssetDependencyHash(templatePath) == templateHash &&
				AssetDatabase.GetAssetDependencyHash(modelPath) == modelHash,
			"Template / Model untouched");
		assertions++;
		Check(
			SceneManager.sceneCount == sceneCount &&
				EditorSceneManager.previewSceneCount == previewCount &&
				SceneManager.GetActiveScene() == activeScene &&
				activeScene.isDirty == dirtyBefore,
			"Preview cleanup and active scene unchanged");
		assertions++;
		Debug.Log($"M2.2 Reference checks passed ({assertions} assertions). Run Save Reload Checks for persistence coverage.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException("M2.2 check failed: " + message);
		}
	}
}
