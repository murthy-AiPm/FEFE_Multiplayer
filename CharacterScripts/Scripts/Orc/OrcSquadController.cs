using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class OrcSquadMemberSetup
{
    [Tooltip("The orc controlled by this squad entry.")]
    public OrcAI orc;
    [Tooltip("Patrol behavior assigned to this orc when the squad initializes.")]
    public OrcPatrolMode patrolMode = OrcPatrolMode.Wander;
    [Tooltip("Loop/PingPong members use the squad Shared Patrol Points when this is checked.")]
    public bool useSharedPatrolPoints = true;
    [Tooltip("Optional route used only when Use Shared Patrol Points is unchecked.")]
    public Transform[] patrolPoints;
    [Tooltip("For Loop/PingPong routes, this member starts at a random route point.")]
    public bool randomizePatrolStartPoint = true;
}

public class OrcSquadController : MonoBehaviour
{
    [Header("Roster")]
    [Tooltip("Each entry controls one orc's squad membership and calm-state patrol behavior.")]
    [SerializeField] private OrcSquadMemberSetup[] members;
    [SerializeField] private OrcAI leader;
    [Tooltip("When no member entries are assigned, child OrcAI components are added at edit/runtime.")]
    [SerializeField] private bool autoFindChildMembers = true;

    [Header("Home")]
    [SerializeField] private Transform homeAnchor;
    [SerializeField] private bool useLeaderAsHomeAnchor = true;
    [SerializeField] private bool assignSharedHomeOnStart = true;

    [Header("Shared Patrol Route")]
    [Tooltip("Route used by member entries with Use Shared Patrol Points checked.")]
    [SerializeField] private Transform[] sharedPatrolPoints;

    [Header("Alert Sharing")]
    [SerializeField] private bool shareCombatTargets = true;
    [SerializeField] private float alertShareRadius = 20f;
    [SerializeField] private float leaderlessAlertRadiusMultiplier = 0.5f;
    [SerializeField] private float targetBroadcastInterval = 0.25f;
    [Tooltip("How many squadmates investigate a ranged hit or nearby missed-arrow impact before visual confirmation.")]
    [SerializeField] private int rangedAlertInvestigatorCount = 2;
    [Tooltip("Maximum distance from the hit/impact point for helper orcs to be selected. Set 0 to disable helper selection.")]
    [SerializeField] private float rangedAlertAssistRadius = 18f;
    [Tooltip("Repeated ranged disturbances inside this window trigger a camp alert instead of another investigation.")]
    [SerializeField] private int rangedAlertCampAlertThreshold = 2;
    [SerializeField] private float rangedAlertCampAlertWindow = 6f;
    [Tooltip("How long non-investigator orcs pause in guarded idle during a camp alert.")]
    [SerializeField] private float campAlertHoldDuration = 6f;

    [Header("Gizmos")]
    [SerializeField] private bool showPatrolGizmos = true;
    [Tooltip("Shows/hides each member OrcAI's LoS, detection, chase, investigate, wander, and combat-distance gizmos.")]
    [SerializeField] private bool showMemberOrcGizmos = true;
    [SerializeField] private Color sharedPatrolRouteGizmoColor = new Color(0.25f, 0.8f, 1f, 0.85f);
    [SerializeField] private Color memberPatrolRouteGizmoColor = new Color(1f, 0.65f, 0.15f, 0.85f);
    [SerializeField] private float patrolPointGizmoRadius = 0.25f;

    private float targetBroadcastTimer;
    private float rangedAlertCampAlertTimer;
    private bool registeredMembers;
    private int lastNearbyRangedImpactSequence;
    private int rangedAlertCampAlertCount;

    private bool IsServerActive => NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
    private bool IsLeaderCoordinationIntact => leader == null || leader.IsAlive;

    private void Awake()
    {
        PopulateChildMembersIfNeeded();
    }

    private void OnValidate()
    {
        PopulateChildMembersIfNeeded();
        patrolPointGizmoRadius = Mathf.Max(0.05f, patrolPointGizmoRadius);
        alertShareRadius = Mathf.Max(0f, alertShareRadius);
        rangedAlertInvestigatorCount = Mathf.Max(0, rangedAlertInvestigatorCount);
        rangedAlertAssistRadius = Mathf.Max(0f, rangedAlertAssistRadius);
        rangedAlertCampAlertThreshold = Mathf.Max(1, rangedAlertCampAlertThreshold);
        rangedAlertCampAlertWindow = Mathf.Max(0.1f, rangedAlertCampAlertWindow);
        campAlertHoldDuration = Mathf.Max(0.1f, campAlertHoldDuration);
        targetBroadcastInterval = Mathf.Max(0.05f, targetBroadcastInterval);
        leaderlessAlertRadiusMultiplier = Mathf.Max(0f, leaderlessAlertRadiusMultiplier);
        ApplyMemberOrcGizmoVisibility();
    }

    [ContextMenu("Rebuild Members From Children")]
    private void RebuildMembersFromChildren()
    {
        OrcAI[] childOrcs = GetComponentsInChildren<OrcAI>();
        members = new OrcSquadMemberSetup[childOrcs.Length];
        for (int i = 0; i < childOrcs.Length; i++)
        {
            members[i] = CreateDefaultMemberSetup(childOrcs[i]);
        }

        registeredMembers = false;
    }

    [ContextMenu("Fill Missing Members From Children")]
    private void FillMissingMembersFromChildren()
    {
        OrcAI[] childOrcs = GetComponentsInChildren<OrcAI>();
        if (childOrcs == null || childOrcs.Length == 0)
            return;

        int missingCount = 0;
        for (int i = 0; i < childOrcs.Length; i++)
        {
            if (!HasMember(childOrcs[i]))
                missingCount++;
        }

        if (missingCount == 0)
            return;

        int existingCount = members != null ? members.Length : 0;
        var nextMembers = new OrcSquadMemberSetup[existingCount + missingCount];
        for (int i = 0; i < existingCount; i++)
        {
            nextMembers[i] = members[i];
        }

        int writeIndex = existingCount;
        for (int i = 0; i < childOrcs.Length; i++)
        {
            OrcAI childOrc = childOrcs[i];
            if (HasMember(childOrc))
                continue;

            nextMembers[writeIndex] = CreateDefaultMemberSetup(childOrc);
            writeIndex++;
        }

        members = nextMembers;
        registeredMembers = false;
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

        TickRangedAlertMemory();

        if (!shareCombatTargets || members == null || members.Length == 0)
            return;

        targetBroadcastTimer -= Time.deltaTime;
        if (targetBroadcastTimer > 0f)
            return;

        targetBroadcastTimer = Mathf.Max(0.05f, targetBroadcastInterval);
        BroadcastCurrentTarget();
    }

    private void PopulateChildMembersIfNeeded()
    {
        if (!autoFindChildMembers || (members != null && members.Length > 0))
            return;

        OrcAI[] childOrcs = GetComponentsInChildren<OrcAI>();
        members = new OrcSquadMemberSetup[childOrcs.Length];
        for (int i = 0; i < childOrcs.Length; i++)
        {
            members[i] = CreateDefaultMemberSetup(childOrcs[i]);
        }
    }

    private OrcSquadMemberSetup CreateDefaultMemberSetup(OrcAI orc)
    {
        return new OrcSquadMemberSetup
        {
            orc = orc,
            patrolMode = OrcPatrolMode.Wander,
            useSharedPatrolPoints = true,
            randomizePatrolStartPoint = true
        };
    }

    private bool HasMember(OrcAI orc)
    {
        if (orc == null || members == null)
            return false;

        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] != null && members[i].orc == orc)
                return true;
        }

        return false;
    }

    private void TryRegisterMembers()
    {
        if (!IsServerActive || members == null || members.Length == 0)
            return;

        Vector3 homePosition = GetSharedHomePosition();
        for (int i = 0; i < members.Length; i++)
        {
            OrcSquadMemberSetup setup = members[i];
            OrcAI member = setup != null ? setup.orc : null;
            if (member == null)
                continue;

            member.SetSquad(this);
            member.SetGizmosVisible(showMemberOrcGizmos);

            if (assignSharedHomeOnStart)
                member.SetHomeAnchor(homePosition);

            member.SetPatrolRoute(
                setup.patrolMode,
                GetPatrolPointsFor(setup),
                setup.randomizePatrolStartPoint);
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

    private Transform[] GetPatrolPointsFor(OrcSquadMemberSetup setup)
    {
        if (setup == null)
            return null;

        return setup.useSharedPatrolPoints ? sharedPatrolPoints : setup.patrolPoints;
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
            OrcAI member = GetMemberOrc(i);
            if (member == null || member == source || !member.IsAlive)
                continue;

            Vector3 toSource = member.transform.position - sourcePosition;
            if (toSource.sqrMagnitude > shareRadiusSqr)
                continue;

            member.ReceiveSharedTarget(target, sourcePosition, strongCoordination);
        }
    }

    public bool HandleMemberDirectRangedHit(
        OrcAI hitMember,
        Vector3 sourcePosition,
        NetworkObject attackerObject)
    {
        if (!IsServerActive || hitMember == null)
            return false;

        if (RegisterRangedDisturbance())
        {
            BeginCampAlert(sourcePosition, hitMember);
            return true;
        }

        AssignRangedAlertInvestigators(hitMember.transform.position, sourcePosition, hitMember);
        return false;
    }

    public void HandleNearbyRangedImpact(
        int alertSequence,
        Vector3 impactPosition,
        Vector3 sourcePosition,
        NetworkObject attackerObject)
    {
        if (!IsServerActive || alertSequence == lastNearbyRangedImpactSequence)
            return;

        lastNearbyRangedImpactSequence = alertSequence;

        if (RegisterRangedDisturbance())
        {
            BeginCampAlert(sourcePosition, null);
            return;
        }

        AssignRangedAlertInvestigators(impactPosition, sourcePosition, null);
    }

    private void TickRangedAlertMemory()
    {
        if (rangedAlertCampAlertTimer <= 0f)
            return;

        rangedAlertCampAlertTimer -= Time.deltaTime;
        if (rangedAlertCampAlertTimer <= 0f)
            rangedAlertCampAlertCount = 0;
    }

    private bool RegisterRangedDisturbance()
    {
        if (rangedAlertCampAlertTimer <= 0f)
            rangedAlertCampAlertCount = 0;

        rangedAlertCampAlertCount++;
        rangedAlertCampAlertTimer = Mathf.Max(0.1f, rangedAlertCampAlertWindow);

        if (rangedAlertCampAlertCount < rangedAlertCampAlertThreshold)
            return false;

        rangedAlertCampAlertCount = 0;
        rangedAlertCampAlertTimer = 0f;
        return true;
    }

    private void BeginCampAlert(Vector3 sourcePosition, OrcAI triggeringMember)
    {
        if (members == null)
            return;

        int guardIndex = 0;
        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = GetMemberOrc(i);
            if (member == null || !member.IsAlive || member.HasCombatTarget)
                continue;

            if (member == triggeringMember || member.IsRangedInvestigationActive)
            {
                member.ReceiveCampAlertReturnHome(sourcePosition);
                continue;
            }

            bool faceThreat = guardIndex % 2 == 0;
            member.ReceiveCampAlertHold(sourcePosition, campAlertHoldDuration, faceThreat);
            guardIndex++;
        }
    }

    private void AssignRangedAlertInvestigators(
        Vector3 alertOrigin,
        Vector3 sourcePosition,
        OrcAI excludedMember)
    {
        int count = Mathf.Max(0, rangedAlertInvestigatorCount);
        if (count == 0 || rangedAlertAssistRadius <= 0f)
            return;

        OrcAI[] selected = new OrcAI[count];
        for (int i = 0; i < count; i++)
        {
            OrcAI investigator = FindBestRangedAlertInvestigator(alertOrigin, excludedMember, selected);
            if (investigator == null)
                return;

            selected[i] = investigator;
            investigator.ReceiveSharedAlert(sourcePosition, null, true);
        }
    }

    private OrcAI FindBestRangedAlertInvestigator(
        Vector3 alertOrigin,
        OrcAI excludedMember,
        OrcAI[] selectedMembers)
    {
        OrcAI best = null;
        float bestScore = float.MaxValue;
        float assistRadiusSqr = rangedAlertAssistRadius * rangedAlertAssistRadius;

        if (members == null)
            return null;

        for (int i = 0; i < members.Length; i++)
        {
            OrcSquadMemberSetup setup = members[i];
            OrcAI member = setup != null ? setup.orc : null;
            if (member == null || member == excludedMember || !member.IsAlive || member.HasCombatTarget)
                continue;

            if (IsSelected(member, selectedMembers))
                continue;

            Vector3 toAlert = member.transform.position - alertOrigin;
            float distanceSqr = toAlert.sqrMagnitude;
            if (distanceSqr > assistRadiusSqr)
                continue;

            float score = distanceSqr;
            if (setup.patrolMode == OrcPatrolMode.Idle)
                score += 1000f;
            if (member == leader)
                score += 2000f;

            if (score < bestScore)
            {
                bestScore = score;
                best = member;
            }
        }

        return best;
    }

    private bool IsSelected(OrcAI member, OrcAI[] selectedMembers)
    {
        if (member == null || selectedMembers == null)
            return false;

        for (int i = 0; i < selectedMembers.Length; i++)
        {
            if (selectedMembers[i] == member)
                return true;
        }

        return false;
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

        if (members == null)
            return false;

        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = GetMemberOrc(i);
            if (member == null || !member.IsAlive || !member.HasCombatTarget)
                continue;

            source = member;
            target = member.CurrentTarget;
            return target != null;
        }

        return false;
    }

    private OrcAI GetMemberOrc(int index)
    {
        if (members == null || index < 0 || index >= members.Length)
            return null;

        return members[index] != null ? members[index].orc : null;
    }

    private void ApplyMemberOrcGizmoVisibility()
    {
        if (members == null)
            return;

        for (int i = 0; i < members.Length; i++)
        {
            OrcAI member = GetMemberOrc(i);
            if (member != null)
                member.SetGizmosVisible(showMemberOrcGizmos);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showPatrolGizmos)
            return;

        DrawAllPatrolGizmos();
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (!showPatrolGizmos || !IsAnyPatrolPointSelected())
            return;

        DrawAllPatrolGizmos();
#endif
    }

    private void DrawAllPatrolGizmos()
    {
        DrawPatrolPath(sharedPatrolPoints, GetSharedPatrolGizmoMode(), sharedPatrolRouteGizmoColor);

        if (members == null)
            return;

        for (int i = 0; i < members.Length; i++)
        {
            OrcSquadMemberSetup setup = members[i];
            if (setup == null || setup.useSharedPatrolPoints)
                continue;

            DrawPatrolPath(setup.patrolPoints, setup.patrolMode, memberPatrolRouteGizmoColor);
        }
    }

#if UNITY_EDITOR
    private bool IsAnyPatrolPointSelected()
    {
        Transform[] selectedTransforms = Selection.transforms;
        if (selectedTransforms == null || selectedTransforms.Length == 0)
            return false;

        for (int i = 0; i < selectedTransforms.Length; i++)
        {
            if (IsPatrolPointTransform(selectedTransforms[i]))
                return true;
        }

        return false;
    }

    private bool IsPatrolPointTransform(Transform selected)
    {
        if (selected == null)
            return false;

        if (ContainsTransform(sharedPatrolPoints, selected))
            return true;

        if (members == null)
            return false;

        for (int i = 0; i < members.Length; i++)
        {
            OrcSquadMemberSetup setup = members[i];
            if (setup == null || setup.useSharedPatrolPoints)
                continue;

            if (ContainsTransform(setup.patrolPoints, selected))
                return true;
        }

        return false;
    }

    private bool ContainsTransform(Transform[] points, Transform selected)
    {
        if (points == null)
            return false;

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == selected)
                return true;
        }

        return false;
    }
#endif

    private void DrawPatrolPath(Transform[] points, OrcPatrolMode mode, Color color)
    {
        if (points == null || points.Length == 0 || mode == OrcPatrolMode.Wander || mode == OrcPatrolMode.Idle)
            return;

        Gizmos.color = color;
        Transform firstPoint = null;
        Transform previousPoint = null;

        for (int i = 0; i < points.Length; i++)
        {
            Transform point = points[i];
            if (point == null)
                continue;

            Gizmos.DrawSphere(point.position, patrolPointGizmoRadius);

            if (firstPoint == null)
                firstPoint = point;

            if (previousPoint != null)
                Gizmos.DrawLine(previousPoint.position, point.position);

            previousPoint = point;
        }

        if (mode == OrcPatrolMode.Loop &&
            firstPoint != null &&
            previousPoint != null &&
            firstPoint != previousPoint)
        {
            Gizmos.DrawLine(previousPoint.position, firstPoint.position);
        }
    }

    private OrcPatrolMode GetSharedPatrolGizmoMode()
    {
        if (members == null)
            return OrcPatrolMode.PingPong;

        for (int i = 0; i < members.Length; i++)
        {
            OrcSquadMemberSetup setup = members[i];
            if (setup == null || !setup.useSharedPatrolPoints)
                continue;

            if (setup.patrolMode == OrcPatrolMode.Loop)
                return OrcPatrolMode.Loop;
        }

        return OrcPatrolMode.PingPong;
    }
}
