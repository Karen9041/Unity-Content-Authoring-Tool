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

        var listView = new ListView
        {
            itemsSource = pets,
            makeItem = () => new Label(),
            bindItem = (element, index) =>
            {
                var label = (Label)element;
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

        Debug.Log($"Found {pets.Count} PetData assets.");
    }

    private void ShowPet(PetData pet)
    {
        rightPane.Clear();

        var serializedPet = new SerializedObject(pet);

        PropertyField idField = new PropertyField(serializedPet.FindProperty("id"));
        rightPane.Add(idField);
        
        rightPane.Add(
            new PropertyField(serializedPet.FindProperty("displayName"))
        );

        PropertyField iconField = new PropertyField(serializedPet.FindProperty("icon"));
        rightPane.Add(iconField);

        PropertyField prefabField = new PropertyField(serializedPet.FindProperty("prefab"));
        rightPane.Add(prefabField);
    
        rightPane.Bind(serializedPet);

        rightPane.Add(validationContainer);

        RefreshValidation(pet);

        idField.RegisterValueChangeCallback(evt =>
        {
            RefreshValidation(pet);
        });

        iconField.RegisterValueChangeCallback(evt =>
        {
            RefreshValidation(pet);
        });

        prefabField.RegisterValueChangeCallback(evt =>
        {
            RefreshValidation(pet);
        });
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
        var duplicates = new List<PetData>();
        if(string.IsNullOrWhiteSpace(selectedPet?.Id))
            return duplicates;

        foreach(var pet in pets)
        {
            if(pet != selectedPet && pet.Id == selectedPet.Id)
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

#endregion

}

