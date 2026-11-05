using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.UIElements;

/// <summary>Draft-only controls; never binds to Addressables assets or mutates their entries.</summary>
public sealed class ContentRegistrationView : VisualElement
{
	private readonly ContentCreationDraft draft;
	private readonly EditorWindow owner;
	private readonly Action changed;
	private readonly Func<AddressableAssetSettings> getSettings;
	private readonly Toggle enabled;
	private readonly Foldout details;
	private readonly DropdownField group;
	private readonly TextField address;
	private readonly Label suggestion;
	private readonly VisualElement labelRows;
	private readonly VisualElement preview;
	private readonly TextField search;
	private List<AddressableAssetGroup> groups = new();
	public AddressableAssetSettings Settings { get; private set; }

	public ContentRegistrationView(
		ContentCreationDraft draft,
		EditorWindow owner,
		Action changed,
		string searchText,
		Action<string> searchChanged,
		Func<AddressableAssetSettings> getSettings = null)
	{
		this.draft = draft;
		this.owner = owner;
		this.changed = changed;
		this.getSettings = getSettings ?? ContentAddressRegistration.ExistingSettings;
		draft.Registration ??= new AddressRegistrationDraft();
		name = "addressablesRegistration";
		Add(new Label("Addressables Registration"));
		enabled = new Toggle("Register with Addressables")
		{
			name = "registerAddressables",
			value = draft.Registration.Enabled
		};
		enabled.RegisterValueChangedCallback(evt => Edit(() =>
		{
			draft.Registration.Enabled = evt.newValue;
			if (evt.newValue && !draft.Registration.GroupInitialized)
			{
				var settings = this.getSettings();
				if (ContentAddressRegistration.EligibleGroup(settings, settings?.DefaultGroup))
				{
					draft.Registration.Group = settings.DefaultGroup;
				}

				draft.Registration.GroupInitialized = true;
			}
		}, true));
		Add(enabled);
		details = new Foldout
		{
			text = "Registration Settings / Preview",
			value = true,
			viewDataKey = "registrationDetails"
		};
		Add(details);
		group = new DropdownField("Group")
		{
			name = "registrationGroup"
		};
		group.RegisterValueChangedCallback(_ => Edit(() => draft.Registration.Group = group.index > 0 ? groups[group.index - 1] : null));
		details.Add(group);
		address = new TextField("Address")
		{
			name = "registrationAddress",
			isDelayed = false,
			value = draft.Registration.Address
		};
		address.RegisterValueChangedCallback(evt => Edit(() => draft.Registration.EditAddress(evt.newValue)));
		details.Add(address);
		suggestion = new Label();
		details.Add(suggestion);
		details.Add(new Button(() => Edit(() => draft.Registration.UseSuggested(draft.Id)))
		{
			name = "useSuggestedAddress",
			text = "Use Suggested Address"
		});
		search = new TextField("Search Labels")
		{
			name = "labelSearch",
			value = searchText ?? ""
		};
		search.RegisterValueChangedCallback(evt =>
		{
			searchChanged(evt.newValue);
			DrawLabels();
		});
		details.Add(search);
		labelRows = new VisualElement
		{
			name = "labelChoices"
		};
		details.Add(labelRows);
		details.Add(new Button(() =>
		{
			RefreshOptions();
			changed();
		})
		{
			text = "Refresh Groups / Labels"
		});
		details.Add(new Label("Manage Groups and Labels in Window > Asset Management > Addressables > Groups. This tool does not create them."));
		preview = new VisualElement
		{
			name = "registrationPreview"
		};
		details.Add(preview);
		RefreshOptions();
	}

	private void Edit(Action edit, bool refreshOptions = false)
	{
		Undo.RecordObject(owner, "Edit registration draft");
		edit();
		EditorUtility.SetDirty(owner);
		if (refreshOptions)
		{
			RefreshOptions();
		}

		changed();
	}

	public void RefreshOptions()
	{
		// Off must neither require Settings nor fetch them (the native getter can initialize/migrate).
		Settings = draft.Registration.Enabled ? getSettings() : null;
		groups = ContentAddressRegistration.Groups(Settings);
		var labels = new List<string>
		{
			"Select Group..."
		};
		labels.AddRange(groups.Select(item => item.Name + " [" + item.Guid + "]"));
		var selected = groups.IndexOf(draft.Registration.Group);
		if (draft.Registration.Group != null && selected < 0)
		{
			labels.Add("Unavailable Group: " + draft.Registration.Group.Name);
			groups.Add(draft.Registration.Group);
			selected = groups.Count - 1;
		}

		group.choices = labels;
		group.SetValueWithoutNotify(labels[selected + 1]);
		DrawLabels();
		RefreshPreview();
	}

	private void DrawLabels()
	{
		labelRows.Clear();
		var defined = Settings?.GetLabels() ?? new List<string>();
		var selected = draft.Registration.Labels;
		foreach (string label in defined.Concat(selected).Distinct().OrderBy(item => item, StringComparer.Ordinal))
		{
			if (label.IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}

			var toggle = new Toggle(defined.Contains(label) ? label : label + " (missing)")
			{
				value = selected.Contains(label)
			};
			toggle.RegisterValueChangedCallback(evt => Edit(() =>
			{
				if (evt.newValue)
				{
					if (!selected.Contains(label))
					{
						selected.Add(label);
					}
				}
				else
				{
					selected.Remove(label);
				}
			}));
			labelRows.Add(toggle);
		}

		if (defined.Count == 0)
		{
			labelRows.Add(new Label("No existing Labels available."));
		}
	}

	public void RefreshPreview()
	{
		var request = draft.Registration;
		enabled.SetValueWithoutNotify(request.Enabled);
		details.SetEnabled(request.Enabled);
		if (request.Enabled && details.parent == null)
		{
			Add(details);
		}
		else if (!request.Enabled)
		{
			details.RemoveFromHierarchy();
		}

		address.SetValueWithoutNotify(request.Address);
		suggestion.text = $"Mode: {(request.ManualAddress ? "Manual (preserved)" : "Auto")} | Suggested: {AddressRegistrationDraft.Suggest(draft.Id)}";
		preview.Clear();
		if (!request.Enabled)
		{
			return;
		}

		preview.Add(new Label($"Group: {(request.Group == null ? "None" : request.Group.Name)}"));
		preview.Add(new Label($"Address: {request.Address}"));
		if (request.Address != AddressRegistrationDraft.Suggest(draft.Id))
		{
			preview.Add(new HelpBox(
				"Content Browser expects Content ID as Address; a custom Address may display AddressMismatch there.",
				HelpBoxMessageType.Info));
		}

		preview.Add(new Label("Labels to add (existing labels retained): " + string.Join(", ", request.Labels)));
		preview.Add(new Label("New Prefab GUID: available after Save / Reload verification."));
		var errors = ContentAddressRegistration.Preflight(request, Settings);
		foreach (var error in errors)
		{
			preview.Add(new HelpBox(error, HelpBoxMessageType.Error));
		}

		if (errors.Count == 0)
		{
			preview.Add(new HelpBox(
				"Preflight passed. Registration will be rechecked after Prefab verification.",
				HelpBoxMessageType.Info));
		}
	}
}
