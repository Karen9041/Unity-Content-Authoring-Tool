using System.Collections.Generic;

public class AssetReferenceMatch
{
    public string Key;
    public string ExpectedType;

    public AssetMatchStatus Status;

    public List<AssetRecord> Matches = new();
}