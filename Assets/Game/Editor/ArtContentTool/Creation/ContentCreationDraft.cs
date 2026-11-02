using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor-only draft. No assets or ContentRecord entries are created.</summary>
[Serializable]
public class ContentCreationDraft
{
    // Match ContentDefinition's Id / integer ContentType convention.
    // Keep presence separate so zero remains a valid integer type.
    public int ContentType;
    public bool HasContentType;
    public string Id = "";
    public GameObject BaseTemplatePrefab;
    public GameObject SourceModelPrefab;
    public List<SkinnedMeshRenderer> Renderers = new();
    public bool HasDiscoveredRenderers;
    public ReferenceAuthoringDraft References = new();
}
