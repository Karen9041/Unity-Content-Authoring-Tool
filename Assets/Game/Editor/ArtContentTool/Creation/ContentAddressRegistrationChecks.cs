using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>In-memory Addressables fixtures only. Never uses or saves the production Settings.</summary>
public static class ContentAddressRegistrationChecks
{
	private static int count;
	[MenuItem("Tools/Art Content Tool/Run M5 Registration Checks")]
	public static void Run()
	{
		count = 0;
		// Fingerprint serialized production settings, group/schema assets and .meta before any test.
		var fingerprints = Directory.GetFiles("Assets/AddressableAssetsData", "*", SearchOption.AllDirectories).ToDictionary(
			path => path,
			Fingerprint);
		var settings = AddressableAssetSettings.Create(null, "M5_InMemoryFixture", false, false);
		CreateContentWindow window = null;
		var focusedWindow = EditorWindow.focusedWindow;
		try
		{
			var group = FixtureGroup(settings, "Content", true);
			var other = FixtureGroup(settings, "Other");
			var system = FixtureGroup(settings, "unifiedraytracing");
			settings.AddLabel("existing", false);
			settings.AddLabel("selected", false);
			var request = new AddressRegistrationDraft();
			Check(
				!request.Enabled && ContentAddressRegistration.Preflight(request, null).Count == 0,
				"Off ignores missing Settings");
			request.FollowId(" first ");
			Check(request.Address == "first", "Auto address follows trimmed ID");
			request.EditAddress("manual");
			request.FollowId("second");
			Check(request.Address == "manual", "Manual address is preserved");
			request.UseSuggested(" second ");
			Check(!request.ManualAddress && request.Address == "second", "Explicit reset restores Auto");
			request.Enabled = true;
			request.Group = group;
			request.Labels.Add("selected");
			Check(
				ContentAddressRegistration.Preflight(request, settings, requireImplementation: false).Count == 0,
				"Valid isolated request");
			Check(!ContentAddressRegistration.Groups(settings).Contains(system), "System Group excluded");
			request.Group = system;
			Check(
				ContentAddressRegistration.Preflight(request, settings, requireImplementation: false).Count > 0,
				"System Group blocked");
			request.Group = group;
			settings.RemoveLabel("selected", false);
			Check(
				ContentAddressRegistration.Preflight(request, settings, requireImplementation: false).Any(error => error.Contains("selected")),
				"Removed label diagnosed");
			settings.AddLabel("selected", false);
			settings.groups.Remove(group);
			Check(
				ContentAddressRegistration.Preflight(request, settings, requireImplementation: false).Count > 0,
				"Removed Group diagnosed");
			settings.groups.Add(group);
			var template = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.SmokeTemplatePath);
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.FixtureFolder + "/SourceModel.prefab");
			Check(template != null && model != null, "Reusable fixtures available");
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(template));
			// Creating entries here changes ONLY the temporary, non-persisted fixture settings.
			var entry = settings.CreateOrMoveEntry(guid, other, false, false);
			entry.SetAddress("second", false);
			entry.SetLabel("existing", true, false, false);
			Check(
				ContentAddressRegistration.Preflight(request, settings, requireImplementation: false).Any(error => error.Contains("conflicts")),
				"Other GUID address conflict");
			var sameGuidErrors = ContentAddressRegistration.Preflight(request, settings, guid, false);
			Check(
				sameGuidErrors.Any(error => error.Contains("will not be moved")) && entry.parentGroup == other,
				"Existing Group is never moved by Preflight");
			Check(entry.labels.SetEquals(new[] {
				"existing"
			}), "Preflight preserves old labels");
			var draft = new ContentCreationDraft
			{
				HasContentType = true,
				ContentType = int.MinValue,
				Id = "M5_Guard_" + Guid.NewGuid().ToString("N"),
				BaseTemplatePrefab = template,
				SourceModelPrefab = model,
				References = ContentReferenceAuthoring.Inspect(template),
				Registration = request
			};
			request.Address = "unique_" + draft.Id;
			var path = ContentVariantCreator.GetOutputPath(draft.ContentType, draft.Id);
			request.Labels.Add("unknown_preflight_guard");
			var blocked = ContentCreationFlow.Create(draft, settings);
			request.Labels.Remove("unknown_preflight_guard");
			Check(
				blocked.Registration.Status == (ContentAddressRegistration.ImplementationReady
					? AddressRegistrationStatus.Rejected
					: AddressRegistrationStatus.NotImplemented) &&
					!blocked.Succeeded &&
					!ContentVariantCreator.PathOccupied(path),
				"Registration Preflight blocks invalid requests before Prefab creation");
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, request.Address, request.Labels).Status == AddressRegistrationStatus.Rejected &&
					entry.parentGroup == other,
				"Core rejects existing identity mismatch without mutation");
			var snapshot = JsonUtility.FromJson<ContentCreationDraft>(JsonUtility.ToJson(draft));
			var receipt = new ContentRegistrationReceipt
			{
				AssetGuid = guid,
				AssetPath = AssetDatabase.GetAssetPath(template),
				Snapshot = snapshot
			};
			draft.Id = "later_id";
			draft.Registration.Address = "later_address";
			var retried = ContentCreationFlow.Retry(receipt, settings);
			Check(
				retried.AssetGuid == guid &&
					retried.Status == (ContentAddressRegistration.ImplementationReady
					? AddressRegistrationStatus.Rejected
					: AddressRegistrationStatus.NotImplemented) &&
					receipt.Snapshot.Registration.Address != draft.Registration.Address,
				"Retry uses frozen original GUID and request");
			Check(
				ContentCreationFlow.Retry(
					new ContentRegistrationReceipt
					{
						AssetGuid = guid,
						AssetPath = "Assets/missing.prefab",
						Snapshot = snapshot
					},
					settings).Status == AddressRegistrationStatus.Rejected,
				"Retry rejects changed path without creating assets");
			window = ScriptableObject.CreateInstance<CreateContentWindow>();
			window.Show(); // UI Toolkit ChangeEvents require an attached panel.
			window.CreateGUI();
			var windowDraft = (ContentCreationDraft)typeof(CreateContentWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
			var type = window.rootVisualElement.Q<DropdownField>("contentType");
			type.index = type.choices.Count - 1;
			var custom = window.rootVisualElement.Q<IntegerField>("customContentType");
			custom.value = int.MinValue;
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
			custom.value = int.MaxValue;
			Undo.FlushUndoRecordObjects();
			Check(
				windowDraft.HasContentType && windowDraft.ContentType == int.MaxValue,
				"Custom Type accepts full int range");
			Undo.PerformUndo();
			Check(
				window.rootVisualElement.Q<IntegerField>("customContentType").value == int.MinValue,
				"Undo rebuilds matching fields");
			Undo.PerformRedo();
			Check(
				window.rootVisualElement.Q<IntegerField>("customContentType").value == int.MaxValue,
				"Redo rebuilds matching fields");
			windowDraft = (ContentCreationDraft)typeof(CreateContentWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
			Check(!windowDraft.Registration.Enabled, "Window Registration starts Off");
			var restored = JsonUtility.FromJson<ContentCreationDraft>(JsonUtility.ToJson(windowDraft));
			Check(
				restored.HasContentType && restored.ContentType == int.MaxValue && !restored.Registration.Enabled,
				"Draft serialization retains UI inputs");
			int reads = 0;
			var view = new ContentRegistrationView(windowDraft, window, () =>
			{
			}, "", _ =>
			{
			}, () =>
			{
				reads++;
				return settings;
			});
			window.rootVisualElement.Add(view);
			Check(
				reads == 0 && view.Q<DropdownField>("registrationGroup") == null,
				"Off hides settings and does not fetch them");
			view.Q<Toggle>("registerAddressables").value = true;
			Check(reads > 0 && windowDraft.Registration.Group == group, "On selects eligible default Group");
			view.Q<TextField>("registrationAddress").value = "manual_ui";
			windowDraft.Id = "changed_id";
			windowDraft.Registration.FollowId(windowDraft.Id);
			view.RefreshPreview();
			Check(view.Q<TextField>("registrationAddress").value == "manual_ui", "Manual UI address survives ID change");
			windowDraft.Registration.Labels.Add("selected");
			view.RefreshOptions();
			view.Q<TextField>("labelSearch").value = "no_matches";
			Check(windowDraft.Registration.Labels.Contains("selected"), "Label search preserves selection");
			view.Q<Toggle>("registerAddressables").value = false;
			Check(
				view.Q<TextField>("registrationAddress") == null && windowDraft.Registration.Address == "manual_ui",
				"Off hides but preserves inactive inputs");
			Check(
				fingerprints.Count == Directory.GetFiles("Assets/AddressableAssetsData", "*", SearchOption.AllDirectories).Length &&
					fingerprints.All(pair => File.Exists(pair.Key) && Fingerprint(pair.Key) == pair.Value),
				"Production Addressables assets and meta unchanged");
			Debug.Log($"M5 UI / safe registration checks passed ({count} checks). Fixture Settings were in memory only.");
		}
		finally
		{
			if (window != null)
			{
				Undo.ClearUndo(window);
				window.Close();
			}

			if (focusedWindow != null)
			{
				focusedWindow.Focus();
			}

			foreach (var group in settings.groups.ToArray())
			{
				foreach (var schema in group.Schemas)
				{
					Object.DestroyImmediate(schema);
				}

				Object.DestroyImmediate(group);
			}

			Object.DestroyImmediate(settings);
		}
	}

	private static string Fingerprint(string path)
	{
		using var hash = SHA256.Create();
		return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
	}

	private static void Check(bool condition, string name)
	{
		if (!condition)
		{
			throw new InvalidOperationException("M5 check failed: " + name);
		}

		count++;
	}

	[MenuItem("Tools/Art Content Tool/Run M5 Shared Validation Checks")]
	public static void RunValidation()
	{
		count = 0;
		var files = Directory.GetFiles("Assets/AddressableAssetsData", "*", SearchOption.AllDirectories).ToDictionary(
			path => path,
			Fingerprint);
		var settings = AddressableAssetSettings.Create(null, "M5_ValidationOnly", false, false);
		var foreign = AddressableAssetSettings.Create(null, "M5_ForeignOwner", false, false);
		try
		{
			var group = FixtureGroup(settings, "Content", true);
			var other = FixtureGroup(settings, "Other");
			var readOnly = FixtureGroup(settings, "ReadOnly", false, true);
			var foreignGroup = FixtureGroup(foreign, "Foreign", true);
			settings.AddLabel("existing", false);
			settings.AddLabel("selected", false);
			string path = ContentReferenceIntegrationChecks.SmokeTemplatePath;
			string guid = AssetDatabase.AssetPathToGUID(path);
			var entry = settings.CreateOrMoveEntry(guid, group, false, false);
			entry.SetAddress("shared", false);
			entry.SetLabel("existing", true, false, false);
			var labels = new[]
			{
				"selected"
			};
			var retry = ContentAddressRegistration.ValidateRequest(settings, group, "shared", labels, guid, true);
			Check(
				retry.IsValid && ReferenceEquals(retry.ExistingEntry, entry),
				"Same GUID retry is not an address conflict");
			Check(
				!ContentAddressRegistration.ValidateRequest(settings, group, "shared", labels).IsValid,
				"UI without GUID still detects an occupied address");
			var request = new AddressRegistrationDraft
			{
				Enabled = true,
				Group = group,
				Address = "shared",
				Labels = labels.ToList()
			};
			Check(
				ContentAddressRegistration.Preflight(request, settings, guid, false).SequenceEqual(retry.Errors),
				"Preflight and core share data validation");
			Check(
				!ContentAddressRegistration.ValidateRequest(settings, group, "shared", labels, requireAssetGuid: true).IsValid,
				"Core mode cannot silently omit its GUID");
			Check(
				!ContentAddressRegistration.ValidateRequest(settings, readOnly, "new", labels).IsValid,
				"Read-only Group rejected");
			settings.groups.Add(foreignGroup);
			Check(
				ContentAddressRegistration.ValidateRequest(settings, foreignGroup, "new", labels).Errors.Any(error => error.Contains("belong")),
				"Group ownership checked even if inserted into Settings list");
			settings.groups.Remove(foreignGroup);
			Check(
				!ContentAddressRegistration.ValidateRequest(settings, group, "new", new[] {
					"unknown"
				}).IsValid,
				"Unknown Label rejected");
			Check(
				!ContentAddressRegistration.ValidateRequest(settings, group, "new", null).IsValid,
				"Null Labels explicitly rejected");
			foreach (var address in new[]
			{
				"",
				" ",
				" padded",
				"trailing ",
				"a\nb",
				"a[b"
			}

			)
			{
				Check(
					!ContentAddressRegistration.ValidateRequest(settings, group, address, labels).IsValid,
					"Invalid Address rejected: " + address);
			}

			entry.ReadOnly = true;
			Check(
				ContentAddressRegistration.ValidateRequest(settings, group, "shared", labels, guid, true).Errors.Any(error => error.Contains("read-only")),
				"Read-only existing Entry rejected");
			entry.ReadOnly = false;
			string otherGuid = AssetDatabase.AssetPathToGUID(ContentReferenceIntegrationChecks.FixtureFolder + "/BaseTemplate.prefab");
			var duplicate = settings.CreateOrMoveEntry(otherGuid, other, false, false);
			duplicate.SetAddress("shared", false);
			Check(
				ContentAddressRegistration.ValidateRequest(settings, group, "shared", labels, guid, true).Errors.Any(error => error.Contains("conflicts")),
				"Same Address and different GUID conflict even on retry");
			duplicate.SetAddress("other", false);
			string before = EntryState(settings);
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "shared", labels).Status == AddressRegistrationStatus.Rejected,
				"Source Template cannot be registered as a generated output");
			Check(
				EntryState(settings) == before && entry.labels.SetEquals(new[] {
					"existing"
				}),
				"Core valid request has no Entry/Settings/Group side effects");
			entry.SetLabel("selected", true, false, false);
			before = EntryState(settings);
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "shared", labels).Status == AddressRegistrationStatus.Rejected,
				"Matching source Template is still not a verified generated output");
			Check(EntryState(settings) == before, "Matching-label branch has no side effects");
			var rejected = ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "shared", new[] {
				"unknown"
			});
			Check(
				rejected.Status == AddressRegistrationStatus.Rejected &&
					!rejected.Succeeded &&
					EntryState(settings) == before,
				"Core validation failure has no side effects");
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, "missing_guid", group, "new", labels).Status == AddressRegistrationStatus.Rejected,
				"User GUID identity guard retained");
			string folderPath = ContentReferenceIntegrationChecks.FixtureFolder;
			var folder = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(folderPath), other, false, false);
			folder.SetAddress("folder", false);
			var outerFolder = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID("Assets/Game/Tests/Fixtures"), other, false, false);
			outerFolder.SetAddress("outer", false);
			Check(
				ContentAddressRegistration.ValidateRequest(settings, group, "folder", labels).Errors.Any(error => error.Contains("conflicts")),
				"Direct Folder Entry's own Address included");
			string implicitPath = folderPath + "/SourceModel.prefab";
			string implicitGuid = AssetDatabase.AssetPathToGUID(implicitPath);
			string implicitAddress = "folder/SourceModel.prefab";
			Check(
				ContentAddressRegistration.ValidateRequest(settings, group, implicitAddress, labels).Errors.Any(error => error.Contains("implicit GUID")),
				"Folder implicit Address conflicts before UI has GUID");
			Check(
				ContentAddressRegistration.ValidateRequest(settings, other, implicitAddress, labels, implicitGuid, true).Errors.Any(error => error.Contains("implicitly registered")),
				"Same GUID implicit registration is not promoted");
			Check(
				ContentAddressRegistration.ValidateRequest(settings, group, "folder/SmokeTemplate.prefab", labels).IsValid,
				"Explicit child overrides Folder implicit path");
			Check(
				ContentAddressRegistration.ValidateRequest(
					settings,
					group,
					"outer/ReferenceAuthoring/SourceModel.prefab",
					labels).IsValid,
				"Explicit nested Folder excludes parent Folder subtree");
			var cached = settings.FindAssetEntry(implicitGuid, true);
			Check(
				cached != null && cached.ParentEntry == folder && cached.IsSubAsset,
				"2.8.1 implicit-cache fixture established");
			Check(
				ReferenceEquals(settings.FindAssetEntry(implicitGuid, false), cached),
				"2.8.1 GUID-only FindAssetEntry cache behavior confirmed");
			before = EntryState(settings);
			bool isFolder = folder.IsFolder;
			var subAssets = folder.SubAssets.ToArray();
			var cachedLabels = cached.labels.ToArray();
			var withWarmCache = ContentAddressRegistration.ValidateRequest(settings, other, implicitAddress, labels, implicitGuid, true);
			Check(
				!withWarmCache.IsValid && withWarmCache.ExistingEntry == null,
				"Validator ignores cached implicit Entry as direct identity");
			Check(
				before == EntryState(settings) &&
					folder.IsFolder == isFolder &&
					subAssets.SequenceEqual(folder.SubAssets) &&
					cached.labels.SetEquals(cachedLabels),
				"Validation does not dirty or update synthetic Folder Entries");
			// Package comparison is isolated too; it is not the production Validator's inspection path.
			var packageEntries = new List<AddressableAssetEntry>();
			settings.GetAllAssets(packageEntries, false);
			Check(
				packageEntries.Any(item => item.guid == implicitGuid && item.address == implicitAddress),
				"Implicit address agrees with installed package GetAllAssets");
			Check(
				!packageEntries.Any(item => item.guid == folder.guid),
				"Installed package GetAllAssets omits Folder root (direct scan required)");
			Check(
				files.Count == Directory.GetFiles("Assets/AddressableAssetsData", "*", SearchOption.AllDirectories).Length &&
					files.All(pair => Fingerprint(pair.Key) == pair.Value),
				"Production Settings/Groups/schemas/meta unchanged");
			Debug.Log($"M5 shared Validation PASS: {count} assertions. Settings/Groups/Entries were isolated in-memory fixtures; no Prefabs written.");
		}
		finally
		{
			DestroySettings(settings);
			DestroySettings(foreign);
		}
	}

	private static string EntryState(AddressableAssetSettings settings) => EditorJsonUtility.ToJson(settings) +
		string.Concat(settings.groups.Select(group => EditorJsonUtility.ToJson(group) + EditorUtility.IsDirty(group))) +
		EditorUtility.IsDirty(settings);
	private static void DestroySettings(AddressableAssetSettings settings)
	{
		foreach (var group in settings.groups.ToArray())
		{
			foreach (var schema in group.Schemas)
			{
				Object.DestroyImmediate(schema);
			}

			Object.DestroyImmediate(group);
		}

		Object.DestroyImmediate(settings);
	}

	// AddSchema in 2.8.1 calls global SaveAssets even for temporary Settings. Build fixtures without it.
	internal static AddressableAssetGroup FixtureGroup(
		AddressableAssetSettings settings,
		string name,
		bool defaultGroup = false,
		bool readOnly = false)
	{
		var group = settings.CreateGroup(name, defaultGroup, readOnly, false, null);
		var schema = ScriptableObject.CreateInstance<BundledAssetGroupSchema>();
		using (var serialized = new SerializedObject(schema))
		{
			serialized.FindProperty("m_Group").objectReferenceValue = group;
			serialized.ApplyModifiedPropertiesWithoutUndo();
		}

		group.Schemas.Add(schema);
		typeof(BundledAssetGroupSchema).GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(
			schema,
			null);
		if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(group)))
		{
			AssetDatabase.CreateAsset(schema, settings.ConfigFolder + "/" + name + "_Schema.asset");
			EditorUtility.SetDirty(group);
			AssetDatabase.SaveAssetIfDirty(schema);
		}

		return group;
	}

	// Execute Prepare, request a real script reload, then Complete. Only this test's Window is closed.
	public static void PrepareDomainReloadCheck()
	{
		const string key = "M5.DomainReloadWindow";
		if (SessionState.GetInt(key, 0) != 0)
		{
			throw new InvalidOperationException("A reload check is already pending.");
		}

		var window = ScriptableObject.CreateInstance<CreateContentWindow>();
		var draft = (ContentCreationDraft)typeof(CreateContentWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
		draft.HasContentType = true;
		draft.ContentType = -123;
		draft.Id = "M5_Reload_" + Guid.NewGuid().ToString("N");
		draft.Registration.EditAddress("manual_reload");
		draft.Registration.Labels.Add("retained_selection");
		draft.BaseTemplatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.SmokeTemplatePath);
		draft.SourceModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.FixtureFolder + "/SourceModel.prefab");
		draft.References = ContentReferenceAuthoring.Inspect(draft.BaseTemplatePrefab);
		var receipt = new ContentRegistrationReceipt
		{
			AssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(draft.BaseTemplatePrefab)),
			AssetPath = AssetDatabase.GetAssetPath(draft.BaseTemplatePrefab),
			Snapshot = JsonUtility.FromJson<ContentCreationDraft>(JsonUtility.ToJson(draft))
		};
		typeof(CreateContentWindow).GetField("pendingRegistration", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(
			window,
			receipt);
		typeof(CreateContentWindow).GetField("customType", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(
			window,
			true);
		draft.Id = "changed_after_receipt";
		EditorUtility.SetDirty(window);
		SessionState.SetInt(key, window.GetInstanceID());
		window.Show();
		window.CreateGUI();
	}

	public static void CompleteDomainReloadCheck()
	{
		const string key = "M5.DomainReloadWindow";
		int id = SessionState.GetInt(key, 0);
		var window = Resources.FindObjectsOfTypeAll<CreateContentWindow>().SingleOrDefault(item => item.GetInstanceID() == id);
		if (window == null)
		{
			throw new InvalidOperationException("Reload test Window was not restored.");
		}

		try
		{
			var draft = (ContentCreationDraft)typeof(CreateContentWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
			var receipt = (ContentRegistrationReceipt)typeof(CreateContentWindow).GetField("pendingRegistration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
			Check(
				draft.ContentType == -123 &&
					draft.HasContentType &&
					window.rootVisualElement.Q<IntegerField>("customContentType").value == -123,
				"Real Domain Reload restores custom Type and fields");
			Check(
				!draft.Registration.Enabled &&
					draft.Registration.ManualAddress &&
					draft.Registration.Address == "manual_reload" &&
					draft.Registration.Labels.Contains("retained_selection"),
				"Real Domain Reload preserves Off and Manual inputs");
			Check(
				receipt.Snapshot.Id.StartsWith("M5_Reload_", StringComparison.Ordinal) &&
					receipt.Snapshot.BaseTemplatePrefab == draft.BaseTemplatePrefab &&
					receipt.AssetGuid == AssetDatabase.AssetPathToGUID(receipt.AssetPath),
				"Real Domain Reload retains frozen receipt and asset references");
			Debug.Log("M5 real Domain Reload checks passed (3 checks).");
		}
		finally
		{
			SessionState.EraseInt(key);
			Undo.ClearUndo(window);
			window.Close();
		}
	}

	[MenuItem("Tools/Art Content Tool/Run M5 Registration Off Save Reload Check")]
	public static void RunCreationOff()
	{
		var template = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.SmokeTemplatePath);
		var model = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.FixtureFolder + "/SourceModel.prefab");
		var draft = new ContentCreationDraft
		{
			HasContentType = true,
			ContentType = 2,
			Id = "M5_Test_" + Guid.NewGuid().ToString("N"),
			BaseTemplatePrefab = template,
			SourceModelPrefab = model,
			References = ContentReferenceAuthoring.Inspect(template)
		};
		string path = ContentVariantCreator.GetOutputPath(draft.ContentType, draft.Id);
		Check(AssetDatabase.IsValidFolder(Path.GetDirectoryName(path)), "Use existing output folder only");
		Check(!ContentVariantCreator.PathOccupied(path), "Off test output is unused");
		var protectedFiles = new[]
		{
			AssetDatabase.GetAssetPath(template),
			AssetDatabase.GetAssetPath(model)
		}.SelectMany(
			item => new[] {
				item,
				item + ".meta"
			}).ToDictionary(
			item => item,
			Fingerprint);
		var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
		bool dirty = scene.isDirty;
		int previews = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
		var result = ContentCreationFlow.Create(draft, null);
		Check(
			result.Succeeded && result.Registration.Status == AddressRegistrationStatus.NotRequested,
			"Off creates without Settings");
		AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		Check(
			PrefabUtility.GetPrefabAssetType(saved) == PrefabAssetType.Variant &&
				PrefabUtility.GetCorrespondingObjectFromSource(saved) == template,
			"Off Variant base survives reload");
		Check(
			saved.GetComponentsInChildren<Transform>(true).Any(
				child => child != saved.transform &&
					PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
					PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == model),
			"Off Nested Model source survives reload");
		var duplicate = ContentCreationFlow.Create(draft, null);
		Check(
			!duplicate.Succeeded && duplicate.Variant.Status == VariantCreationStatus.InvalidInput,
			"Duplicate output rejected");
		Check(
			protectedFiles.All(pair => Fingerprint(pair.Key) == pair.Value),
			"Off leaves Template/Model/meta unchanged");
		Check(
			UnityEngine.SceneManagement.SceneManager.GetActiveScene() == scene &&
				scene.isDirty == dirty &&
				UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount == previews,
			"Off cleans Preview Scene and instances");
		var registration = ContentAddressRegistration.RegisterVerifiedPrefab(
			null,
			AssetDatabase.AssetPathToGUID(path),
			null,
			draft.Id,
			Array.Empty<string>());
		var failed = new ContentCreationOutcome
		{
			Variant = result.Variant,
			RegistrationRequested = true,
			Registration = registration
		};
		Check(
			!failed.Succeeded && File.Exists(path),
			"Registration failure never reports overall success or deletes Prefab");
		// Delete only this known, successful, unregistered run's output after checking incoming dependencies.
		var existing = ContentAddressRegistration.ExistingSettings();
		Check(
			existing == null || existing.FindAssetEntry(AssetDatabase.AssetPathToGUID(path), true) == null,
			"Test output is unregistered");
		Check(
			!AssetDatabase.GetAllAssetPaths().Where(
				item => item.StartsWith("Assets/", StringComparison.Ordinal) &&
					item != path &&
					!AssetDatabase.IsValidFolder(item)).Any(item => AssetDatabase.GetDependencies(item, false).Contains(path)),
			"No external assets reference the test output");
		Check(AssetDatabase.DeleteAsset(path), "Clean this run's successful test output");
		Debug.Log("M5 Registration Off Save / Reload / duplicate / cleanup checks passed. Test output removed.");
	}
}
