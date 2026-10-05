using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

public class PetEditorWindow : EditorWindow
{
    private List<PetData> pets = new();
    private PetData selectedPet;
    private VisualElement rightPane;
    private VisualElement validationContainer;
    private ListView listView;
    private readonly Dictionary<PetData, List<ValidationResult>> validationCache = new();
    private readonly Dictionary<string, List<PetData>> petsById = new();


    [MenuItem("Tools/Content Tools/Pet Editor")]
    public static void Open()
    {
        GetWindow<PetEditorWindow>("Pet Editor");
    }

    public void CreateGUI()
    {
        RefreshPetList();

        var splitView = new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal);
        var leftPane = new VisualElement();
        rightPane = new VisualElement();
        validationContainer = new VisualElement();

        listView = new ListView
        {
            itemsSource = pets,
            makeItem = () => new Label(),
            bindItem = (element, index) =>
            {
                var label = (Label)element;
                var pet = pets[index];

                if(!validationCache.TryGetValue(pet, out var results))
                {
                    results = new List<ValidationResult>();
                }

                if (results != null && results.Count > 0)
                {
                    var highestSeverity = ValidationSeverity.Warning;
                    foreach (var result in results)
                    {
                        if (result.Severity == ValidationSeverity.Error)
                        {
                            highestSeverity = ValidationSeverity.Error;
                            break;
                        }
                    }

                    label.style.color = highestSeverity == ValidationSeverity.Error ? Color.red : Color.yellow;
                }
                else
                {
                    label.style.color = Color.white;
                }
                
                label.text = pets[index].DisplayName;
            },
            selectionType = SelectionType.Single
        };

        listView.selectionChanged += selection =>
        {
            foreach(var item in selection)
            {
                if(item is not PetData pet)
                    continue;
                
                selectedPet = pet;
                ShowPet(selectedPet);
            }
        };

        leftPane.Add(new Label($"Pets: {pets.Count}"));
        leftPane.Add(listView);

        splitView.Add(leftPane);
        splitView.Add(rightPane);

        rootVisualElement.Add(splitView);
    }

    private void RefreshPetList()
    {
        pets.Clear();

        string[] guids = AssetDatabase.FindAssets("t:PetData");

        foreach(string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            PetData pet = AssetDatabase.LoadAssetAtPath<PetData>(path);

            if(pet != null)
            {
                pets.Add(pet);
            }
        }

        ValidateAllPets();
    }

    private void ShowPet(PetData pet)
    {
        rightPane.Clear();

        var serializedPet = new SerializedObject(pet);

        PropertyField idField = new PropertyField(serializedPet.FindProperty("id"));
        rightPane.Add(idField);
        
        PropertyField displayNameField = new PropertyField(serializedPet.FindProperty("displayName"));
        rightPane.Add(displayNameField);

        PropertyField iconField = new PropertyField(serializedPet.FindProperty("icon"));
        rightPane.Add(iconField);

        PropertyField prefabField = new PropertyField(serializedPet.FindProperty("prefab"));
        rightPane.Add(prefabField);
    
        rightPane.Bind(serializedPet);

        rightPane.Add(validationContainer);

        RefreshValidation(pet);

        idField.RegisterValueChangeCallback(evt =>
        {
            ValidateAllPets();
            listView.RefreshItems();
            RefreshValidation(pet);
        });

        displayNameField.RegisterValueChangeCallback(evt =>
        {
            int index = pets.IndexOf(pet);
            if(index >= 0)
            {
                listView.RefreshItem(index);
            }
        });

        iconField.RegisterValueChangeCallback(evt =>
        {
            ValidatePetAndUpdateCache(pet);
            int index = pets.IndexOf(pet);
            if(index >= 0)
            {
                listView.RefreshItem(index);
            }
            RefreshValidation(pet);
        });

        prefabField.RegisterValueChangeCallback(evt =>
        {
            ValidatePetAndUpdateCache(pet);
            int index = pets.IndexOf(pet);
            if(index >= 0)
            {
                listView.RefreshItem(index);
            }
            RefreshValidation(pet);
        });
    }

    private void BuildPetInIndex()
    {
        petsById.Clear();

        foreach(var pet in pets)
        {
            if(string.IsNullOrWhiteSpace(pet.Id))
                continue;

            if(!petsById.TryGetValue(pet.Id, out var sameIdPets))
            {
                sameIdPets = new List<PetData>();
                petsById.Add(pet.Id, sameIdPets);
            }

            sameIdPets.Add(pet);
        }
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnUndoRedo()
    {
        if(selectedPet == null)
            return;

        // Validation is derived state
        ValidateAllPets();
        listView.RefreshItems();
        RefreshValidation(selectedPet);
    }

#region Validation 

    private bool IsIdMissing(PetData selectedPet)
    {
        return string.IsNullOrWhiteSpace(selectedPet?.Id);
    }

    private bool IsPrefabMissing(PetData selectedPet)
    {
        return selectedPet?.Prefab == null;
    }

    private bool IsIconMissing(PetData selectedPet)
    {
        return selectedPet?.Icon == null;
    }

    private List<PetData> FindDuplicateIdPets(PetData selectedPet)
    {
        if(string.IsNullOrWhiteSpace(selectedPet?.Id))
            return new List<PetData>();

        if(!petsById.TryGetValue(selectedPet.Id, out var sameIdPets))
            return new List<PetData>();

        var duplicates = new List<PetData>();

        foreach(var pet in sameIdPets)
        {
            if(pet != selectedPet)
            {
                duplicates.Add(pet);
            }
        }

        return duplicates;
    }

    public enum ValidationSeverity
    {
        Warning,
        Error
    }

    public class ValidationResult
    {
        public ValidationSeverity Severity;
        public string Message;
    }

    public List<ValidationResult> ValidatePet(PetData selectedPet)
    {
        var results = new List<ValidationResult>();

        if(IsIdMissing(selectedPet))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "ID is required."
            });
        }

        if(IsPrefabMissing(selectedPet))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Prefab is required."
            });
        }

        if(IsIconMissing(selectedPet))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Warning,
                Message = "Icon is missing."
            });
        }

        var duplicatePets = FindDuplicateIdPets(selectedPet);

        foreach(var duplicate in duplicatePets)
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = $"ID '{selectedPet.Id}' is also used by '{duplicate.name}'."
            });
        }

        return results;
    }

    private void RefreshValidation(PetData pet)
    {
        validationContainer.Clear();
        var validationResults = ValidatePet(pet);
        foreach(var result in validationResults)
        {
            validationContainer.Add(
                new Label($"[{result.Severity}] {result.Message}")
            );
        }
    }

    private void ValidatePetAndUpdateCache(PetData pet)
    {
        validationCache[pet] = ValidatePet(pet);
    }

    private void ValidateAllPets()
    {
        validationCache.Clear();

        BuildPetInIndex();

        foreach(var pet in pets)
        {
            ValidatePetAndUpdateCache(pet);
        }
    }

#endregion

}

