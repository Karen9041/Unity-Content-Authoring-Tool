using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Persisted, uniquely owned fixtures. Never assigns DefaultObject.Settings or saves all assets.</summary>
public static class ContentAddressRegistrationAcceptanceChecks
{
	private static int passed;
	private static string root;
	private static readonly List<string> outputs = new();
	public static string LastResult { get; private set; } = "Not run";

	public static string RunAndRecord()
	{
		LastResult = "Running";
		try
		{
			return LastResult = Run();
		}
		catch (Exception ex)
		{
			LastResult = "FAIL: " + ex.Message;
			throw;
		}
	}

	[MenuItem("Tools/Art Content Tool/Run M5 Persistence Acceptance")]
	public static string Run()
	{
		passed = 0;
		outputs.Clear();
		root = "Assets/M5_Acceptance_" + Guid.NewGuid().ToString("N");
		var protectedPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(path)).SelectMany(
			path => File.Exists(path + ".meta") ? new[] {
				path,
				path + ".meta"
			} : new[] {
				path
			}).Distinct().ToDictionary(
			path => path,
			Hash);
		var production = ContentAddressRegistration.ExistingSettings();
		string productionState = production == null ? "" : EditorJsonUtility.ToJson(production);
		var active = SceneManager.GetActiveScene();
		bool dirty = active.isDirty;
		int previews = EditorSceneManager.previewSceneCount;
		bool success = false;
		CreateContentWindow window = null;
		var previousFocus = EditorWindow.focusedWindow;
		// 2.8.1's default postprocessor auto-enrols ANY imported Group. Suspend dispatch only while
		// our synchronous fixture run imports its own assets, then restore the exact original handler.
		using var imports = new IsolatedImports();
		try
		{
			Check(AssetDatabase.CreateFolder("Assets", Path.GetFileName(root)) != "", "Own unique fixture folder");
			var settings = ScriptableObject.CreateInstance<AddressableAssetSettings>();
			settings.activeProfileId = settings.profileSettings.Reset();
			AssetDatabase.CreateAsset(settings, root + "/Settings.asset");
			settings.AddLabel("existing", false);
			settings.AddLabel("selected", false);
			var group = ContentAddressRegistrationChecks.FixtureGroup(settings, "M5_Content", true);
			var other = ContentAddressRegistrationChecks.FixtureGroup(settings, "M5_Other");
			foreach (var value in settings.groups)
			{
				AssetDatabase.SaveAssetIfDirty(value);
			}

			AssetDatabase.SaveAssetIfDirty(settings);
			var settingsHash = Hash(AssetDatabase.GetAssetPath(settings));
			var otherHash = Hash(AssetDatabase.GetAssetPath(other));
			string groupPath = AssetDatabase.GetAssetPath(group);
			var first = Create("First");
			string guid = AssetDatabase.AssetPathToGUID(first.OutputPath);
			var registered = ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "m5_first", new[] {
				"existing"
			});
			Check(
				registered.Status == AddressRegistrationStatus.Registered,
				"Create Entry / save / independent read-back: " + registered.Message);
			Check(
				registered.AssetGuid == guid &&
					registered.AssetPath == first.OutputPath &&
					registered.ActualGroupGuid == group.Guid &&
					registered.ActualAddress == "m5_first" &&
					registered.ActualLabels.SequenceEqual(new[] {
					"existing"
				}),
				"Actual success Result populated");
			settings.AddLabel("unsaved_definition", false);
			string definitionsBefore = Hash(AssetDatabase.GetAssetPath(settings));
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(
					settings,
					guid,
					group,
					"m5_first",
					new[] {
						"unsaved_definition"
					}).Status == AddressRegistrationStatus.Rejected &&
					Hash(AssetDatabase.GetAssetPath(settings)) == definitionsBefore &&
					!Direct(group, guid).labels.Contains("unsaved_definition"),
				"Unsaved Label definition cannot report registration success or force-save Settings");
			var updated = ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "m5_first", new[] {
				"selected"
			});
			Check(
				updated.Status == AddressRegistrationStatus.Registered &&
					new HashSet<string>(updated.ActualLabels).SetEquals(new[] {
					"existing",
					"selected"
				}),
				"Update preserves full label union: " + updated.Message);
			string fileBefore = Hash(groupPath);
			DateTime writeBefore = File.GetLastWriteTimeUtc(groupPath);
			var already = ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "m5_first", new[] {
				"selected"
			});
			Check(
				already.Status == AddressRegistrationStatus.AlreadyRegistered &&
					Hash(groupPath) == fileBefore &&
					File.GetLastWriteTimeUtc(groupPath) == writeBefore,
				"AlreadyRegistered does not save / change file");
			var second = Create("Second");
			string secondGuid = AssetDatabase.AssetPathToGUID(second.OutputPath);
			var conflict = ContentAddressRegistration.RegisterVerifiedPrefab(
				settings,
				secondGuid,
				group,
				"m5_first",
				Array.Empty<string>());
			Check(
				conflict.Status == AddressRegistrationStatus.Rejected &&
					Hash(groupPath) == fileBefore &&
					Direct(group, secondGuid) == null,
				"Cross-GUID conflict rejected without effects");
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, other, "m5_first", Array.Empty<string>()).Status == AddressRegistrationStatus.Rejected &&
					Direct(group, guid) != null &&
					Direct(other, guid) == null,
				"Existing Entry never moved");
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "rename", Array.Empty<string>()).Status == AddressRegistrationStatus.Rejected,
				"Existing Address never renamed");
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(
					settings,
					secondGuid,
					group,
					"unknown",
					new[] {
						"undefined"
					}).Status == AddressRegistrationStatus.Rejected,
				"Undefined Labels never created");
			// Dirty ownership is relevant only if an actual write would be needed.
			var pending = settings.CreateOrMoveEntry(secondGuid, group, false, false);
			pending.SetAddress("pending_user_edit", false);
			Check(EditorUtility.IsDirty(group), "Fixture Group has an unrelated pending edit");
			var unchangedDirty = ContentAddressRegistration.RegisterVerifiedPrefab(settings, guid, group, "m5_first", new[] {
				"selected"
			});
			Check(
				unchangedDirty.Status == AddressRegistrationStatus.AlreadyRegistered &&
					EditorUtility.IsDirty(group) &&
					Hash(groupPath) == fileBefore,
				"No-op with unrelated dirty edit neither saves nor discards it");
			var third = Create("Third");
			string thirdGuid = AssetDatabase.AssetPathToGUID(third.OutputPath);
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(settings, thirdGuid, group, "third", Array.Empty<string>()).Status == AddressRegistrationStatus.Rejected &&
					Hash(groupPath) == fileBefore &&
					Direct(group, thirdGuid) == null,
				"Dirty Group blocks writes before mutation");
			// Save only this test-owned edit, then verify the new-GUID path is usable again.
			AssetDatabase.SaveAssetIfDirty(group);
			// An unsaved target-label edit cannot masquerade as persistent AlreadyRegistered.
			Direct(group, secondGuid).SetLabel("selected", true, false, false);
			var memoryOnly = ContentAddressRegistration.RegisterVerifiedPrefab(
				settings,
				secondGuid,
				group,
				"pending_user_edit",
				new[] {
					"selected"
				});
			Check(
				memoryOnly.Status == AddressRegistrationStatus.Failed && EditorUtility.IsDirty(group),
				"Disk check rejects matching memory that was never saved");
			AssetDatabase.SaveAssetIfDirty(group);
			ContentAddressRegistration.PersistenceFaultForChecks = stage =>
			{
				if (stage == "save target Group")
				{
					throw new IOException("Isolated save fault");
				}
			};
			var saveFail = ContentAddressRegistration.RegisterVerifiedPrefab(settings, thirdGuid, group, "third", Array.Empty<string>());
			Check(
				saveFail.Status == AddressRegistrationStatus.Failed &&
					!saveFail.Succeeded &&
					saveFail.Message.Contains("Partial Entry"),
				"Save exception fails with partial-effects diagnostic");
			ContentAddressRegistration.PersistenceFaultForChecks = null;
			AssetDatabase.SaveAssetIfDirty(group); // Explicitly owned fixture recovery; never done by the service.
			var fourth = Create("ReadFail");
			string fourthGuid = AssetDatabase.AssetPathToGUID(fourth.OutputPath);
			ContentAddressRegistration.PersistenceFaultForChecks = stage =>
			{
				if (stage == "verify serialized Group file independently")
				{
					throw new IOException("Isolated read-back fault");
				}
			};
			var readFail = ContentAddressRegistration.RegisterVerifiedPrefab(
				settings,
				fourthGuid,
				group,
				"read_fail",
				Array.Empty<string>());
			Check(
				readFail.Status == AddressRegistrationStatus.Failed &&
					!readFail.Succeeded &&
					File.Exists(fourth.OutputPath),
				"Read-back exception fails and retains Prefab");
			ContentAddressRegistration.PersistenceFaultForChecks = null;
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(
					settings,
					fourthGuid,
					group,
					"read_fail",
					Array.Empty<string>()).Status == AddressRegistrationStatus.AlreadyRegistered,
				"Retry after saved-but-unverified result is idempotent");
			// Register a folder only in the isolated Group, then reject promotion of an implicit Variant.
			var implicitVariant = Create("Implicit");
			string folderGuid = AssetDatabase.AssetPathToGUID("Assets/Game/GeneratedContent/Type_2");
			var folder = settings.CreateOrMoveEntry(folderGuid, other, false, false);
			folder.SetAddress("folder", false);
			AssetDatabase.SaveAssetIfDirty(other);
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(
					settings,
					AssetDatabase.AssetPathToGUID(implicitVariant.OutputPath),
					group,
					"folder/" + Path.GetFileName(implicitVariant.OutputPath),
					Array.Empty<string>()).Status == AddressRegistrationStatus.Rejected,
				"Implicit Folder Variant safely rejected");
			settings.RemoveAssetEntry(folderGuid, false);
			AssetDatabase.SaveAssetIfDirty(other);
			Check(
				ContentAddressRegistration.RegisterVerifiedPrefab(
					settings,
					AssetDatabase.AssetPathToGUID(ContentReferenceIntegrationChecks.SmokeTemplatePath),
					group,
					"source_template",
					Array.Empty<string>()).Status == AddressRegistrationStatus.Rejected,
				"Source Template refused");
			if (ContentAddressRegistration.ImplementationReady)
			{
				// Real Window callbacks use injected fixture Settings, including its Retry button.
				window = ScriptableObject.CreateInstance<CreateContentWindow>();
				window.SettingsProviderForChecks = () => settings;
				window.Show();
				window.CreateGUI();
				var draft = WindowField<ContentCreationDraft>(window, "draft");
				CopyDraft(Draft("UI_Off"), draft);
				window.CreateGUI();
				Click(window.rootVisualElement.Query<Button>().ToList().Single(button => button.text == "Create Variant"));
				string offPath = ContentVariantCreator.GetOutputPath(draft.ContentType, draft.Id);
				outputs.Add(offPath);
				Check(
					WindowField<bool>(window, "lastSucceeded") && File.Exists(offPath),
					"UI Toggle Off creates independently");
				CopyDraft(Draft("UI_On"), draft);
				window.CreateGUI();
				window.rootVisualElement.Q<Toggle>("registerAddressables").value = true;
				Click(window.rootVisualElement.Query<Button>().ToList().Single(button => button.text == "Create Variant"));
				var receipt = WindowField<ContentRegistrationReceipt>(window, "pendingRegistration");
				if (receipt != null && !string.IsNullOrEmpty(receipt.AssetPath))
				{
					outputs.Add(receipt.AssetPath);
				}

				Check(
					WindowField<bool>(window, "lastSucceeded") &&
						receipt != null &&
						Direct(group, receipt.AssetGuid) != null,
					"UI On: Create / Prefab Verify / Register / Verify: " + WindowField<string>(window, "lastMessage"));
				CopyDraft(Draft("UI_Fail"), draft);
				window.CreateGUI();
				window.rootVisualElement.Q<Toggle>("registerAddressables").value = true;
				ContentAddressRegistration.PersistenceFaultForChecks = stage =>
				{
					if (stage == "verify serialized Group file independently")
					{
						throw new IOException("UI read fault");
					}
				};
				Click(window.rootVisualElement.Query<Button>().ToList().Single(button => button.text == "Create Variant"));
				receipt = WindowField<ContentRegistrationReceipt>(window, "pendingRegistration");
				outputs.Add(receipt.AssetPath);
				Check(
					!WindowField<bool>(window, "lastSucceeded") && File.Exists(receipt.AssetPath),
					"UI partial success is failure with Prefab retained");
				var prefabHash = Hash(receipt.AssetPath);
				var prefabMeta = Hash(receipt.AssetPath + ".meta");
				draft.Id = "later_draft";
				draft.Registration.Address = "later_address";
				ContentAddressRegistration.PersistenceFaultForChecks = null;
				Click(window.rootVisualElement.Q<Button>("retryRegistration"));
				Check(
					WindowField<bool>(window, "lastSucceeded") &&
						Hash(receipt.AssetPath) == prefabHash &&
						Hash(receipt.AssetPath + ".meta") == prefabMeta &&
						Direct(group, receipt.AssetGuid).address == receipt.Snapshot.Registration.Address,
					"UI Retry uses original GUID without recreating Prefab");
				Undo.ClearUndo(window);
				window.Close();
				window = null;
			}

			Check(
				Hash(AssetDatabase.GetAssetPath(settings)) == settingsHash &&
					Hash(AssetDatabase.GetAssetPath(other)) == otherHash,
				"Registration did not save Settings or another Group");
			// Strong fixture evidence comes last: unloading invalidates live references, so it is never a production verification step.
			string unionAddress = Direct(group, guid).address;
			Undo.ClearUndo(group);
			Resources.UnloadAsset(group);
			group = AssetDatabase.LoadAssetAtPath<AddressableAssetGroup>(groupPath);
			Check(
				Direct(group, guid).address == unionAddress &&
					Direct(group, guid).labels.SetEquals(new[] {
					"existing",
					"selected"
				}),
				"Strong persistence: fixture Group unloaded / freshly loaded from disk");
			Check(
				protectedPaths.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value),
				"All pre-existing assets and meta byte-identical");
			Check(
				production == null || EditorJsonUtility.ToJson(production) == productionState,
				"Production Settings memory unchanged");
			Check(
				SceneManager.GetActiveScene() == active &&
					active.isDirty == dirty &&
					EditorSceneManager.previewSceneCount == previews,
				"Active Scene and preview cleanup preserved");
			success = true;
			string result = $"M5 persistence acceptance PASS: {passed} assertions; UI integration enabled={ContentAddressRegistration.ImplementationReady}.";
			Debug.Log(result);
			return result;
		}
		finally
		{
			ContentAddressRegistration.PersistenceFaultForChecks = null;
			if (window != null)
			{
				Undo.ClearUndo(window);
				window.Close();
			}

			if (previousFocus != null)
			{
				previousFocus.Focus();
			}

			if (success)
			{
				// Remove only this uniquely owned fixture unit and its outputs. Check external dependencies first.
				var owned = new HashSet<string>(AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith(root + "/", StringComparison.Ordinal)).Concat(outputs));
				var external = AssetDatabase.GetAllAssetPaths().Where(
					path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
						!owned.Contains(path) &&
						!AssetDatabase.IsValidFolder(path)).ToArray();
				bool referenced = AssetDatabase.GetDependencies(external, false).Any(owned.Contains);
				if (referenced)
				{
					throw new InvalidOperationException("Test fixtures gained external references; outputs retained at " + root);
				}

				foreach (var path in outputs)
				{
					if (File.Exists(path) && !AssetDatabase.DeleteAsset(path))
					{
						throw new IOException("Cannot clean " + path);
					}
				}

				foreach (var obj in Resources.FindObjectsOfTypeAll<AddressableAssetGroup>())
				{
					if (AssetDatabase.GetAssetPath(obj).StartsWith(root + "/", StringComparison.Ordinal))
					{
						Undo.ClearUndo(obj);
					}
				}

				if (!AssetDatabase.DeleteAsset(root))
				{
					throw new IOException("Cannot clean fixture folder " + root);
				}
			}
			else
			{
				Debug.LogWarning("M5 failed fixtures retained for diagnosis: " + root + "\n" + string.Join("\n", outputs));
			}

			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
		}
	}

	private static ContentCreationDraft Draft(string suffix)
	{
		var template = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.SmokeTemplatePath);
		return new ContentCreationDraft
		{
			HasContentType = true,
			ContentType = 2,
			Id = "M5_Accept_" + Path.GetFileName(root) + "_" + suffix,
			BaseTemplatePrefab = template,
			SourceModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentReferenceIntegrationChecks.FixtureFolder + "/SourceModel.prefab"),
			References = ContentReferenceAuthoring.Inspect(template)
		};
	}

	private static VariantCreationResult Create(string suffix)
	{
		var result = ContentVariantCreator.Create(Draft(suffix));
		if (result.OutputPath != null && File.Exists(result.OutputPath))
		{
			outputs.Add(result.OutputPath);
		}

		Check(
			result.Status == VariantCreationStatus.Succeeded,
			"Create verified acceptance Variant: " + result.Message);
		return result;
	}

	private static AddressableAssetEntry Direct(AddressableAssetGroup group, string guid) => group.entries.SingleOrDefault(value => value.guid == guid);
	public static void CleanRetainedRun(string ownedRoot)
	{
		if (!ownedRoot.StartsWith("Assets/M5_Acceptance_", StringComparison.Ordinal) ||
			!Directory.Exists(ownedRoot) ||
			!File.Exists(ownedRoot + "/Settings.asset"))
		{
			throw new InvalidOperationException("Not an identified M5 acceptance fixture root.");
		}

		string token = "Content_M5_Accept_" + Path.GetFileName(ownedRoot) + "_";
		var prefabs = AssetDatabase.GetAllAssetPaths().Where(
			path => path.StartsWith("Assets/Game/GeneratedContent/Type_2/" + token, StringComparison.Ordinal) &&
				path.EndsWith(".prefab", StringComparison.Ordinal)).ToArray();
		var owned = new HashSet<string>(AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith(ownedRoot + "/", StringComparison.Ordinal)).Concat(prefabs));
		var production = ContentAddressRegistration.ExistingSettings();
		if (production != null &&
			production.groups.Any(
			group => group != null &&
				(owned.Contains(AssetDatabase.GetAssetPath(group)) ||
				group.entries.Any(entry => owned.Contains(AssetDatabase.GUIDToAssetPath(entry.guid))))))
		{
			throw new InvalidOperationException("Production Settings reference retained test assets; cleanup refused.");
		}

		var external = AssetDatabase.GetAllAssetPaths().Where(
			path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
				!owned.Contains(path) &&
				!AssetDatabase.IsValidFolder(path)).ToArray();
		if (AssetDatabase.GetDependencies(external, false).Any(owned.Contains))
		{
			throw new InvalidOperationException("External references found; cleanup refused.");
		}

		using var imports = new IsolatedImports();
		foreach (var path in prefabs)
		{
			if (!AssetDatabase.DeleteAsset(path))
			{
				throw new IOException("Cannot delete owned output " + path);
			}
		}

		foreach (var group in Resources.FindObjectsOfTypeAll<AddressableAssetGroup>())
		{
			if (AssetDatabase.GetAssetPath(group).StartsWith(ownedRoot + "/", StringComparison.Ordinal))
			{
				Undo.ClearUndo(group);
			}
		}

		if (!AssetDatabase.DeleteAsset(ownedRoot))
		{
			throw new IOException("Cannot delete owned fixture root " + ownedRoot);
		}

		AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
	}

	public static void CleanRetainedRuns()
	{
		foreach (var path in AssetDatabase.GetAllAssetPaths().Where(
			path => path.StartsWith("Assets/M5_Acceptance_", StringComparison.Ordinal) &&
				path.Count(c => c == '/') == 1 &&
				AssetDatabase.IsValidFolder(path)).ToArray())
		{
			CleanRetainedRun(path);
		}
	}

	private static void CopyDraft(ContentCreationDraft from, ContentCreationDraft to)
	{
		to.ContentType = from.ContentType;
		to.HasContentType = true;
		to.Id = from.Id;
		to.BaseTemplatePrefab = from.BaseTemplatePrefab;
		to.SourceModelPrefab = from.SourceModelPrefab;
		to.References = from.References;
		to.Registration = from.Registration;
	}

	private static T WindowField<T>(CreateContentWindow window, string name) => (T)typeof(CreateContentWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
	private static void Click(Button button)
	{
		using var evt = new NavigationSubmitEvent
		{
			target = button
		};
		button.SendEvent(evt);
	}

	private static string Hash(string path)
	{
		using var hash = SHA256.Create();
		return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException("M5 acceptance failed: " + message);
		}

		passed++;
	}

	private sealed class IsolatedImports : IDisposable
	{
		private readonly FieldInfo field;
		private readonly object original;
		public IsolatedImports()
		{
			var type = typeof(AddressableAssetSettings).Assembly.GetType(
				"UnityEditor.AddressableAssets.Settings.AddressablesAssetPostProcessor",
				true);
			field = type.GetField("s_OnPostProcessHandler", BindingFlags.NonPublic | BindingFlags.Static);
			if (field == null)
			{
				throw new NotSupportedException("Cannot isolate Addressables 2.8.1 import dispatch; no fixtures were created.");
			}

			original = field.GetValue(null);
			field.SetValue(null, Activator.CreateInstance(field.FieldType, true));
		}

		public void Dispose() => field.SetValue(null, original);
	}
}
