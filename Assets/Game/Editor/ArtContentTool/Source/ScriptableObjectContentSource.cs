using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

public class ScriptableObjectContentSource: IContentSource
{
    public ContentLoadResult Load()
    {
        var result = new ContentLoadResult();

        string[] guids = AssetDatabase.FindAssets("t:PetData");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            var pet = AssetDatabase.LoadAssetAtPath<PetData>(path);  

            if (pet == null)
                continue;

            result.Records.Add(new ContentRecord
            {
                Definition = ConvertPetData(pet),

                Source = new ContentSourceHandle
                {
                    SourceType = "ScriptableObject",
                    SourceId = path
                }
            });
        }

        return result;
    }

    private ContentDefinition ConvertPetData(PetData pet)
    {
        return new ContentDefinition
        {
            Id = pet.Id,
            Name = pet.DisplayName,

            PrefabKey = pet.Prefab != null ? pet.Prefab.name : null,

            IconKey = pet.Icon != null ? pet.Icon.name : null
        };
    }
}