using UnityEngine; 
public class ContentPrefabReferences : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Renderer mainRenderer;
    [SerializeField] private Transform rootBone;

    public Animator Animator => animator;
    public Renderer MainRenderer => mainRenderer;
    public Transform RootBone => rootBone;
}