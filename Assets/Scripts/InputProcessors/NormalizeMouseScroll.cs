using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public class NormalizeMouseScroll : InputProcessor<Vector2>
{
    [Tooltip("Scale factor to multiply normalized scroll values")]
    public float scrollScale = 120;
    
    public override Vector2 Process(Vector2 value, InputControl control) {
        Vector2 normalizedScroll = value.normalized * scrollScale;
        #if UNITY_STANDALONE_LINUX
            normalizedScroll *= -1;
        #endif
        return normalizedScroll;
    }
    
    #if UNITY_EDITOR
    static NormalizeMouseScroll()
    {
        Initialize();
    }
    #endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        InputSystem.RegisterProcessor<NormalizeMouseScroll>();
    }
}