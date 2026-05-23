using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SimpleAIFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private bool findTargetByTag = true;
    [SerializeField] private string targetTag = "Player";

    [Header("Follow")]
    [SerializeField] private float followDistance = 2f;
    [SerializeField] private float repathInterval = 0.2f;
    [SerializeField] private float repathDistance = 0.75f;
    [SerializeField] private bool stopWhenTargetMissing = true;
    [SerializeField] private bool stopWhenPathInvalid = true;

    [Header("NavMesh Sampling")]
    [SerializeField] private float sampleHeightOffset = 1f;
    [SerializeField] private float sampleRadius = 3f;
    [SerializeField] private bool requireCompletePath = true;

    [Header("Rotation")]
    [SerializeField] private bool rotateTowardMovement = true;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float rotationVelocityThreshold = 0.01f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private float animatorDampTime = 0.1f;
    [SerializeField] private float minimumAgentSpeed = 0.01f;

    private NavMeshAgent agent;
    private NavMeshPath path;
    private Vector3 lastDestination;
    private Vector3 lastSampledTarget;
    private float nextRepathTime;
    private int speedParameterHash;
    private bool hasDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        speedParameterHash = Animator.StringToHash(speedParameter);
        agent.stoppingDistance = followDistance;
    }

    private void Update()
    {
        agent.stoppingDistance = followDistance;

        if (target == null && findTargetByTag)
            TryFindTargetByTag();

        if (target == null)
        {
            if (stopWhenTargetMissing)
                StopAgent();

            ApplyAnimatorSpeed(0f);
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            ApplyAnimatorSpeed(0f);
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (distanceToTarget <= followDistance)
        {
            StopAgent();
            ApplyAnimatorSpeed(0f);
            return;
        }

        if (ShouldRefreshDestination())
            RefreshPathToTarget();

        RotateTowardAgentMovement();
        ApplyAnimatorSpeed(GetNormalizedAgentSpeed());
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        hasDestination = false;
        nextRepathTime = 0f;
    }

    private bool ShouldRefreshDestination()
    {
        if (!hasDestination)
            return true;

        if (!agent.hasPath)
            return true;

        if (Time.time < nextRepathTime)
            return false;

        return Vector3.Distance(lastDestination, target.position) >= repathDistance;
    }

    private void RefreshPathToTarget()
    {
        if (!TryGetSampledTargetPosition(out Vector3 sampledTarget))
        {
            if (stopWhenPathInvalid)
                StopAgent();

            return;
        }

        if (hasDestination && Vector3.Distance(lastSampledTarget, sampledTarget) < repathDistance && Time.time < nextRepathTime)
            return;

        path.ClearCorners();

        bool calculated = agent.CalculatePath(sampledTarget, path);
        bool usablePath = calculated && path.status != NavMeshPathStatus.PathInvalid;

        if (requireCompletePath)
            usablePath = usablePath && path.status == NavMeshPathStatus.PathComplete;

        if (!usablePath)
        {
            if (stopWhenPathInvalid)
                StopAgent();

            return;
        }

        agent.isStopped = false;

        if (agent.SetPath(path))
        {
            lastDestination = target.position;
            lastSampledTarget = sampledTarget;
            hasDestination = true;
            nextRepathTime = Time.time + repathInterval;
        }
    }

    private bool TryGetSampledTargetPosition(out Vector3 sampledTarget)
    {
        Vector3 sampleOrigin = target.position + Vector3.up * sampleHeightOffset;

        if (NavMesh.SamplePosition(sampleOrigin, out NavMeshHit hit, sampleRadius, agent.areaMask))
        {
            sampledTarget = hit.position;
            return true;
        }

        sampledTarget = target.position;
        return false;
    }

    private void StopAgent()
    {
        if (!agent.enabled || !agent.isOnNavMesh)
            return;

        agent.isStopped = true;

        if (agent.hasPath || agent.pathPending)
            agent.ResetPath();

        hasDestination = false;
    }

    private float GetNormalizedAgentSpeed()
    {
        if (agent.pathPending || !agent.hasPath)
            return 0f;

        if (requireCompletePath && agent.pathStatus != NavMeshPathStatus.PathComplete)
            return 0f;

        if (agent.speed <= minimumAgentSpeed)
            return 0f;

        return Mathf.Clamp01(agent.velocity.magnitude / agent.speed);
    }

    private void ApplyAnimatorSpeed(float normalizedSpeed)
    {
        if (animator == null || string.IsNullOrEmpty(speedParameter))
            return;

        animator.SetFloat(speedParameterHash, normalizedSpeed, animatorDampTime, Time.deltaTime);
    }

    private void RotateTowardAgentMovement()
    {
        if (!rotateTowardMovement || agent.updateRotation)
            return;

        Vector3 moveDirection = agent.velocity;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude <= rotationVelocityThreshold * rotationVelocityThreshold)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void TryFindTargetByTag()
    {
        if (string.IsNullOrEmpty(targetTag))
            return;

        GameObject targetObject = GameObject.FindGameObjectWithTag(targetTag);
        if (targetObject != null)
            target = targetObject.transform;
    }
}
