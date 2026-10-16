using UnityEngine; 
public class ContentPrefabReferences : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private SkinnedMeshRenderer mainRenderer;

    public Animator Animator => animator;
    public SkinnedMeshRenderer  MainRenderer => mainRenderer;
}