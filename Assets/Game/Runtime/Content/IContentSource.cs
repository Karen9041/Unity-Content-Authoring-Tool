using UnityEngine;
using System.Collections.Generic;

public interface IContentSource
{
    IReadOnlyList<ContentRecord> Load();
}