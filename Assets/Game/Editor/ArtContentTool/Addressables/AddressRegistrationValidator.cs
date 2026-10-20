using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager.Requests;
using System.Text.RegularExpressions;

public class AddressRegistrationValidator
{
    public AddressValidationResult Validate(ContentAssetMapping mapping)
    {
        var result = new AddressValidationResult();

        if(mapping == null ||
          mapping.Prefab == null ||
          mapping.Prefab.Status != AssetMatchStatus.Matched || 
          mapping.Prefab.Matches.Count != 1)
        {
            result.Status = AddressValidationStatus.MappingUnavailable;
            result.Message = "Prefab mapping must be uniquely matched.";
            return result;
        }

        var assetRecord = mapping.Prefab.Matches[0];

        string guid = assetRecord.Guid;
        string expectedAddress = mapping.Content.Definition.Id;

        result.ExpectedAddress = expectedAddress;
        result.AssetGuid = guid;

        var settings = AddressableAssetSettingsDefaultObject.Settings;

        if(settings == null)
        {
            result.Status = AddressValidationStatus.SettingsMissing;
            result.Message = "Addressables Settings is unavailable.";
            return result;
        }

        AddressableAssetEntry entry = settings.FindAssetEntry(guid, false);

        if(entry == null)
        {
            result.Status = AddressValidationStatus.EntryMissing;
            result.Message = $"{guid} Entry is unavailable.";
            return result;
        }

        if(entry.address != expectedAddress)
        {
            result.ActualAddress = entry.address;
            result.Status = AddressValidationStatus.AddressMismatch;
            result.Message = $"Expected {expectedAddress} is mismatch to {entry.address}";
            return result;
        }

        var matches = FindEntryesByAddress(settings, expectedAddress);

        if(matches.Count > 1)
        {
            result.Status = AddressValidationStatus.DuplicateAddress;
            result.Message = $"Address '{expectedAddress}' is used by {matches.Count} entries.";
            return result;
        }

        result.ActualAddress = entry.address;
        result.Status = AddressValidationStatus.Valid;
        result.Message = "Entry found";

        return result;
    }

    private List<AddressableAssetEntry> FindEntryesByAddress(AddressableAssetSettings settings, string address)
    {
        var matches = new List<AddressableAssetEntry>();

        foreach(var group in settings.groups)
        {
            if(group == null)
                continue;

            foreach(var entry in group.entries)
            {
                if(entry.address == address)
                {
                    matches.Add(entry);
                }
            }
        }

        return matches;
    }
}
