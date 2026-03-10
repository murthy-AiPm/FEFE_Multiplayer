using UnityEngine;

public class RandomIdleAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Settings")]
    [SerializeField] private string parameterName = "IdleVariant";
    [SerializeField] private int variantCount = 3;
    [SerializeField] private float minInterval = 8f;
    [SerializeField] private float maxInterval = 20f;

    private MountableEntity _mountable;
    private int _paramHash;
    private float _timer;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _mountable = GetComponent<MountableEntity>();
        _paramHash = Animator.StringToHash(parameterName);
        ResetTimer();
    }

    private void Update()
    {
        if (animator == null) return;

        bool isMoving = _mountable != null && _mountable.IsMoving;

        if (isMoving)
        {
            animator.SetInteger(_paramHash, 0);
            ResetTimer();
            return;
        }

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            animator.SetInteger(_paramHash, Random.Range(1, variantCount + 1));
            ResetTimer();
        }
    }

    private void ResetTimer()
    {
        _timer = Random.Range(minInterval, maxInterval);
    }
}
