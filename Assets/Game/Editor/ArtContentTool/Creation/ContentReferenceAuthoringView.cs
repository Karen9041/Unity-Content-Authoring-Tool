using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Only edits serialized EditorWindow Draft state. Never binds fields to Template assets.</summary>
public sealed class ContentReferenceAuthoringView : VisualElement
{
	private readonly ContentCreationDraft draft;
	private readonly EditorWindow owner;
	private readonly Action changed;

	public ContentReferenceAuthoringView(
		ContentCreationDraft draft,
		EditorWindow owner,
		Action changed)
	{
		this.draft = draft;
		this.owner = owner;
		this.changed = changed;
		Refresh();
	}

	private void Edit(Action action, bool rebuild = true)
	{
		Undo.RecordObject(owner, "Edit content reference draft");
		action();
		EditorUtility.SetDirty(owner);
		changed();
		if (rebuild)
		{
			Refresh();
		}
	}

	public void Refresh()
	{
		Clear();
		Add(new Label("Reference Contract / Draft"));
		Add(new HelpBox(
			"Edit the Draft only. None explicitly clears a reference. Source Model selections use exact object identity; no automatic name matching.",
			HelpBoxMessageType.Info));
		Add(new Button(
			() => Edit(() => draft.References = ContentReferenceAuthoring.Inspect(draft.BaseTemplatePrefab)))
		{
			text = "Inspect / Reset References (discards reference edits)"
		});
		var refs = draft.References;
		if (refs == null || !refs.Inspected || refs.Template != draft.BaseTemplatePrefab)
		{
			Add(new Label("Select a Template and inspect its contract."));
			return;
		}

		Add(new Label(refs.NeedsApply ? "Draft overrides / additions / removals pending" : "Template values inherited unchanged"));
		if (refs.NeedsApply && !ContentReferenceAuthoring.ApplyImplementationReady)
		{
			Add(new HelpBox(ContentReferenceAuthoring.ApplyNotImplemented, HelpBoxMessageType.Warning));
		}

		foreach (var target in ContentReferenceAuthoring.CollectTargets(refs))
		{
			var section = new Foldout
			{
				text = ContentReferenceAuthoring.Describe(target),
				value = true
			};
			Add(section);
			section.Add(new Button(
				() => Edit(
					() => refs.Entries.Add(new ReferenceDraftEntry
					{
						Target = target,
						ValueType = ContentReferenceValueType.Object,
						Origin = ReferenceValueOrigin.None
					})))
			{
				text = "Add Entry"
			});
			foreach (var entry in refs.Entries.FindAll(row => row.Target == target))
			{
				DrawEntry(section, entry);
			}

			int removed =
				refs.Baseline.FindAll(row =>
					row.Target == target && string.IsNullOrEmpty(row.FixedProperty)).Count -
				refs.Entries.FindAll(row =>
					row.Target == target && string.IsNullOrEmpty(row.FixedProperty)).Count;
			if (removed > 0)
			{
				section.Add(new Label($"Dynamic list has {removed} fewer entries than Template (override)."));
			}
		}
	}

	private void DrawEntry(VisualElement parent, ReferenceDraftEntry entry)
	{
		var row = new VisualElement();
		row.style.marginBottom = 10;
		parent.Add(row);
		bool fixedField = !string.IsNullOrEmpty(entry.FixedProperty);
		var original = draft.References.Baseline.Find(
			value => value.Target == entry.Target &&
				(fixedField
					? value.FixedProperty == entry.FixedProperty
					: entry.TemplateIndex >= 0 &&
						value.TemplateIndex == entry.TemplateIndex &&
						string.IsNullOrEmpty(value.FixedProperty)));
		row.Add(new Label(original == null ? "Added" : entry.Same(original) ? "Inherited" : "Override"));
		if (original != null)
		{
			row.Add(new Label($"Template: {original.Key} / {original.ValueType} / {original.Origin} / " +
				(original.ValueType == ContentReferenceValueType.String
					? original.StringValue
					: ContentReferenceAuthoring.Describe(original.ObjectValue))));
		}

		var key = new TextField("Entry Key")
		{
			value = entry.Key,
			isDelayed = true,
			isReadOnly = fixedField
		};
		key.RegisterValueChangedCallback(evt => Edit(() => entry.Key = evt.newValue));
		row.Add(key);
		var type = new EnumField("Value Type", entry.ValueType);
		type.SetEnabled(!fixedField);
		type.RegisterValueChangedCallback(evt => Edit(() =>
		{
			entry.ValueType = (ContentReferenceValueType)evt.newValue;
			entry.ObjectValue = null;
			entry.StringValue = "";
			entry.Origin = ReferenceValueOrigin.None;
		}));
		row.Add(type);
		if (entry.ValueType == ContentReferenceValueType.String)
		{
			var text = new TextField("String Value")
			{
				value = entry.StringValue,
				isDelayed = true
			};
			text.RegisterValueChangedCallback(evt => Edit(() => entry.StringValue = evt.newValue));
			row.Add(text);
			row.Add(new Label("Storage: entries[].Key / StringValue (stringValue), never objectReferenceValue"));
		}
		else
		{
			var origin = new EnumField("Value Source", entry.Origin);
			origin.RegisterValueChangedCallback(evt => Edit(() =>
			{
				entry.Origin = (ReferenceValueOrigin)evt.newValue;
				entry.ObjectValue = null;
				entry.SelectedSourceRoot = draft.SourceModelPrefab;
			}));
			row.Add(origin);
			if (entry.Origin == ReferenceValueOrigin.SourceModel)
			{
				var candidates = ContentReferenceAuthoring.Candidates(draft.SourceModelPrefab, entry.ValueType);
				var labels = new List<string>
				{
					"Select explicitly..."
				};
				foreach (var item in candidates)
				{
					labels.Add(ContentReferenceAuthoring.Describe(item));
				}

				int selected = candidates.IndexOf(entry.ObjectValue) + 1;
				var picker = new PopupField<string>("Model Object", labels, selected);
				picker.RegisterValueChangedCallback(evt => Edit(() =>
				{
					int index = picker.index - 1;
					entry.ObjectValue = index >= 0 ? candidates[index] : null;
					entry.SelectedSourceRoot = draft.SourceModelPrefab;
				}));
				row.Add(picker);
			}
			else if (entry.Origin != ReferenceValueOrigin.None)
			{
				var field = new ObjectField("Object Value")
				{
					objectType = ContentReferenceAuthoring.ObjectType(entry.ValueType),
					allowSceneObjects = false,
					value = entry.ObjectValue
				};
				field.RegisterValueChangedCallback(evt => Edit(() => entry.ObjectValue = evt.newValue));
				row.Add(field);
			}

			row.Add(new Label("Current: " + ContentReferenceAuthoring.Describe(entry.ObjectValue)));
			if (entry.Origin == ReferenceValueOrigin.Template)
			{
				var options = ContentReferenceAuthoring.Candidates(draft.BaseTemplatePrefab, entry.ValueType);
				var labels = new List<string>
				{
					"Select Template object explicitly..."
				};
				foreach (var value in options)
				{
					labels.Add(ContentReferenceAuthoring.Describe(value));
				}

				var picker = new PopupField<string>("Template Object", labels, options.IndexOf(entry.ObjectValue) + 1);
				picker.RegisterValueChangedCallback(evt => Edit(() => entry.ObjectValue = picker.index > 0 ? options[picker.index - 1] : null));
				row.Add(picker);
			}
		}

		string error = ContentReferenceAuthoring.ValidateEntry(draft, entry);
		row.Add(new HelpBox(error ?? "Valid", error == null ? HelpBoxMessageType.Info : HelpBoxMessageType.Error));
		if (!fixedField)
		{
			row.Add(new Button(() => Edit(() => draft.References.Entries.Remove(entry)))
			{
				text = "Remove Entry (Draft only)"
			});
		}
	}
}
