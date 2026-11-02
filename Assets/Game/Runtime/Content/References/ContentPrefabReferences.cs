using System;
using System.Collections.Generic;
using UnityEngine;

public enum ContentReferenceValueType
{
    Object, GameObject, Transform, SkinnedMeshRenderer, Animator, Texture, Animation, AnimationClip, String
}

[Serializable]
public sealed class ContentReferenceEntry
{
    public string Key;
    public ContentReferenceValueType ValueType;
    public UnityEngine.Object ObjectValue;
    public string StringValue = "";
}

public class ContentPrefabReferences : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private SkinnedMeshRenderer mainRenderer;
    [SerializeField] private List<ContentReferenceEntry> entries = new();

    public Animator Animator => animator;
    public SkinnedMeshRenderer  MainRenderer => mainRenderer;
    public IReadOnlyList<ContentReferenceEntry> Entries => entries;

    // Linear lookup deliberately avoids a second serialized/cache contract.
    public bool TryGetObject<T>(string key, out T value) where T : UnityEngine.Object
    {
        value = null;
        foreach (var entry in entries)
            if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                if (entry.ValueType == ContentReferenceValueType.String) return false;
                value = entry.ObjectValue as T;
                return value != null;
            }
        return false;
    }

    public bool TryGetString(string key, out string value)
    {
        value = null;
        foreach (var entry in entries)
            if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                if (entry.ValueType != ContentReferenceValueType.String) return false;
                value = entry.StringValue;
                return true;
            }
        return false;
    }
}
