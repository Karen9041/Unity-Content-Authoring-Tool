using System;
using System.Collections.Generic;
using System.Reflection;
using Codice.CM.SEIDInfo;
using NPOI.POIFS.FileSystem;
using NPOI.SS.Formula.Functions;
using PlasticGui.WorkspaceWindow.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public enum ReferenceValueOrigin
{
	None,
	Template,
	SourceModel,
	Asset
}

[Serializable]
public sealed class ReferenceDraftEntry
{
	public ContentPrefabReferences Target;
	// Nonempty only for the two existing fixed fields. Dynamic keys are not property paths.
	public string FixedProperty;
	public int TemplateIndex = -1;
	public string Key = "";
	public ContentReferenceValueType ValueType;
	public ReferenceValueOrigin Origin;
	public Object ObjectValue;
	public string StringValue = "";
	public GameObject SelectedSourceRoot;
	public ReferenceDraftEntry Copy() => (ReferenceDraftEntry)MemberwiseClone();
	public bool Same(ReferenceDraftEntry other) => other != null &&
		Target == other.Target &&
		FixedProperty == other.FixedProperty &&
		TemplateIndex == other.TemplateIndex &&
		Key == other.Key &&
		ValueType == other.ValueType &&
		Origin == other.Origin &&
		ObjectValue == other.ObjectValue &&
		StringValue == other.StringValue &&
		SelectedSourceRoot == other.SelectedSourceRoot;
}

[Serializable]
public sealed class ReferenceAuthoringDraft
{
	public GameObject Template;
	public string TemplateFingerprint;
	public List<ReferenceDraftEntry> Entries = new();
	public List<ReferenceDraftEntry> Baseline = new();
	public bool Inspected;
	public bool NeedsApply
	{
		get
		{
			if (Entries.Count != Baseline.Count)
			{
				return true;
			}

			for (int i = 0; i < Entries.Count; i++)
			{
				if (!Entries[i].Same(Baseline[i]))
				{
					return true;
				}
			}

			return false;
		}
	}
}

public static class ContentReferenceAuthoring
{
	// Apply preview regressions passed before enabling integration / persistence checks.
	public static bool ApplyImplementationReady => true;

	public const string ApplyNotImplemented = "Not Implemented: Reference ApplyEntry is a guided exercise. Reference edits cannot be saved yet.";
	public static Type ObjectType(ContentReferenceValueType type)
	{
		switch (type)
		{
			case ContentReferenceValueType.GameObject:
				return typeof(GameObject);
			case ContentReferenceValueType.Transform:
				return typeof(Transform);
			case ContentReferenceValueType.SkinnedMeshRenderer:
				return typeof(SkinnedMeshRenderer);
			case ContentReferenceValueType.Animator:
				return typeof(Animator);
			case ContentReferenceValueType.Texture:
				return typeof(Texture);
			case ContentReferenceValueType.Animation:
				return typeof(Animation);
			case ContentReferenceValueType.AnimationClip:
				return typeof(AnimationClip);
			default:
				return typeof(Object);
		}
	}

	public static bool Within(Object value, GameObject root)
	{
		var transform = value is GameObject go ? go.transform : (value as Component)?.transform;
		return root != null &&
			transform != null &&
			(transform == root.transform || transform.IsChildOf(root.transform));
	}

	public static ReferenceAuthoringDraft Inspect(GameObject template)
	{
		var result = new ReferenceAuthoringDraft
		{
			Template = template,
			Inspected = true
		};
		if (template == null)
		{
			return result;
		}

		result.TemplateFingerprint = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(template)).ToString();
		foreach (var target in template.GetComponentsInChildren<ContentPrefabReferences>(true))
		{
			using (var serialized = new SerializedObject(target))
			{
				AddFixed(result, target, serialized, "animator", ContentReferenceValueType.Animator);
				AddFixed(result, target, serialized, "mainRenderer", ContentReferenceValueType.SkinnedMeshRenderer);
				var list = serialized.FindProperty("entries");
				for (int i = 0; i < list.arraySize; i++)
				{
					var item = list.GetArrayElementAtIndex(i);
					var row = new ReferenceDraftEntry
					{
						Target = target,
						TemplateIndex = i,
						Key = item.FindPropertyRelative("Key").stringValue,
						ValueType = (ContentReferenceValueType)item.FindPropertyRelative("ValueType").enumValueIndex,
						ObjectValue = item.FindPropertyRelative("ObjectValue").objectReferenceValue,
						StringValue = item.FindPropertyRelative("StringValue").stringValue
					};
					row.Origin = Classify(row.ObjectValue, template);
					result.Entries.Add(row);
				}
			}
		}

		foreach (var row in result.Entries)
		{
			result.Baseline.Add(row.Copy());
		}

		return result;
	}

	private static void AddFixed(
		ReferenceAuthoringDraft draft,
		ContentPrefabReferences target,
		SerializedObject serialized,
		string path,
		ContentReferenceValueType type)
	{
		var value = serialized.FindProperty(path).objectReferenceValue;
		draft.Entries.Add(new ReferenceDraftEntry
		{
			Target = target,
			FixedProperty = path,
			Key = path,
			ValueType = type,
			ObjectValue = value,
			Origin = Classify(value, draft.Template)
		});
	}

	private static ReferenceValueOrigin Classify(Object value, GameObject template) => value == null
		? ReferenceValueOrigin.None
		: Within(value, template) ? ReferenceValueOrigin.Template : ReferenceValueOrigin.Asset;
	public static List<Object> Candidates(GameObject source, ContentReferenceValueType type)
	{
		var values = new List<Object>();
		if (source == null || type == ContentReferenceValueType.String)
		{
			return values;
		}

		var expected = ObjectType(type);
		foreach (var transform in source.GetComponentsInChildren<Transform>(true))
		{
			if (expected.IsInstanceOfType(transform.gameObject))
			{
				values.Add(transform.gameObject);
			}

			foreach (var component in transform.GetComponents<Component>())
			{
				if (component != null && expected.IsInstanceOfType(component))
				{
					values.Add(component);
				}
			}
		}

		return values;
	}

	// Labels only: identity is the selected Unity Object, never this path or a name search.
	public static string Describe(Object value)
	{
		if (value == null)
		{
			return "None";
		}

		var transform = value is GameObject go ? go.transform : (value as Component)?.transform;
		if (transform == null)
		{
			return $"{AssetDatabase.GetAssetPath(value)} :: {value.name} ({value.GetType().Name})";
		}

		var segments = new List<string>();
		for (var current = transform; current != null; current = current.parent)
		{
			segments.Insert(0, $"{current.name}[{current.GetSiblingIndex()}]");
		}

		int ordinal = 0;
		if (value is Component component)
		{
			ordinal = Array.IndexOf(component.GetComponents(component.GetType()), component);
		}

		return $"{string.Join("/", segments)} :: {value.GetType().Name} #{ordinal}";
	}

	public static List<string> Validate(ContentCreationDraft draft)
	{
		var errors = new List<string>();
		var refs = draft.References;
		if (draft.BaseTemplatePrefab == null)
		{
			return errors;
		}

		if (refs == null || !refs.Inspected || refs.Template != draft.BaseTemplatePrefab)
		{
			errors.Add("Reference contract has not been inspected for this Template. Inspect / Reset References.");
			return errors;
		}

		if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(refs.Template)).ToString() != refs.TemplateFingerprint)
		{
			errors.Add("Template changed since inspection. Inspect / Reset References before creating.");
		}

		if (refs.Baseline.Count == 0)
		{
			errors.Add("Template has no ContentPrefabReferences contract. Add the public component to your Template explicitly; the tool does not add scripts.");
		}

		var keys = new Dictionary<ContentPrefabReferences, HashSet<string>>();
		foreach (var row in refs.Entries)
		{
			if (row == null)
			{
				errors.Add("Reference Draft contains a null Entry.");
				continue;
			}

			var issue = ValidateEntry(draft, row);
			if (issue != null)
			{
				errors.Add($"[{row.Key}] {issue}");
			}

			if (row.Target == null)
			{
				continue;
			}

			if (!keys.TryGetValue(row.Target, out var names))
			{
				keys[row.Target] = names = new HashSet<string>(StringComparer.Ordinal);
			}

			if (!names.Add(row.Key ?? ""))
			{
				errors.Add($"Duplicate Reference Key '{row.Key}' in {Describe(row.Target)} (includes fixed field keys).");
			}
		}

		// Fixed fields cannot be removed or retargeted through the dynamic list API.
		foreach (var original in refs.Baseline)
		{
			if (!string.IsNullOrEmpty(original.FixedProperty) &&
				!refs.Entries.Exists(row => row != null && row.Target == original.Target && row.FixedProperty == original.FixedProperty))
			{
				errors.Add($"Fixed contract field '{original.FixedProperty}' is missing from Draft.");
			}
		}

		return errors;
	}

	public static string ValidateEntry(ContentCreationDraft draft, ReferenceDraftEntry row)
	{
		if (row == null || row.Target == null || !Within(row.Target, draft.BaseTemplatePrefab))
		{
			return "Target contract is missing or outside Template.";
		}

		if (string.IsNullOrWhiteSpace(row.Key) || row.Key != row.Key.Trim())
		{
			return "Key must be nonblank without leading/trailing whitespace.";
		}

		if (!Enum.IsDefined(typeof(ContentReferenceValueType), row.ValueType))
		{
			return "Unknown Value Type.";
		}

		if (!string.IsNullOrEmpty(row.FixedProperty) &&
			!((row.FixedProperty == "animator" &&
			row.Key == "animator" &&
			row.ValueType == ContentReferenceValueType.Animator) ||
			(row.FixedProperty == "mainRenderer" &&
			row.Key == "mainRenderer" &&
			row.ValueType == ContentReferenceValueType.SkinnedMeshRenderer)))
		{
			return "Fixed field contract cannot change its key/type/property path.";
		}

		if (row.ValueType == ContentReferenceValueType.String)
		{
			return row.ObjectValue != null ? "String uses StringValue, never ObjectValue." : null;
		}

		if (!string.IsNullOrEmpty(row.StringValue))
		{
			return "Object entry must not carry a StringValue.";
		}

		if (row.Origin == ReferenceValueOrigin.None)
		{
			return row.ObjectValue == null ? null : "None origin must have a null value.";
		}

		if (row.ObjectValue == null)
		{
			return "Selected object is missing. Reselect or explicitly choose None.";
		}

		if (!ObjectType(row.ValueType).IsInstanceOfType(row.ObjectValue))
		{
			return "Object does not match Value Type.";
		}

		switch (row.Origin)
		{
			case ReferenceValueOrigin.Template:
				return Within(row.ObjectValue, draft.BaseTemplatePrefab) ? null : "Object is outside Template hierarchy.";
			case ReferenceValueOrigin.SourceModel:
				return row.SelectedSourceRoot == draft.SourceModelPrefab &&
					Within(row.ObjectValue, draft.SourceModelPrefab)
					? null
					: "Source Model changed or selection is outside its hierarchy. Reselect explicitly.";
			case ReferenceValueOrigin.Asset:
				if (!EditorUtility.IsPersistent(row.ObjectValue))
				{
					return "External value must be a persistent Project asset.";
				}

				return row.ObjectValue is Component || row.ObjectValue is GameObject
					? "Hierarchy objects must use Template or SourceModel origin; external prefab components cannot be bound as assets."
					: null;
			default:
				return "Unknown origin.";
		}
	}

	public static string Preflight(ContentCreationDraft draft)
	{
		var issues = Validate(draft);
		if (issues.Count > 0)
		{
			return string.Join("\n", issues);
		}

		return draft.References.NeedsApply && !ApplyImplementationReady ? ApplyNotImplemented : null;
	}

	// Apply one validated Draft row to its exact temporary container and read back before returning.
	// Entry index addresses draft.References.Entries; target list slots are prepared by ApplyToInstance.
	// Throw with key + property path + expected identity on failure. Never return until read-back identity matches.
	public static void ApplyEntry(
		ContentCreationDraft draft,
		int entryIndex,
		GameObject templateInstance,
		GameObject nestedModelInstance)
	{
		var entry = draft.References.Entries[entryIndex];
		int dynamicIndex = 0;
		for (int i = 0; i < entryIndex; i++)
		{
			var previous = draft.References.Entries[i];
			if (previous.Target == entry.Target && string.IsNullOrEmpty(previous.FixedProperty))
			{
				dynamicIndex++;
			}
		}

		string propertyPath = !string.IsNullOrEmpty(entry.FixedProperty) ? entry.FixedProperty : $"entries.Array.data[{dynamicIndex}]";
		string actualIdentity = "not written";
		try
		{
			var entryIssue = ValidateEntry(draft, entry);
			if (entryIssue != null)
			{
				throw new InvalidOperationException(entryIssue);
			}

			var targetContainer = FindInstanceTarget(entry.Target, templateInstance, draft.BaseTemplatePrefab);
			UnityEngine.Object resolvedValue = null;
			// String has no Object resolver, even if an old Draft retained a hierarchy Origin.
			if (entry.ValueType != ContentReferenceValueType.String)
			{
				switch (entry.Origin)
				{
					case ReferenceValueOrigin.None:
						resolvedValue = null;
						break;
					case ReferenceValueOrigin.Asset:
						resolvedValue = entry.ObjectValue;
						break;
					case ReferenceValueOrigin.Template:
						resolvedValue = ResolveInstanceObject(entry, draft.BaseTemplatePrefab, templateInstance);
						break;
					case ReferenceValueOrigin.SourceModel:
						resolvedValue = ResolveInstanceObject(entry, draft.SourceModelPrefab, nestedModelInstance);
						break;
					default:
						throw new InvalidOperationException("Unknown Reference Origin.");
				}
			}

			using (var serialized = new SerializedObject(targetContainer))
			{
				serialized.Update();
				if (!string.IsNullOrEmpty(entry.FixedProperty))
				{
					var property = serialized.FindProperty(entry.FixedProperty);
					if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
					{
						throw new InvalidOperationException($"Fixed property not found: {entry.FixedProperty}");
					}

					property.objectReferenceValue = resolvedValue;
				}
				else
				{
					var list = serialized.FindProperty("entries");
					if (list == null || dynamicIndex >= list.arraySize)
					{
						throw new InvalidOperationException($"Dynamic entry slot missing: {entry.Key}, index={dynamicIndex}");
					}

					var item = list.GetArrayElementAtIndex(dynamicIndex);
					propertyPath = item.propertyPath;
					item.FindPropertyRelative("Key").stringValue = entry.Key;
					item.FindPropertyRelative("ValueType").enumValueIndex = (int)entry.ValueType;
					if (entry.ValueType == ContentReferenceValueType.String)
					{
						item.FindPropertyRelative("StringValue").stringValue = entry.StringValue ?? "";
						item.FindPropertyRelative("ObjectValue").objectReferenceValue = null;
					}
					else
					{
						item.FindPropertyRelative("StringValue").stringValue = "";
						item.FindPropertyRelative("ObjectValue").objectReferenceValue = resolvedValue;
					}
				}

				serialized.ApplyModifiedProperties();
				serialized.Update();
				var written = serialized.FindProperty(propertyPath);
				if (!string.IsNullOrEmpty(entry.FixedProperty))
				{
					actualIdentity = Describe(written.objectReferenceValue);
					if (written.objectReferenceValue != resolvedValue)
					{
						throw new InvalidOperationException("Fixed field read-back identity mismatch.");
					}
				}
				else
				{
					var actualObject = written.FindPropertyRelative("ObjectValue").objectReferenceValue;
					var actualString = written.FindPropertyRelative("StringValue").stringValue;
					actualIdentity = $"{Describe(actualObject)} / String='{actualString}'";
					if (written.FindPropertyRelative("Key").stringValue != entry.Key ||
						written.FindPropertyRelative("ValueType").enumValueIndex != (int)entry.ValueType ||
						actualObject != resolvedValue ||
						actualString != (entry.ValueType == ContentReferenceValueType.String ? entry.StringValue ?? "" : ""))
					{
						throw new InvalidOperationException("Dynamic entry read-back mismatch.");
					}
				}
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				$"Reference Apply failed: Key='{entry.Key}', Origin={entry.Origin}, " +
					$"Property='{propertyPath}', Expected={Describe(entry.ObjectValue)} / String='{entry.StringValue}', " +
					$"Actual={actualIdentity}. {ex.Message}",
				ex);
		}
	}

	private static Object ResolveInstanceObject(
		ReferenceDraftEntry entry,
		Object sourceAsset,
		GameObject root)
	{
		var candidates = new List<UnityEngine.Object>();
		var sourcePath = AssetDatabase.GetAssetPath(sourceAsset);
		var expectedType = entry.ValueType == ContentReferenceValueType.Object && entry.ObjectValue != null
			? entry.ObjectValue.GetType()
			: ObjectType(entry.ValueType);
		Object resolvedValue = null;
		if (root == null || string.IsNullOrEmpty(sourcePath))
		{
			throw new InvalidOperationException($"Missing instance root or Prefab Asset Path for '{entry.Key}'.");
		}

		if (expectedType == typeof(GameObject))
		{
			foreach (var transform in root.GetComponentsInChildren<Transform>(true))
			{
				candidates.Add(transform.gameObject);
			}
		}
		else if (typeof(Component).IsAssignableFrom(expectedType))
		{
			foreach (var component in root.GetComponentsInChildren<Component>(true))
			{
				candidates.Add(component);
			}
		}
		else
		{
			throw new InvalidOperationException($"Unsupported hierarchy reference type: {expectedType.Name}");
		}

		foreach (var candidate in candidates)
		{
			if (candidate == null)
			{
				continue;
			}

			if (!expectedType.IsInstanceOfType(candidate))
			{
				continue;
			}

			var source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(candidate, sourcePath);
			if (source == entry.ObjectValue)
			{
				if (resolvedValue != null)
				{
					throw new InvalidOperationException($"Ambiguous reference: {entry.Key}, SourcePath='{sourcePath}', " +
						$"Matches={Describe(resolvedValue)} AND {Describe(candidate)}");
				}

				resolvedValue = candidate;
			}
		}

		if (resolvedValue == null)
		{
			throw new InvalidOperationException($"Instance reference not found: {entry.Key}, SourcePath='{sourcePath}', " +
				$"Root={Describe(root)}, Candidates={candidates.Count}");
		}

		return resolvedValue;
	}

	public static void ApplyToInstance(
		ContentCreationDraft draft,
		GameObject templateInstance,
		GameObject nestedModelInstance)
	{
		var issue = Preflight(draft);
		if (issue != null)
		{
			throw new InvalidOperationException(issue);
		}

		if (!draft.References.NeedsApply)
		{
			return; // Keep Unity's inherited references untouched.
		}

		foreach (var source in CollectTargets(draft.References))
		{
			var target = FindInstanceTarget(source, templateInstance, draft.BaseTemplatePrefab);
			using (var serialized = new SerializedObject(target))
			{
				var count = draft.References.Entries.FindAll(row => row.Target == source && string.IsNullOrEmpty(row.FixedProperty)).Count;
				serialized.FindProperty("entries").arraySize = count;
				serialized.ApplyModifiedProperties();
			// SerializedProperty already records the prefab override.
			}
		}

		for (int i = 0; i < draft.References.Entries.Count; i++)
		{
			ApplyEntry(draft, i, templateInstance, nestedModelInstance);
		}

		var errors = Verify(draft, templateInstance, nestedModelInstance);
		if (errors.Count > 0)
		{
			throw new InvalidOperationException("Reference read-back failed: " + string.Join("\n", errors));
		}
	}

	public static List<ContentPrefabReferences> CollectTargets(ReferenceAuthoringDraft draft)
	{
		var result = new List<ContentPrefabReferences>();
		foreach (var row in draft.Baseline)
		{
			if (row.Target != null && !result.Contains(row.Target))
			{
				result.Add(row.Target);
			}
		}

		return result;
	}

	public static ContentPrefabReferences FindInstanceTarget(
		ContentPrefabReferences source,
		GameObject instance,
		GameObject template)
	{
		ContentPrefabReferences match = null;
		foreach (var target in instance.GetComponentsInChildren<ContentPrefabReferences>(true))
		{
			if (PrefabUtility.GetCorrespondingObjectFromSourceAtPath(target, AssetDatabase.GetAssetPath(template)) == source)
			{
				if (match != null)
				{
					throw new InvalidOperationException("Ambiguous target contract identity: " + Describe(source));
				}

				match = target;
			}
		}

		return match != null ? match : throw new InvalidOperationException("Target contract missing: " + Describe(source));
	}

	// Independent verifier: compares source identity rather than names, both pre-save and on reloaded assets.
	public static List<string> Verify(
		ContentCreationDraft draft,
		GameObject variant,
		GameObject nestedModel)
	{
		var errors = new List<string>();
		foreach (var source in CollectTargets(draft.References))
		{
			var target = FindInstanceTarget(source, variant, draft.BaseTemplatePrefab);
			using (var serialized = new SerializedObject(target))
			{
				var list = serialized.FindProperty("entries");
				var dynamicRows = draft.References.Entries.FindAll(row => row.Target == source && string.IsNullOrEmpty(row.FixedProperty));
				if (list.arraySize != dynamicRows.Count)
				{
					errors.Add($"{Describe(source)}: entry count mismatch.");
					continue;
				}

				int index = 0;
				foreach (var row in draft.References.Entries.FindAll(row => row.Target == source))
				{
					SerializedProperty property;
					if (!string.IsNullOrEmpty(row.FixedProperty))
					{
						property = serialized.FindProperty(row.FixedProperty);
					}
					else
					{
						var item = list.GetArrayElementAtIndex(index++);
						if (item.FindPropertyRelative("Key").stringValue != row.Key ||
							item.FindPropertyRelative("ValueType").enumValueIndex != (int)row.ValueType)
						{
							errors.Add($"[{row.Key}] key/type mismatch.");
						}

						if (row.ValueType == ContentReferenceValueType.String)
						{
							if (item.FindPropertyRelative("StringValue").stringValue != (row.StringValue ?? "") ||
								item.FindPropertyRelative("ObjectValue").objectReferenceValue != null)
							{
								errors.Add($"[{row.Key}] string value / object separation mismatch.");
							}

							continue;
						}

						if (item.FindPropertyRelative("StringValue").stringValue != "")
						{
							errors.Add($"[{row.Key}] object entry has string data.");
						}

						property = item.FindPropertyRelative("ObjectValue");
					}

					var actual = property.objectReferenceValue;
					bool valid;
					if (row.Origin == ReferenceValueOrigin.None)
					{
						valid = actual == null;
					}
					else if (row.Origin == ReferenceValueOrigin.Asset)
					{
						valid = actual == row.ObjectValue;
					}
					else
					{
						var root = row.Origin == ReferenceValueOrigin.SourceModel ? nestedModel : variant;
						var asset = row.Origin == ReferenceValueOrigin.SourceModel ? draft.SourceModelPrefab : draft.BaseTemplatePrefab;
						valid = actual != null &&
							Within(actual, root) &&
							PrefabUtility.GetCorrespondingObjectFromSourceAtPath(actual, AssetDatabase.GetAssetPath(asset)) == row.ObjectValue;
					}

					if (!valid)
					{
						errors.Add($"[{row.Key}] reference identity mismatch; expected {row.Origin}: {Describe(row.ObjectValue)}, actual: {Describe(actual)}.");
					}
				}
			}
		}

		return errors;
	}
}
