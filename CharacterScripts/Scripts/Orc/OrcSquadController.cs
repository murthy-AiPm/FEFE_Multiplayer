using Unity.Netcode;
using UnityEngine;

public class OrcSquadController : MonoBehaviour
{
    [Header("Roster")]
    [SerializeField] private OrcAI[] members;
    [SerializeField] private OrcAI leader;
    [SerializeField] private bool autoFindChildMembers = true;

    [Header("Home")]
    [SerializeField] private Transform homeAnchor;
    [SerializeField] private bool useLeaderAsHomeAnchor = true;
    [SerializeField] private bool assignSharedHomeOnStart = true;

    [Header("Patrol")]
    [SerializeField] private bool assignSharedPatrolRoute;
    [SerializeField] private OrcPatrolMode sharedPatrolMode = OrcPatrolMode.Wander;
    [SerializeField] private Transform[] sharedPatrolPoints;
    [SerializeField] private bool randomizeMemberPatrolStartPoint = true;

    [Header("Alert Sharing")]
    [SerializeField] private bool shareCombatTargets = true;
    [SerializeField] private float alertShareRadius = 20f;
    [SerializeField] private float leaderlessAlertRadiusMultiplier = 0.5f;
    [SerializeField] private float targetBroadcastInterval = 0.25f;

    private float targetBroadcastTimer;
    private bool registeredMembers;

    private bool IsServerActive => NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
    private bool IsLeaderCoordinationIntact => leader == null || leader.IsAlive;

    private void Awake()
    {
        if (autoFindChildMembers && (members == null || members.Length == 0))
            members = GetComponentsInChildren<OrcAI>();
    }

    private void Start()
    {
        TryRegisterMembers();
    }

    private void Update()
    {
        if (!IsServerActive)
            return;

        if (!registeredMembers)
            TryRegisterMembers();

        if (!shareCombatTargets || members == null || members.Length == 0)
            return;

        targetBroadcastTimer -= Time.deltaTime;
        if (targetBroadcastTimer > 0f)
            return;

        targetBroadcastTimer = Mathf.Max(0.05f, targetBroadcastInterval);
        BroadcastCurrentTarget();
    }

    private void TryRegisterMembers()
    {
        if (!IsServerActive || members == null || members.Length == 0)
            return;

        Vector3 homePosition = GetSharedHomePosition();
        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = members[i];
            if (member == null)
                continue;

            member.SetSquad(this);

            if (assignSharedHomeOnStart)
                member.SetHomeAnchor(homePosition);

            if (assignSharedPatrolRoute)
            {
                member.SetPatrolRoute(
                    sharedPatrolMode,
                    sharedPatrolPoints,
                    randomizeMemberPatrolStartPoint);
            }
        }

        registeredMembers = true;
    }

    private Vector3 GetSharedHomePosition()
    {
        if (useLeaderAsHomeAnchor && leader != null)
            return leader.transform.position;

        if (homeAnchor != null)
            return homeAnchor.position;

        return transform.position;
    }

    private void BroadcastCurrentTarget()
    {
        if (!TryGetBroadcastTarget(out OrcAI source, out Transform target))
            return;

        bool strongCoordination = IsLeaderCoordinationIntact;
        float shareRadius = alertShareRadius;
        if (!strongCoordination)
            shareRadius *= Mathf.Clamp01(leaderlessAlertRadiusMultiplier);

        if (shareRadius <= 0f)
            return;

        Vector3 sourcePosition = source.transform.position;
        float shareRadiusSqr = shareRadius * shareRadius;

        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = members[i];
            if (member == null || member == source || !member.IsAlive)
                continue;

            Vector3 toSource = member.transform.position - sourcePosition;
            if (toSource.sqrMagnitude > shareRadiusSqr)
                continue;

            member.ReceiveSharedTarget(target, sourcePosition, strongCoordination);
        }
    }

    private bool TryGetBroadcastTarget(out OrcAI source, out Transform target)
    {
        source = null;
        target = null;

        if (leader != null && leader.IsAlive && leader.HasCombatTarget)
        {
            source = leader;
            target = leader.CurrentTarget;
            return target != null;
        }

        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = members[i];
            if (member == null || !member.IsAlive || !member.HasCombatTarget)
                continue;

            source = member;
            target = member.CurrentTarget;
            return target != null;
        }

        return false;
    }
}
