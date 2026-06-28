using UnityEngine;

/// <summary>
/// Editor-only helper: samples a chosen clip (e.g. an idle) onto this character in the
/// Scene view while NOT playing, so placed prefabs read clearly for level design instead
/// of sitting in bind/T-pose or whatever frame their bones were last left in.
///
/// No-op at runtime — the Animator takes over the moment you enter Play mode. Lives in the
/// runtime assembly (not an Editor folder) because <see cref="ExecuteAlways"/> components
/// must, but its body is editor-gated so it does nothing in a build.
///
/// Put this on the same GameObject as the Animator and assign a (Humanoid) idle clip.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class EditorPosePreview : MonoBehaviour
{
    [Tooltip("Clip sampled onto this object in the Scene view while not playing. Assign an idle clip (Humanoid clips retarget to any Humanoid orc).")]
    [SerializeField] private AnimationClip editorPose;

    [Tooltip("Normalized time (0-1) of the clip to sample for the preview pose.")]
    [Range(0f, 1f)]
    [SerializeField] private float normalizedTime = 0f;

    private void OnEnable() => ApplyPose();
    private void OnValidate() => ApplyPose();

    private void ApplyPose()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || editorPose == null) return;
        editorPose.SampleAnimation(gameObject, editorPose.length * Mathf.Clamp01(normalizedTime));
#endif
    }
}
