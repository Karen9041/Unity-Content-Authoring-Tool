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

        rightPane.Add(
            new PropertyField(serializedPet.FindProperty("id"))
        );

        rightPane.Add(
            new PropertyField(serializedPet.FindProperty("displayName"))
        );

        rightPane.Add(
            new PropertyField(serializedPet.FindProperty("icon"))
        );

        rightPane.Add(
            new PropertyField(serializedPet.FindProperty("prefab"))
        );

        rightPane.Bind(serializedPet);
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
        ShowPet(selectedPet);
    }
}
