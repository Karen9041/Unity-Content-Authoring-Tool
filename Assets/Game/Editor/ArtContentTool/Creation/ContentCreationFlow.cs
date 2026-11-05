using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>Frozen retry target. Serialized with the Window, independent of subsequent Draft edits.</summary>
[Serializable]
public sealed class ContentRegistrationReceipt
{
	public string AssetGuid;
	public string AssetPath;
	public string TemplateHash;
	public string ModelHash;
	public ContentCreationDraft Snapshot;
}

public sealed class ContentCreationOutcome
{
	public VariantCreationResult Variant;
	public AddressRegistrationResult Registration;
	public ContentRegistrationReceipt Receipt;
	public bool RegistrationRequested;
	public bool Succeeded => Variant?.Status == VariantCreationStatus.Succeeded &&
		(RegistrationRequested ? Registration?.Succeeded == true : Registration?.Status == AddressRegistrationStatus.NotRequested);
	public string Message => (Variant?.Message ?? "") +
		(RegistrationRequested && Variant?.Status == VariantCreationStatus.Succeeded && !Succeeded
		? "\nPrefab created and verified; Registration did not succeed. Prefab retained; retry registration only."
		: "") +
		(Registration == null ? "" : "\n" + Registration.Message);
}

/// <summary>Orchestration only. Prefab construction and Reference Apply remain in the existing Creator.</summary>
public static class ContentCreationFlow
{
	public static ContentCreationOutcome Create(ContentCreationDraft draft, AddressableAssetSettings settings)
	{
		var outcome = new ContentCreationOutcome();
		var registration = draft?.Registration;
		outcome.RegistrationRequested = registration?.Enabled == true;
		var errors = ContentAddressRegistration.Preflight(registration, settings);
		if (errors.Count != 0)
		{
			outcome.Registration = new AddressRegistrationResult(
				ContentAddressRegistration.ImplementationReady
					? AddressRegistrationStatus.Rejected
					: AddressRegistrationStatus.NotImplemented,
				string.Join("\n", errors));
			return outcome; // Must precede ALL Prefab creation, including folder creation.
		}

		ContentRegistrationReceipt receipt = null;
		if (registration != null && registration.Enabled)
		{
			var inputErrors = ContentVariantCreator.Validate(draft, out var unusedPath);
			if (inputErrors.Count != 0)
			{
				outcome.Variant = new VariantCreationResult(
					VariantCreationStatus.InvalidInput,
					string.Join("\n", inputErrors),
					unusedPath);
				return outcome;
			}

			receipt = new ContentRegistrationReceipt
			{
				Snapshot = JsonUtility.FromJson<ContentCreationDraft>(JsonUtility.ToJson(draft)),
				TemplateHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(draft.BaseTemplatePrefab)).ToString(),
				ModelHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(draft.SourceModelPrefab)).ToString()
			};
		}

		outcome.Variant = ContentVariantCreator.Create(draft);
		if (outcome.Variant.Status != VariantCreationStatus.Succeeded)
		{
			return outcome;
		}

		if (receipt == null)
		{
			outcome.Registration = new AddressRegistrationResult(
				AddressRegistrationStatus.NotRequested,
				"Addressables registration is off.");
			return outcome;
		}

		receipt.AssetPath = outcome.Variant.OutputPath;
		receipt.AssetGuid = AssetDatabase.AssetPathToGUID(receipt.AssetPath);
		outcome.Receipt = receipt;
		outcome.Registration = Retry(receipt, settings);
		return outcome;
	}

	public static AddressRegistrationResult Retry(ContentRegistrationReceipt receipt, AddressableAssetSettings settings)
	{
		string guid = receipt?.AssetGuid;
		try
		{
			if (receipt?.Snapshot == null ||
				string.IsNullOrEmpty(guid) ||
				string.IsNullOrEmpty(receipt.AssetPath) ||
				AssetDatabase.GUIDToAssetPath(guid) != receipt.AssetPath ||
				AssetDatabase.AssetPathToGUID(receipt.AssetPath) != guid)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Retry target GUID/path is missing or changed; no new Prefab will be created.",
					guid);
			}

			if (receipt.Snapshot.Registration?.Enabled != true)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Retry receipt has no enabled registration request.",
					guid);
			}

			if (receipt.AssetPath == AssetDatabase.GetAssetPath(receipt.Snapshot.BaseTemplatePrefab) ||
				receipt.AssetPath == AssetDatabase.GetAssetPath(receipt.Snapshot.SourceModelPrefab))
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Registration target cannot be the original Template or Model.",
					guid);
			}

			var errors = ContentAddressRegistration.Preflight(receipt.Snapshot.Registration, settings, guid);
			if (errors.Count != 0)
			{
				return new AddressRegistrationResult(
					ContentAddressRegistration.ImplementationReady
						? AddressRegistrationStatus.Rejected
						: AddressRegistrationStatus.NotImplemented,
					string.Join("\n", errors),
					guid);
			}

			var draft = receipt.Snapshot;
			var verified = ContentVariantCreator.VerifyCreatedVariant(
				receipt.AssetPath,
				draft.BaseTemplatePrefab,
				draft.SourceModelPrefab,
				Hash128.Parse(receipt.TemplateHash),
				Hash128.Parse(receipt.ModelHash));
			if (verified.Status != VariantCreationStatus.Succeeded)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Retry Variant verification failed: " + verified.Message,
					guid);
			}

			var nested = verified.Asset.transform.Cast<Transform>().Where(
				child => PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
					PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == draft.SourceModelPrefab).ToList();
			if (nested.Count != 1)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Retry Nested Model identity is ambiguous.",
					guid);
			}

			var issues = ContentReferenceAuthoring.Verify(draft, verified.Asset, nested[0].gameObject);
			if (issues.Count != 0)
			{
				return new AddressRegistrationResult(
					AddressRegistrationStatus.Rejected,
					"Retry Reference verification failed:\n" + string.Join("\n", issues),
					guid);
			}

			var request = draft.Registration;
			return ContentAddressRegistration.RegisterVerifiedPrefab(
				settings,
				guid,
				request.Group,
				request.Address,
				request.Labels);
		}
		catch (Exception ex)
		{
			return new AddressRegistrationResult(
				AddressRegistrationStatus.Failed,
				$"Registration failed for GUID {guid} ({ex.GetType().Name}): {ex.Message}. Prefab retained; retry registration only.",
				guid);
		}
	}
}
