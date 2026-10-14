using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

public class ArtContentToolWindow  : EditorWindow
{
    private List<ContentRecord> contents = new();
    private IContentSource contentSource;
    private List<SourceDiagnostic> sourceDiagnostics = new();
    private ContentRecord selectedContent;

    private enum EditorMode
    {
        Content,
        ArtAssets
    }

    private EditorMode currentMode = EditorMode.Content;

    private VisualElement contentView;
    private VisualElement artAssetsView;
    private VisualElement rightPane;
    private ListView listView;
    private VisualElement contentValidationContainer;
    private Label contentCount;

    private readonly Dictionary<ContentRecord, List<ValidationResult>> validationCache = new();
    private readonly Dictionary<string, List<ContentRecord>> contentsById = new();

    private readonly List<AssetRecord> assetRecords = new();
    private ListView assetListView;
    private VisualElement assetDetailPane;

    private const string ArtContentRoot = "Assets/Game/Arts";

    [MenuItem("Tools/Content Tools/Art Content Tool")]
    public static void Open()
    {
        GetWindow<ArtContentToolWindow >("Art Content Tool");
    }

    public void CreateGUI()
    {
        contentSource = new FakeContentSource();

        RefreshContentList();
        ValidateAllContents();

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

    private void SetContentSource(
    IContentSource source)
    {
        contentSource = source;

        selectedContent = null;

        RefreshContentList();
        ValidateAllContents();

        listView.itemsSource = contents;
        listView.ClearSelection();
        listView.Rebuild();

        rightPane.Clear();
    }

    private void BuildContentView()
    {
        contentView = new VisualElement();

        var soSourceButton = new Button(() =>
        {
            SetContentSource(
                new ScriptableObjectContentSource()
            );
        })
        {
            text = "SO Source"
        };

        var fakeSourceButton = new Button(() =>
        {
            SetContentSource(
                new FakeContentSource()
            );
        })
        {
            text = "Fake Source"
        };

        var excelSourceButton = new Button(() =>
        {
            SetContentSource(
                new ExcelContentSource(
                    "Assets/Game/ContentData/Content.xlsx",
                    "PetSetting"
                )
            );
        })
        {
            text = "Excel Source"
        };

        contentView.Add(soSourceButton);
        contentView.Add(fakeSourceButton);
        contentView.Add(excelSourceButton);

        var splitView = new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal);

        var leftPane = new VisualElement();
        rightPane = new VisualElement();

        contentValidationContainer = new VisualElement();

        listView = new ListView
        {
            itemsSource = contents,
            makeItem = () => new Label(),
            bindItem = (element, index) =>
            {
                var label = (Label)element;
                var record = contents[index];

                if(!validationCache.TryGetValue(record, out var results))
                {
                    results = new List<ValidationResult>();
                }

                if (results.Count > 0)
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
                
                label.text = contents[index].Definition.Name;
            },
            selectionType = SelectionType.Single
        };

        listView.selectionChanged += selection =>
        {
            foreach(var item in selection)
            {
                if(item is not ContentRecord record)
                    continue;
                
                selectedContent = record;
                ShowContent(record);
            }
        };

        contentCount = new Label($"Contents: {contents.Count}");

        leftPane.Add(contentCount);
        leftPane.Add(listView);

        splitView.Add(leftPane);
        splitView.Add(rightPane);

        contentView.Add(splitView);

        rootVisualElement.Add(contentView);
    }

    private void BuildArtAssetsView()
    {
        artAssetsView = new VisualElement();

        var rescanButton = new Button(() =>
        {
            RescanAssets();
        })
        {
            text = "Rescan Assets"
        };

        artAssetsView.Add(rescanButton);

        var splitView = new TwoPaneSplitView(0, 180, TwoPaneSplitViewOrientation.Horizontal);
        var leftPane = new VisualElement();
        assetDetailPane = new VisualElement();

        assetListView = new ListView
        {
            itemsSource = assetRecords,
            makeItem = () => new Label(),  
            bindItem = (element, index) =>
            {
                var label = (Label)element;
                var record = assetRecords[index];

                label.text = $"{record.AssetType} | {record.Name}";
            },

            selectionType = SelectionType.Single
        };

        leftPane.Add(assetListView);

        splitView.Add(leftPane);
        splitView.Add(assetDetailPane);

        artAssetsView.Add(splitView);

        rootVisualElement.Add(artAssetsView);

        assetListView.selectionChanged += selection =>
        {
            foreach(var item in selection)
            {
                if(item is not AssetRecord record)
                    continue;

                ShowAssetDetail(record);
            }
        };
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

    private void RefreshContentList()
    {
        var loadResult = contentSource.Load();

        contents.Clear();
        contents.AddRange(loadResult.Records);

        sourceDiagnostics.Clear();
        sourceDiagnostics.AddRange(loadResult.Diagnostics);

        foreach (var diagnostic in sourceDiagnostics)
        {
            string message =
                $"{diagnostic.SheetName} " +
                $"Row {diagnostic.Row} " +
                $"Field {diagnostic.FieldName}: " +
                diagnostic.Message;
            
            if(diagnostic.Severity == DiagnosticSeverity.Error)
                Debug.LogError(message);
            else
                Debug.LogWarning(message);
        }
    }

    private void ShowContent(ContentRecord record)
    {
        rightPane.Clear();

        rightPane.Add(
            new Label($"ID: {record.Definition.Id}"));

        rightPane.Add(
            new Label($"Name: {record.Definition.Name}"));

        rightPane.Add(
            new Label($"Prefab: {record.Definition.PrefabKey}"));

        rightPane.Add(
            new Label($"Icon: {record.Definition.IconKey}"));

        rightPane.Add(
            new Label($"Source: {record.Source.SourceId}"));

        rightPane.Add(contentValidationContainer);

        RefreshValidation(record);
    }

    private void ShowAssetDetail(AssetRecord record)
    {
        assetDetailPane.Clear();

        assetDetailPane.Add(
            new Label($"Name: {record.Name}"));

        assetDetailPane.Add(
            new Label($"Type: {record.AssetType}"));

        assetDetailPane.Add(
            new Label($"Path: {record.Path}"));

        assetDetailPane.Add(
            new Label($"GUID: {record.Guid}"));

        var pingButton = new Button(() =>
        {
            EditorGUIUtility.PingObject(record.Asset);
        })
        {
            text = "Ping"
        };

        var selectButton = new Button(() =>
        {
            Selection.activeObject = record.Asset;
        })
        {
            text = "Select"
        };

        assetDetailPane.Add(pingButton);
        assetDetailPane.Add(selectButton);
    }

    private void BuildContentIdIndex()
    {
        contentsById.Clear();

        foreach(var content in contents)
        {
            string id = content.Definition.Id;

            if(string.IsNullOrWhiteSpace(id))
                continue;

            if(!contentsById.TryGetValue(id, out var sameIdContents))
            {
                sameIdContents = new List<ContentRecord>();
                contentsById.Add(content.Definition.Id, sameIdContents);
            }

            sameIdContents.Add(content);
        }
    }

#region Validation 

    private void ValidateMissingId(ContentRecord record, List<ValidationResult> results)
    {
        if(!string.IsNullOrWhiteSpace(record.Definition.Id))
        {
            return;
        }

        results.Add(new ValidationResult
        {
            Severity = ValidationSeverity.Error,
            Message = "Content ID is required."
        });
    }

    private void ValidateDuplicateId(
        ContentRecord record, 
        Dictionary<string, List<ContentRecord>> idIndex, 
        List<ValidationResult> results)
    {
        string id = record.Definition.Id;

        if(string.IsNullOrWhiteSpace(id))
            return;

        if(!idIndex.TryGetValue(id, out var records))
            return;

        if(records.Count <= 1)
            return;

        results.Add(new ValidationResult
        {
            Severity = ValidationSeverity.Error,
            Message = $"Content ID '{id}' is duplicated."
        });
    }

    private void ValidateEvolutionTarget(
        ContentRecord record, 
        Dictionary<string, List<ContentRecord>> idIndex, 
        List<ValidationResult> results)
    {
        string targetId = record.Definition.EvolutionToId;

        if(string.IsNullOrWhiteSpace(targetId))
            return;

        if(idIndex.ContainsKey(targetId))
            return;

        results.Add(new ValidationResult
        {
            Severity = ValidationSeverity.Error,
            Message = $"Evolution target '{targetId}' does not exist."
        });
    }

    private void ValidateSelfEvolution(ContentRecord record, List<ValidationResult> results)
    {
        string id = record.Definition.Id;
        string targetId = record.Definition.EvolutionToId;

        if(string.IsNullOrWhiteSpace(id))
            return;

        if(string.IsNullOrWhiteSpace(targetId))
            return;
        
        if(id != targetId)
            return;

        results.Add(new ValidationResult
        {
           Severity = ValidationSeverity.Error,
           Message =  $"Content '{id}' cannot evolve into itself."
        });
    }

    public List<ValidationResult> ValidateContent(ContentRecord record)
    {
        var results = new List<ValidationResult>();

        ValidateMissingId(record, results);
        ValidateDuplicateId(record, contentsById, results);
        ValidateEvolutionTarget(record, contentsById, results);
        ValidateSelfEvolution(record, results);
        ValidateEvolutionCycle(record, contentsById, results);

        return results;
    }

    private void RefreshValidation(ContentRecord record)
    {
        contentValidationContainer.Clear();
        contentValidationContainer.Add(new Label("[Validation]"));
        var validationResults = ValidateContent(record);
        foreach(var result in validationResults)
        {
            contentValidationContainer.Add(
                new Label($"[{result.Severity}] {result.Message}")
            );
        }
    }

    private void ValidateContentAndUpdateCache(ContentRecord record)
    {
        validationCache[record] = ValidateContent(record);
    }

    private void ValidateAllContents()
    {
        validationCache.Clear();

        BuildContentIdIndex();

        foreach(var content in contents)
        {
            ValidateContentAndUpdateCache(content);
        }
    }

    //DFS helper
    private bool HasEvolutionCycle(
        string contentId, 
        Dictionary<string, List<ContentRecord>> idIndex, 
        HashSet<string> visiting, 
        HashSet<string> visited)
    {
        if(visiting.Contains(contentId))
            return true;

        if(visited.Contains(contentId))
            return false;

        if(!idIndex.TryGetValue(contentId, out var records))
            return false;

        if(records.Count != 1)
            return false;

        visiting.Add(contentId);

        var record = records[0];
        string nextId = record.Definition.EvolutionToId;

        //self-loop
        if(contentId == nextId)
            return false;

        if (!string.IsNullOrWhiteSpace(nextId))
        {
            if(HasEvolutionCycle(
                nextId,
                idIndex,
                visiting,
                visited
            ))
            {
                return true;
            }
        }

        visiting.Remove(contentId);
        visited.Add(contentId);

        return false;
    }

    private void ValidateEvolutionCycle(
        ContentRecord record,
        Dictionary<string, List<ContentRecord>> idIndex,
        List<ValidationResult> results
    )
    {
        string id = record.Definition.Id;

        if(string.IsNullOrWhiteSpace(id))
            return;

        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();

        if(!HasEvolutionCycle(id, idIndex, visiting, visited))
            return;

        results.Add(new ValidationResult
        {
           Severity = ValidationSeverity.Error,
           Message = $"Evolution cycle detected from '{id}'."
        });
    }

#endregion

#region Scanner

    private void RescanAssets()
    {
        assetRecords.Clear();

        ScanAssets<GameObject>("t:prefab", "prefab");
        ScanAssets<Sprite>("t:sprite", "Sprite");
        ScanAssets<Material>("t:Material", "Material");
        ScanAssets<RuntimeAnimatorController>("t:RuntimeAnimatorController", "AnimatorController");

        assetListView.itemsSource = assetRecords;
        assetListView.Rebuild();
    }

    private void ScanAssets<T>(string filter, string assetType) where T : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets(filter, new[] {ArtContentRoot});

        foreach(string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if(asset == null)
                continue;

            assetRecords.Add(new AssetRecord
            {
                Guid = guid,
                Path = path,
                Name = asset.name,
                AssetType= assetType,
                Asset = asset
            });
        }
    }

#endregion
}

