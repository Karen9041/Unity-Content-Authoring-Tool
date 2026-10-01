using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

public class PetEditorWindow : EditorWindow
{
    private List<PetData> pets = new();

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
        var rightPane = new VisualElement();

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
            rightPane.Clear();

            foreach(var item in selection)
            {
                if(item is not PetData pet)
                    continue;
                
                rightPane.Add(new Label($"ID: {pet.Id}"));

                string displayName = string.IsNullOrWhiteSpace(pet.DisplayName) ? pet.name : pet.DisplayName;
                rightPane.Add(new Label($"Name: {displayName}"));
                
                rightPane.Add(new ObjectField("Icon")
                {
                   objectType = typeof(Sprite),
                   value = pet.Icon 
                });

                rightPane.Add(new ObjectField("Prefab")
                {
                    objectType = typeof(GameObject),
                    value = pet.Prefab
                });
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
}
