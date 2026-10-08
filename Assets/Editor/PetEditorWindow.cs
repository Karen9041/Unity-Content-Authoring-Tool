using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

public class PetEditorWindow : EditorWindow
{
    private List<PetData> pets = new();
    private PetData selectedPet;

    private enum EditorMode
    {
        Content,
        ArtAssets
    }

    private EditorMode currentMode = EditorMode.Content;

    private VisualElement contentView;
    private VisualElement artAssetsView;

    private VisualElement rightPane;
    private VisualElement artRightPane;

    private ListView listView;
    private ListView artCandidateListView;

    private VisualElement contentValidationContainer;
    private VisualElement artValidationContainer;

    private readonly Dictionary<PetData, List<ValidationResult>> validationCache = new();
    private readonly Dictionary<string, List<PetData>> petsById = new();
    private Image iconPreview;
    private Image PrefabPreview;
    private PetData previewPet;
    private const string PetArtRoot = "Assets/Game/Arts/Pets";
    private List<PetArtCandidate> artCandidates = new();
    

    [MenuItem("Tools/Content Tools/Pet Editor")]
    public static void Open()
    {
        GetWindow<PetEditorWindow>("Pet Editor");
    }

    public void CreateGUI()
    {
        RefreshPetList();
        RefreshArtCandidates();

        BuildToolbar();
        BuildContentView();
        BuildArtAssetsView();

        SwitchMode(EditorMode.Content);
    }

    private void BuildToolbar()
    {
        var toolbar = new VisualElement();

        toolbar.style.flexDirection = FlexDirection.Row;

        var contentButton = new Button(() =>
        {
            SwitchMode(EditorMode.Content);
        })
        {
            text = "Content"
        };

        var artAssetsButton = new Button(() =>
        {
            SwitchMode(EditorMode.ArtAssets);
        })
        {
            text = "Art Assets"
        };

        toolbar.Add(contentButton);
        toolbar.Add(artAssetsButton);

        rootVisualElement.Add(toolbar);
    }

    private void BuildContentView()
    {
        contentView = new VisualElement();

        var splitView = new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal);

        var leftPane = new VisualElement();
        rightPane = new VisualElement();

        contentValidationContainer = new VisualElement();

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

        contentView.Add(splitView);

        rootVisualElement.Add(contentView);
    }

    private void BuildArtAssetsView()
    {
        artAssetsView = new VisualElement();

        var splitView = new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal);

        var leftPane = new VisualElement();
        artRightPane = new VisualElement();

        artValidationContainer = new VisualElement();

        artCandidateListView = new ListView
        {
            itemsSource = artCandidates,

            makeItem = () => new Label(),

            bindItem = (element, index) =>
            {
                var label = (Label)element;
                var candidate = artCandidates[index];

                var validation =
                    ValidateArtCandidate(candidate);

                string folderName =
                    System.IO.Path.GetFileName(
                        candidate.FolderPath);

                bool hasError = validation.Exists(
                    r => r.Severity == ValidationSeverity.Error);

                bool hasWarning = validation.Exists(
                    r => r.Severity == ValidationSeverity.Warning);

                string prefix =
                    hasError ? "✕" :
                    hasWarning ? "⚠" :
                    "✓";

                label.text =
                    $"{prefix} {folderName}";
            },

            selectionType = SelectionType.Single
        };

        artCandidateListView.selectionChanged += selection =>
        {
            foreach (var item in selection)
            {
                if (item is not PetArtCandidate candidate)
                    continue;

                ShowArtCandidate(candidate);
            }
        };

        leftPane.Add(new Label($"Art Candidates: {artCandidates.Count}"));
        leftPane.Add(artCandidateListView);

        splitView.Add(leftPane);
        splitView.Add(artRightPane);

        artAssetsView.Add(splitView);

        rootVisualElement.Add(artAssetsView);
    }

    private void ShowArtCandidate(PetArtCandidate candidate)
    {
        artRightPane.Clear();

        artRightPane.Add(
            new Label(candidate.FolderPath)
        );
    }

    private void SwitchMode(EditorMode mode)
    {
        currentMode = mode;

        contentView.style.display =
            mode == EditorMode.Content
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        artAssetsView.style.display =
            mode == EditorMode.ArtAssets
                ? DisplayStyle.Flex
                : DisplayStyle.None;
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

        iconPreview = new Image
        {
          scaleMode = ScaleMode.ScaleToFit,  
        };

        iconPreview.style.width = 128;
        iconPreview.style.height = 128;
        iconPreview.image = pet.Icon != null? pet.Icon.texture : null;

        var iconPreviewContainer = new VisualElement();
        iconPreviewContainer.Add(iconPreview);
        iconPreviewContainer.style.flexDirection = FlexDirection.Row;
        iconPreviewContainer.style.justifyContent = Justify.FlexEnd;        

        rightPane.Add(iconPreviewContainer);

        PropertyField prefabField = new PropertyField(serializedPet.FindProperty("prefab"));
        rightPane.Add(prefabField);

        PrefabPreview = new Image
        {
            scaleMode = ScaleMode.ScaleToFit,
        };

        PrefabPreview.style.width = 160;
        PrefabPreview.style.height = 160;
        RefreshPrefabPreview(pet);

        var prefabPreviewContainer = new VisualElement();
        prefabPreviewContainer.Add(PrefabPreview);
        prefabPreviewContainer.style.flexDirection = FlexDirection.Row;
        prefabPreviewContainer.style.justifyContent = Justify.FlexEnd;

        rightPane.Add(prefabPreviewContainer);

        rightPane.Bind(serializedPet);

        rightPane.Add(contentValidationContainer);

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
            iconPreview.image = pet.Icon != null? pet.Icon.texture : null;

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
            RefreshPrefabPreview(pet);

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

    private void RefreshPrefabPreview(PetData pet)
    {
        previewPet = pet;

        if(pet == null || pet.Prefab == null)
        {
            PrefabPreview.image = null;
            EditorApplication.update -= PollPrefabPreview;
            return;
        }

        var prefabTexture = AssetPreview.GetAssetPreview(pet.Prefab);
        if(prefabTexture != null)
        {
            PrefabPreview.image = prefabTexture;
            EditorApplication.update -= PollPrefabPreview;
            return;
        }

        EditorApplication.update += PollPrefabPreview;
        EditorApplication.update -= PollPrefabPreview;
    }

    private void PollPrefabPreview()
    {
        if(previewPet == null || previewPet.Prefab == null)
        {
            EditorApplication.update -= PollPrefabPreview;
            return;
        }

        if(previewPet != selectedPet)
        {
            EditorApplication.update -= PollPrefabPreview;
            return;
        }

        var prefabTexture = AssetPreview.GetAssetPreview(previewPet.Prefab);
        if(prefabTexture == null)
            return;

        PrefabPreview.image = prefabTexture;
        EditorApplication.update -= PollPrefabPreview;
        
        Repaint();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.update -= PollPrefabPreview;
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
        contentValidationContainer.Clear();
        contentValidationContainer.Add(new Label("Validation"));
        var validationResults = ValidatePet(pet);
        foreach(var result in validationResults)
        {
            contentValidationContainer.Add(
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

    private List<ValidationResult> ValidateArtCandidate(PetArtCandidate candidate)
    {
        var results = new List<ValidationResult>();

        if(candidate.Prefabs.Count == 0)
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Prefab is missing"
            });
        }else if(candidate.Prefabs.Count > 1){
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = $"Multiple prefabs found ({candidate.Prefabs.Count})."
            });
        }

        if(candidate.Icons.Count == 0)
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Icon is missing"
            });
        }else if(candidate.Icons.Count > 1){
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = $"Multiple Icons found ({candidate.Icons.Count})."
            });
        }

        return results;
    }

#endregion

#region Scanner

    public class PetArtCandidate
    {
        public string FolderPath;
        public List<GameObject> Prefabs = new();
        public List<Sprite> Icons = new();
    }

    private List<GameObject> FindPetPrefabs()
    {
        var results = new List<GameObject>();

        string[] guids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] {PetArtRoot});
        
        foreach(string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if(prefab != null)
            {
                results.Add(prefab);
            }
        }

        return results;
    }

    private List<Sprite> FindPetIcons()
    {
        var results = new List<Sprite>();

        string[] guids = AssetDatabase.FindAssets(
            "t:Sprite",
            new[] {PetArtRoot});

        foreach(string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if(sprite != null)
            {
                results.Add(sprite);
            }
        }

        return results;
    }

    private void RefreshArtCandidates()
    {
        artCandidates.Clear();

        var candidatesByFolder = new Dictionary<string, PetArtCandidate>();

        foreach(string folder in AssetDatabase.GetSubFolders(PetArtRoot))
        {
            candidatesByFolder[folder] = new PetArtCandidate
            {
                FolderPath = folder
            };
        }

        foreach(GameObject prefab in FindPetPrefabs())
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            string folder = GetPetRootFolder(path);

            if(folder != null && candidatesByFolder.TryGetValue(folder, out var candidate))
            {
                candidate.Prefabs.Add(prefab);
            }
        }

        foreach(Sprite icon in FindPetIcons())
        {
            string path = AssetDatabase.GetAssetPath(icon);
            string folder = GetPetRootFolder(path);

            if(folder != null && candidatesByFolder.TryGetValue(folder, out var candidate))
            {
                candidate.Icons.Add(icon);
            }
        }

        artCandidates = new List<PetArtCandidate>(candidatesByFolder.Values);
    }

    private string GetPetRootFolder(string assetPath)
    {
        string prefix = PetArtRoot.TrimEnd('/') + "/";

        if (!assetPath.StartsWith(prefix))
            return null;

        string relativePath =
            assetPath.Substring(prefix.Length);

        int slashIndex = relativePath.IndexOf('/');

        if (slashIndex < 0)
            return null;

        string petFolderName =
            relativePath.Substring(0, slashIndex);

        return prefix + petFolderName;
    }

#endregion
}

