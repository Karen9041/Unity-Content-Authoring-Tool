using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

[Serializable]
public sealed class AddressRegistrationDraft
{
	public bool Enabled;
	public AddressableAssetGroup Group;
	public bool GroupInitialized;
	public string Address = "";
	public bool ManualAddress;
	public List<string> Labels = new();

	public static string Suggest(string id) => (id ?? "").Trim();

	public void FollowId(string id)
	{
		if (!ManualAddress)
		{
			Address = Suggest(id);
		}
	}

	public void EditAddress(string value)
	{
		Address = value;
		ManualAddress = true;
	}

	public void UseSuggested(string id)
	{
		ManualAddress = false;
		FollowId(id);
	}
}

public enum AddressRegistrationStatus
{
	NotRequested,
	NotImplemented,
	Rejected,
	Failed,
	Registered,
	AlreadyRegistered
}

public sealed class AddressRegistrationResult
{
	public AddressRegistrationStatus Status { get; }
	public string Message { get; }
	public string AssetGuid { get; }
	public string AssetPath { get; }
	public string ActualGroupGuid { get; }
	public string ActualAddress { get; }
	public IReadOnlyCollection<string> ActualLabels { get; }
	public bool Succeeded => Status == AddressRegistrationStatus.Registered ||
		Status == AddressRegistrationStatus.AlreadyRegistered;

	public AddressRegistrationResult(
		AddressRegistrationStatus status,
		string message,
		string assetGuid = null,
		string assetPath = null,
		string actualGroupGuid = null,
		string actualAddress = null,
		IEnumerable<string> actualLabels = null)
	{
		Status = status;
		Message = message;
		AssetGuid = assetGuid;
		AssetPath = assetPath;
		ActualGroupGuid = actualGroupGuid;
		ActualAddress = actualAddress;
		ActualLabels = (actualLabels ?? Array.Empty<string>()).ToArray();
	}
}

/// <summary>Validation only; ExistingEntry is a direct Entry, not a persistence receipt or a reservation.</summary>
public sealed class RegistrationRequestValidation
{
	public IReadOnlyList<string> Errors { get; }
	public AddressableAssetEntry ExistingEntry { get; }
	public bool IsValid => Errors.Count == 0;

	public RegistrationRequestValidation(IEnumerable<string> errors, AddressableAssetEntry existingEntry)
	{
		Errors = errors.Distinct().ToArray();
		ExistingEntry = existingEntry;
	}
}

/// <summary>Shared validation and bounded registration of generated, verified Prefab Variants.</summary>
public static class ContentAddressRegistration
{
	public static bool ImplementationReady => true;

	// Null in normal operation; acceptance checks inject exceptions only for their isolated Settings.
	internal static Action<string> PersistenceFaultForChecks;
	public const string NotImplementedMessage = "Not Implemented: RegisterVerifiedPrefab() must validate, register, save and read back before registration can be enabled. No Prefab was created by this request.";

	// SettingsExists prevents the package getter's legacy migration / implicit creation path.
	public static AddressableAssetSettings ExistingSettings() =>
		AddressableAssetSettingsDefaultObject.SettingsExists
			? AddressableAssetSettingsDefaultObject.Settings
			: null;

	public static bool EligibleGroup(
		AddressableAssetSettings settings,
		AddressableAssetGroup group) =>
		settings != null && !GroupErrors(settings, group).Any();

	private static IEnumerable<string> GroupErrors(AddressableAssetSettings settings, AddressableAssetGroup group)
	{
		if (group == null)
		{
			yield return "Target Group is missing.";
			yield break;
		}

		if (!settings.groups.Contains(group) || group.Settings != settings)
		{
			yield return "Target Group does not belong to the supplied Settings.";
		}

		if (group.ReadOnly)
		{
			yield return "Target Group is read-only.";
		}

		if (string.Equals(group.Name, "unifiedraytracing", StringComparison.OrdinalIgnoreCase))
		{
			yield return "System Group 'unifiedraytracing' is not eligible for content registration.";
		}

		if (!group.HasSchema<BundledAssetGroupSchema>() ||
			!group.GetSchema<BundledAssetGroupSchema>().IncludeInBuild)
		{
			yield return "Target Group requires a BundledAssetGroupSchema with Include In Build enabled.";
		}
	}

	public static List<AddressableAssetGroup> Groups(AddressableAssetSettings settings) => settings == null
		? new List<AddressableAssetGroup>()
		: settings.groups.Where(group => EligibleGroup(settings, group)).ToList();

	public static List<string> Preflight(
		AddressRegistrationDraft request,
		AddressableAssetSettings settings,
		string assetGuid = null,
		bool requireImplementation = true)
	{
		if (request == null || !request.Enabled)
		{
			return new List<string>();
		}

		var errors = ValidateRequest(settings, request.Group, request.Address, request.Labels, assetGuid).Errors.ToList();
		if (requireImplementation && !ImplementationReady)
		{
			errors.Add(NotImplementedMessage);
		}

		return errors;
	}

	/// <summary>
	/// Shared UI/core checks. A null GUID means a new, not-yet-created UI asset; no existing GUID is exempted.
	/// Core callers must pass requireAssetGuid:true. GUID/path/GameObject identity remains in RegisterVerifiedPrefab.
	/// No dirtying, Entry mutation, persistence or success status is performed here.
	/// </summary>
	public static RegistrationRequestValidation ValidateRequest(
		AddressableAssetSettings settings,
		AddressableAssetGroup targetGroup,
		string address,
		IReadOnlyCollection<string> requestedLabels,
		string assetGuid = null,
		bool requireAssetGuid = false)
	{
		var errors = new List<string>();
		AddressableAssetEntry existingEntry = null;
		if (requireAssetGuid && string.IsNullOrWhiteSpace(assetGuid))
		{
			errors.Add("An asset GUID is required for core registration.");
		}

		if (string.IsNullOrWhiteSpace(address) ||
			address != address.Trim() ||
			address.Any(char.IsControl) ||
			address.IndexOfAny(new[] { '[', ']' }) >= 0)
		{
			errors.Add("Address must be nonempty, have no surrounding whitespace, control characters or brackets.");
		}

		if (requestedLabels == null)
		{
			errors.Add("Requested Labels cannot be null; use an empty collection for no additions.");
		}

		if (settings == null)
		{
			errors.Add("Addressables Settings is missing. Disable registration to create a Prefab only.");
			return new RegistrationRequestValidation(errors, null);
		}

		errors.AddRange(GroupErrors(settings, targetGroup));
		var defined = settings.GetLabels();
		foreach (var label in requestedLabels ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(label) || !defined.Contains(label))
			{
				errors.Add($"Label '{label}' is not defined in these Settings. Select an existing Label.");
			}
		}

		// Read actual direct membership, never FindAssetEntry's GUID-only cache (which can contain implicit Entries).
		var direct = new List<AddressableAssetEntry>();
		foreach (var group in settings.groups)
		{
			if (group == null)
			{
				continue;
			}

			foreach (var entry in group.entries)
			{
				if (entry == null)
				{
					errors.Add($"Group '{group.Name}' contains a null Entry.");
					continue;
				}

				direct.Add(entry);
				if (entry.parentGroup != group || entry.ParentEntry != null || entry.IsSubAsset)
				{
					errors.Add($"Direct Entry {entry.guid} in Group '{group.Name}' has inconsistent ownership or implicit identity.");
				}

				if (entry.address == address && (string.IsNullOrEmpty(assetGuid) || entry.guid != assetGuid))
				{
					errors.Add($"Address '{address}' conflicts with GUID {entry.guid} in Group '{entry.parentGroup?.Name}'.");
				}

				if (!string.IsNullOrEmpty(assetGuid) && entry.guid == assetGuid)
				{
					if (existingEntry != null)
					{
						errors.Add($"GUID {assetGuid} has multiple direct Entries; identity is ambiguous.");
					}

					existingEntry = entry;
					if (entry.ReadOnly)
					{
						errors.Add("Existing Entry is read-only.");
					}

					if (entry.parentGroup != targetGroup)
					{
						errors.Add($"GUID {assetGuid} is already in Group '{entry.parentGroup?.Name}'; it will not be moved.");
					}

					if (entry.address != address)
					{
						errors.Add($"GUID {assetGuid} already uses Address '{entry.address}'; it will not be renamed.");
					}
				}
			}
		}

		var directGuids = new HashSet<string>(direct.Select(entry => entry.guid), StringComparer.Ordinal);
		foreach (var folder in direct.Where(entry => AssetDatabase.IsValidFolder(entry.AssetPath)))
		{
			try
			{
				// 2.8.1 public path enumeration excludes explicit children/subtrees. It does not create Entries.
				foreach (var path in AddressablesFileEnumeration.EnumerateAddressableFolder(folder.AssetPath, settings, true))
				{
					var guid = AssetDatabase.AssetPathToGUID(path);
					if (string.IsNullOrEmpty(guid) || !path.StartsWith(folder.AssetPath + "/", StringComparison.Ordinal))
					{
						errors.Add($"Folder '{folder.AssetPath}' returned an unresolved or out-of-folder asset '{path}'.");
						continue;
					}

					if (directGuids.Contains(guid))
					{
						continue;
					}

					// Matches AddressableAssetEntry.GatherFolderEntries / GetRelativePath, without synthetic Entries.
					var implicitAddress = folder.address + path.Substring(folder.AssetPath.Length);
					if (implicitAddress == address && (string.IsNullOrEmpty(assetGuid) || guid != assetGuid))
					{
						errors.Add($"Address '{address}' conflicts with implicit GUID {guid} under Folder '{folder.AssetPath}'.");
					}

					if (!string.IsNullOrEmpty(assetGuid) && guid == assetGuid)
					{
						errors.Add($"GUID {assetGuid} is implicitly registered under Folder '{folder.AssetPath}' (read-only); automatic promotion is not supported.");
					}
				}
			}
			catch (Exception ex)
			{
				errors.Add($"Cannot safely inspect Folder '{folder.AssetPath}' ({ex.GetType().Name}): {ex.Message}");
			}
		}

		return new RegistrationRequestValidation(errors, existingEntry);
	}

	/// <summary>
	/// GUID must identify the already verified output, never the Template or Model.
	/// Only generated Variant paths are accepted. The creation flow additionally verifies base/model/reference identity.
	/// Save only a clean owned Group, reacquire its direct Entry, then independently read its serialized file.
	/// Never unload the live Settings/Group. Failures preserve the Prefab and may leave partial Entry changes.
	/// </summary>
	public static AddressRegistrationResult RegisterVerifiedPrefab(
		AddressableAssetSettings settings,
		string prefabAssetGuid,
		AddressableAssetGroup targetGroup,
		string address,
		IReadOnlyCollection<string> requestedLabels)
	{
		string assetPath = null;
		string stage = "validate identity";
		bool mutationStarted = false;

		try
		{
			assetPath = AssetDatabase.GUIDToAssetPath(prefabAssetGuid);
			if (string.IsNullOrEmpty(assetPath))
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Guid can't be found",
					prefabAssetGuid);
			}

			var actualGuid = AssetDatabase.AssetPathToGUID(assetPath);
			if (actualGuid != prefabAssetGuid)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Guid and Path aren't matched",
					actualGuid,
					assetPath);
			}

			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			if (prefab == null)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Target asset is not a GameObject",
					prefabAssetGuid,
					assetPath);
			}

			var parts = assetPath.Split('/');
			int contentType;
			bool isGeneratedVariant =
				parts.Length == 5 &&
				parts[0] == "Assets" &&
				parts[1] == "Game" &&
				parts[2] == "GeneratedContent" &&
				parts[3].StartsWith("Type_", StringComparison.Ordinal) &&
				int.TryParse(parts[3].Substring(5), out contentType) &&
				parts[4].StartsWith("Content_", StringComparison.Ordinal) &&
				parts[4].EndsWith(".prefab", StringComparison.Ordinal) &&
				ContentVariantCreator.GetOutputPath(contentType, parts[4].Substring(8, parts[4].Length - 15)) == assetPath &&
				PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant;
			if (!isGeneratedVariant)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"GUID must identify a generated Prefab Variant at the Content Creator output path; Template, FBX and arbitrary GameObjects are not accepted.",
					prefabAssetGuid,
					assetPath);
			}

			var validation = ValidateRequest(
				settings,
				targetGroup,
				address,
				requestedLabels,
				prefabAssetGuid,
				requireAssetGuid: true);
			if (!validation.IsValid)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					string.Join("\n", validation.Errors),
					prefabAssetGuid,
					assetPath);
			}

			var existingEntry = validation.ExistingEntry;
			AddressableAssetEntry entry;

			var originalLabels = existingEntry != null ? new HashSet<string>(existingEntry.labels) : new HashSet<string>();
			var expectedLabels = new HashSet<string>(originalLabels);
			expectedLabels.UnionWith(requestedLabels);
			bool changed = existingEntry == null || !existingEntry.labels.SetEquals(expectedLabels);

			var groupPath = AssetDatabase.GetAssetPath(targetGroup);
			var settingsPath = AssetDatabase.GetAssetPath(settings);
			var expectedGroupGuid = targetGroup.Guid;
			var groupAssetGuid = AssetDatabase.AssetPathToGUID(groupPath);
			bool hasUnsupportedSchema = targetGroup.Schemas.Any(schema =>
				schema == null ||
				schema.Group != targetGroup ||
				(schema.GetType() != typeof(BundledAssetGroupSchema) &&
					schema.GetType() != typeof(ContentUpdateGroupSchema)));
			if (hasUnsupportedSchema)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Independent serialized verification supports Group-owned native Bundled / ContentUpdate schemas only; custom or mismatched schema callbacks cannot be safely loaded.",
					prefabAssetGuid,
					assetPath);
			}

			var bundle = targetGroup.GetSchema<BundledAssetGroupSchema>();
			bool hasInitializedSchema =
				!string.IsNullOrEmpty(settings.profileSettings.GetValueById(settings.activeProfileId, bundle.BuildPath.Id)) &&
				!string.IsNullOrEmpty(settings.profileSettings.GetValueById(settings.activeProfileId, bundle.LoadPath.Id)) &&
				bundle.AssetBundleProviderType.Value != null &&
				bundle.BundledAssetProviderType.Value != null;
			if (!hasInitializedSchema)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Initialize the native Group's Build / Load paths and provider types before serialized verification; the tool will not repair Schema assets.",
					prefabAssetGuid,
					assetPath);
			}

			bool hasPersistedAssets =
				settings.IsPersisted &&
				!string.IsNullOrEmpty(settingsPath) &&
				!string.IsNullOrEmpty(groupPath) &&
				File.Exists(settingsPath) &&
				File.Exists(groupPath) &&
				!string.IsNullOrEmpty(groupAssetGuid);
			if (!hasPersistedAssets)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Settings and target Group must be persisted assets.",
					prefabAssetGuid,
					assetPath);
			}

			stage = "verify saved Label definitions";
			var savedLabels = ReadSerializedLabels(settingsPath);
			if (!expectedLabels.IsSubsetOf(savedLabels))
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Requested / inherited Labels are not all defined in the saved Settings. Save their definitions in the native Groups window first; this operation will not save Settings.",
					prefabAssetGuid,
					assetPath);
			}

			if (changed)
			{
				// Only this asset will be saved. Do not flush any pre-existing Group edits, even on an owned GUID.
				bool canWriteGroup =
					!EditorUtility.IsDirty(targetGroup) &&
					AssetDatabase.IsOpenForEdit(targetGroup) &&
					!new FileInfo(groupPath).IsReadOnly;
				if (!canWriteGroup)
				{
					return new AddressRegistrationResult(
						AddressRegistrationStatus.Rejected,
						"Target Group has unsaved changes or is not writable. Save or discard them before registration.",
						prefabAssetGuid,
						assetPath);
				}

				stage = "revalidate immediately before mutation";
				var finalValidation = ValidateRequest(
					settings,
					targetGroup,
					address,
					requestedLabels,
					prefabAssetGuid,
					true);
				if (!finalValidation.IsValid || !ReferenceEquals(existingEntry, finalValidation.ExistingEntry))
				{
					return new AddressRegistrationResult(
						AddressRegistrationStatus.Rejected,
						"Registration state changed before writing: " + string.Join("\n", finalValidation.Errors),
						prefabAssetGuid,
						assetPath);
				}

				Undo.RecordObject(targetGroup, "Register content Variant");
				mutationStarted = true;
				stage = "modify target Group Entry";

				if (existingEntry != null)
				{
					entry = existingEntry;
					foreach (var label in requestedLabels)
					{
						if (!entry.labels.Contains(label))
						{
							entry.SetLabel(label, true, force: false);
						}
					}
				}
				else
				{
					entry = settings.CreateOrMoveEntry(prefabAssetGuid, targetGroup);
					if (entry == null || entry.parentGroup != targetGroup)
					{
						throw new InvalidOperationException("CreateOrMoveEntry did not return the requested direct Entry.");
					}

					entry.SetAddress(address);
					foreach (var label in requestedLabels)
					{
						entry.SetLabel(label, true, force: false);
					}
				}

				stage = "save target Group";
				PersistenceFaultForChecks?.Invoke(stage);
				AssetDatabase.SaveAssetIfDirty(targetGroup);
				if (EditorUtility.IsDirty(targetGroup))
				{
					throw new IOException("Target Group is still dirty after targeted save.");
				}

				stage = "reimport saved Group";
				AssetDatabase.ImportAsset(groupPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
			}

			stage = "verify reacquired direct Entry";
			var reloadedGroup = AssetDatabase.LoadAssetAtPath<AddressableAssetGroup>(groupPath);
			bool hasExpectedIdentity =
				reloadedGroup != null &&
				reloadedGroup.Guid == expectedGroupGuid &&
				AssetDatabase.AssetPathToGUID(groupPath) == groupAssetGuid &&
				AssetDatabase.GUIDToAssetPath(prefabAssetGuid) == assetPath;
			if (!hasExpectedIdentity)
			{
				throw new InvalidOperationException("Reloaded asset / Group identity differs from the requested identity.");
			}

			VerifyEntry(reloadedGroup, prefabAssetGuid, expectedGroupGuid, address, expectedLabels);

			stage = "verify serialized Group file independently";
			PersistenceFaultForChecks?.Invoke(stage);
			// AssetDatabase can return the same cached Group. Load a detached serialized copy to inspect disk,
			// then destroy only the detached copy. Never unload or replace any live Settings / Group asset.
			var actual = ReadSerializedEntry(groupPath, prefabAssetGuid, expectedGroupGuid, address, expectedLabels);
			return new AddressRegistrationResult(
				changed ? AddressRegistrationStatus.Registered : AddressRegistrationStatus.AlreadyRegistered,
				changed
					? "Registered; reacquired direct Entry and independent serialized Group data verified."
					: "Already registered; serialized Group data verified. No assets saved or reimported.",
				actual.guid,
				AssetDatabase.GUIDToAssetPath(actual.guid),
				actual.parentGroupGuid,
				actual.address,
				actual.labels);
		}
		catch (Exception ex)
		{
			return new AddressRegistrationResult(
				AddressRegistrationStatus.Failed,
				$"Could not {stage} for GUID {prefabAssetGuid} at '{assetPath}' ({ex.GetType().Name}): {ex.Message}. " +
					(mutationStarted
						? "Partial Entry changes may remain in memory or on disk; no rollback was attempted. "
						: "No registration writes were started. ") +
					"Prefab retained; inspect the Group before retrying.",
				prefabAssetGuid,
				assetPath);
		}
	}

	private static AddressableAssetEntry VerifyEntry(
		AddressableAssetGroup group,
		string guid,
		string groupGuid,
		string address,
		HashSet<string> labels)
	{
		var matches = group.entries.Where(value => value.guid == guid).ToArray();
		if (matches.Length != 1)
		{
			throw new InvalidOperationException($"Expected one direct Entry for {guid}; actual count={matches.Length}.");
		}

		var actual = matches[0];
		bool hasExpectedEntry =
			actual.ParentEntry == null &&
			!actual.IsSubAsset &&
			actual.parentGroup == group &&
			actual.parentGroup.Guid == groupGuid &&
			actual.address == address &&
			actual.labels.SetEquals(labels);
		if (!hasExpectedEntry)
		{
			throw new InvalidOperationException(
				$"Entry mismatch. Expected GUID={guid}, Group={groupGuid}, Address='{address}', Labels=[{string.Join(",", labels)}]; " +
				$"Actual GUID={actual.guid}, Group={actual.parentGroup?.Guid}, Address='{actual.address}', Labels=[{string.Join(",", actual.labels)}].");
		}

		return actual;
	}

	private static (string guid, string parentGroupGuid, string address, string[] labels) ReadSerializedEntry(
		string path,
		string guid,
		string groupGuid,
		string address,
		HashSet<string> labels)
	{
		var objects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(path);
		try
		{
			var copies = objects.OfType<AddressableAssetGroup>().ToArray();
			if (copies.Length != 1 || objects.Any(EditorUtility.IsPersistent))
			{
				throw new InvalidOperationException("Independent serialized-file load did not return one detached Group.");
			}

			if (copies[0].Guid != groupGuid)
			{
				throw new InvalidOperationException("Serialized Group GUID mismatch.");
			}

			var entry = VerifyEntry(copies[0], guid, groupGuid, address, labels);
			return (
				entry.guid,
				entry.parentGroup.Guid,
				entry.address,
				entry.labels.OrderBy(value => value, StringComparer.Ordinal).ToArray());
		}
		finally
		{
			foreach (var value in objects)
			{
				if (value != null && !EditorUtility.IsPersistent(value))
				{
					UnityEngine.Object.DestroyImmediate(value);
				}
			}
		}
	}

	private static HashSet<string> ReadSerializedLabels(string path)
	{
		var objects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(path);
		try
		{
			var copies = objects.OfType<AddressableAssetSettings>().ToArray();
			if (copies.Length != 1 || objects.Any(EditorUtility.IsPersistent))
			{
				throw new InvalidOperationException("Saved Label definitions could not be read from a detached Settings copy.");
			}

			return new HashSet<string>(copies[0].GetLabels(), StringComparer.Ordinal);
		}
		finally
		{
			foreach (var value in objects)
			{
				if (value != null && !EditorUtility.IsPersistent(value))
				{
					UnityEngine.Object.DestroyImmediate(value);
				}
			}
		}
	}
}
