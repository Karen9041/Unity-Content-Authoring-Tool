using UnityEngine;

[CreateAssetMenu(
    fileName = "PetData",
    menuName = "Game Content/Pet Data")]
public class PetData : ScriptableObject
{
    [SerializeField]
    private string id;
    
    [SerializeField]
    private string displayName;

    [SerializeField]
    private Sprite icon;

    [SerializeField]
    private GameObject prefab;

    public string Id => id;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public GameObject Prefab => prefab;
}
