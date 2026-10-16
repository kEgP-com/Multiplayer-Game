using System.Collections;
using UnityEngine;
using UnityEngine.AI;
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class EnemyAI : MonoBehaviour
{
    public enum AIState
    {
        Idle,
        Patrol,
        Chase,
        Attack
    }

    [Header("Sensory & Detection Settings")]
    [Tooltip("Radius of the 360-degree danger zone (RED indicator).")]
    [SerializeField] private float proximityRadius = 4.5f;

    [Tooltip("Forward range of the vision cone (BLUE indicator).")]
    [SerializeField] private float visionRadius = 16f;

    [Tooltip("Field-of-view angle for vision cone in degrees.")]
    [SerializeField] private float visionAngle = 90f;

    [Tooltip("Eye height from enemy base for line-of-sight raycasts.")]
    [SerializeField] private float eyeHeight = 1.6f;

    [Tooltip("Layers that block line of sight (e.g. walls, obstacles).")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Combat Settings")]
    [SerializeField] private float attackRange = 2.0f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float attackDamage = 20f;

    [Header("Patrol / Wander Settings")]
    [SerializeField] private float wanderRadius = 12f;
    [SerializeField] private float minWaitTime = 2f;
    [SerializeField] private float maxWaitTime = 4.5f;

    [Header("Movement Speeds")]
    [SerializeField] private float patrolSpeed = 2.5f;
    [SerializeField] private float chaseSpeed = 5.2f;

    // Runtime state
    private NavMeshAgent agent;
    private Health health;
    private EnemyVisualIndicators visualIndicators;
#if PHOTON_UNITY_NETWORKING
    private PhotonView photonView;
#endif

    private AIState currentState = AIState.Idle;
    private Transform currentTargetPlayer;
    private float lastAttackTime = -99f;
    private float nextWanderTime = 0f;
    private bool isAttacking = false;

    // Detection flags for indicators
    private bool isProximityTriggered = false;
    private bool isVisionConeTriggered = false;
    private bool hasSightThrough = false;

    public AIState CurrentState => currentState;
    public bool HasSightThrough => hasSightThrough;
    public bool IsProximityTriggered => isProximityTriggered;
    public bool IsVisionConeTriggered => isVisionConeTriggered;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        visualIndicators = GetComponent<EnemyVisualIndicators>();
#if PHOTON_UNITY_NETWORKING
        photonView = GetComponent<PhotonView>();
#endif

        if (health != null)
        {
            health.OnDamaged += OnTakeDamageReaction;
            health.OnDeath += OnDeath;
        }

        // Configure NavMeshAgent defaults
        agent.speed = patrolSpeed;
        agent.stoppingDistance = attackRange * 0.8f;
        agent.acceleration = 12f;
        agent.angularSpeed = 240f;
    }

    private void Start()
    {
        WanderToRandomPoint();
    }

    private void Update()
    {
        if (health != null && health.IsDead) return;

        // In multiplayer, MasterClient can drive AI decision logic
#if PHOTON_UNITY_NETWORKING
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            // Non-master clients only update local sensory visual indicators for smooth rendering
            UpdateSensoryDetection();
            UpdateVisualIndicators();
            return;
        }
#endif

        UpdateSensoryDetection();
        UpdateStateMachine();
        UpdateVisualIndicators();
    }

    #region Sensory Detection

    private void UpdateSensoryDetection()
    {
        isProximityTriggered = false;
        isVisionConeTriggered = false;
        hasSightThrough = false;

        // Find closest target player
        currentTargetPlayer = FindClosestPlayer();
        if (currentTargetPlayer == null) return;

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 targetChestPos = currentTargetPlayer.position + Vector3.up * 1.0f;
        Vector3 dirToTarget = targetChestPos - eyePos;
        float distanceToTarget = dirToTarget.magnitude;

        // 1. Proximity Check (RED): 360-degree close awareness
        if (distanceToTarget <= proximityRadius)
        {
            isProximityTriggered = true;
        }

        // 2. Vision Cone Check (BLUE): FOV cone forward check
        if (distanceToTarget <= visionRadius)
        {
            Vector3 flatForward = transform.forward;
            flatForward.y = 0;
            Vector3 flatDir = dirToTarget;
            flatDir.y = 0;

            float angle = Vector3.Angle(flatForward, flatDir);
            if (angle <= visionAngle * 0.5f)
            {
                isVisionConeTriggered = true;
            }
        }

        // 3. Sight-Through Check (YELLOW): Direct unobstructed raycast line-of-sight
        if (isVisionConeTriggered || isProximityTriggered)
        {
            // Raycast toward player to confirm unobstructed sightline
            if (Physics.Raycast(eyePos, dirToTarget.normalized, out RaycastHit hit, distanceToTarget + 0.5f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                // Verify if ray hit the player or a child of player
                if (hit.transform == currentTargetPlayer || hit.transform.IsChildOf(currentTargetPlayer))
                {
                    hasSightThrough = true;
                }
            }
            else
            {
                // Nothing obstructed the raycast
                hasSightThrough = true;
            }
        }
    }

    private Transform FindClosestPlayer()
    {
        FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        Transform closest = null;
        float minDst = float.MaxValue;

        Vector3 myPos = transform.position;
        foreach (FirstPersonController p in players)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            
            Health h = p.GetComponent<Health>();
            if (h != null && h.IsDead) continue;

            float dst = Vector3.Distance(myPos, p.transform.position);
            if (dst < minDst)
            {
                minDst = dst;
                closest = p.transform;
            }
        }

        return closest;
    }

    private void UpdateVisualIndicators()
    {
        if (visualIndicators != null)
        {
            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            visualIndicators.UpdateIndicators(
                proximityRadius,
                isProximityTriggered,
                visionRadius,
                visionAngle,
                isVisionConeTriggered,
                hasSightThrough,
                eyePos,
                currentTargetPlayer
            );
        }
    }

    #endregion

    #region State Machine

    private void UpdateStateMachine()
    {
        if (!agent.isOnNavMesh) return;

        // If player spotted via Sight-Through (Yellow) or Proximity (Red) -> Chase or Attack
        if (hasSightThrough || isProximityTriggered)
        {
            if (currentTargetPlayer != null)
            {
                float dst = Vector3.Distance(transform.position, currentTargetPlayer.position);
                if (dst <= attackRange)
                {
                    SwitchState(AIState.Attack);
                }
                else
                {
                    SwitchState(AIState.Chase);
                }
            }
        }
        else
        {
            // Player lost or out of sight -> return to Wander/Patrol
            if (currentState == AIState.Chase || currentState == AIState.Attack)
            {
                SwitchState(AIState.Patrol);
                nextWanderTime = Time.time + Random.Range(1f, 2.5f);
            }
        }

        // Execute current state logic
        switch (currentState)
        {
            case AIState.Idle:
            case AIState.Patrol:
                ExecutePatrol();
                break;
            case AIState.Chase:
                ExecuteChase();
                break;
            case AIState.Attack:
                ExecuteAttack();
                break;
        }
    }

    private void SwitchState(AIState newState)
    {
        if (currentState == newState) return;
        currentState = newState;

        switch (currentState)
        {
            case AIState.Patrol:
            case AIState.Idle:
                agent.isStopped = false;
                agent.speed = patrolSpeed;
                break;
            case AIState.Chase:
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                break;
            case AIState.Attack:
                agent.isStopped = true;
                break;
        }
    }

    private void ExecutePatrol()
    {
        if (!agent.isOnNavMesh) return;

        if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.3f)
        {
            if (Time.time >= nextWanderTime)
            {
                WanderToRandomPoint();
                nextWanderTime = Time.time + Random.Range(minWaitTime, maxWaitTime);
            }
        }
    }

    private void WanderToRandomPoint()
    {
        if (!agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;

        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit navHit, wanderRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
        }
    }

    private void ExecuteChase()
    {
        if (!agent.isOnNavMesh || currentTargetPlayer == null) return;

        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.SetDestination(currentTargetPlayer.position);
    }

    private void ExecuteAttack()
    {
        if (currentTargetPlayer == null) return;

        // Turn to smoothly face player
        Vector3 lookDir = currentTargetPlayer.position - transform.position;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        // Check if player moved out of attack range
        float dst = Vector3.Distance(transform.position, currentTargetPlayer.position);
        if (dst > attackRange + 0.8f)
        {
            SwitchState(AIState.Chase);
            return;
        }

        // Trigger attack on cooldown
        if (Time.time >= lastAttackTime + attackCooldown && !isAttacking)
        {
            StartCoroutine(PerformAttackRoutine());
        }
    }

    private IEnumerator PerformAttackRoutine()
    {
        isAttacking = true;
        lastAttackTime = Time.time;

        // Visual wind-up / lunge animation
        Vector3 originalScale = transform.localScale;
        transform.localScale = originalScale * 1.15f;

        yield return new WaitForSeconds(0.18f);

        // Inflict damage if still in range
        if (currentTargetPlayer != null)
        {
            float dst = Vector3.Distance(transform.position, currentTargetPlayer.position);
            if (dst <= attackRange + 1.2f)
            {
                IDamageable playerDamageable = currentTargetPlayer.GetComponent<IDamageable>();
                if (playerDamageable != null)
                {
                    playerDamageable.TakeDamage(attackDamage, currentTargetPlayer.position + Vector3.up, -transform.forward);
                }
            }
        }

        yield return new WaitForSeconds(0.12f);
        transform.localScale = originalScale;
        isAttacking = false;
    }

    #endregion

    #region Reaction to Damage & Death

    private void OnTakeDamageReaction(float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        // Immediately trigger alert & aggro towards damage source
        isProximityTriggered = true;
        if (currentTargetPlayer == null)
        {
            currentTargetPlayer = FindClosestPlayer();
        }
        if (currentTargetPlayer != null)
        {
            SwitchState(AIState.Chase);
        }

        StartCoroutine(FlashDamageRoutine());
    }

    private IEnumerator FlashDamageRoutine()
    {
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var r in renderers)
        {
            if (r != null && r.material != null && r.material.HasProperty("_BaseColor"))
            {
                r.material.SetColor("_BaseColor", Color.white);
            }
        }

        yield return new WaitForSeconds(0.08f);

        foreach (var r in renderers)
        {
            if (r != null && r.material != null && r.material.HasProperty("_BaseColor"))
            {
                r.material.SetColor("_BaseColor", new Color(0.8f, 0.2f, 0.2f));
            }
        }
    }

    private void OnDeath()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (visualIndicators != null)
        {
            visualIndicators.enabled = false;
        }

        // Notify Spawner if present
        EnemySpawner.OnEnemyKilled();

        Destroy(gameObject, 0.5f);
    }

    #endregion

    #region Scene Gizmos

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        Vector3 eyePos = center + Vector3.up * eyeHeight;

        // 1. Red Proximity circle
        Gizmos.color = isProximityTriggered ? Color.red : new Color(1f, 0f, 0f, 0.35f);
        Gizmos.DrawWireSphere(center, proximityRadius);

        // 2. Blue Vision Cone lines
        Gizmos.color = isVisionConeTriggered ? Color.cyan : new Color(0f, 0.5f, 1f, 0.35f);
        Vector3 leftRay = Quaternion.Euler(0, -visionAngle * 0.5f, 0) * transform.forward * visionRadius;
        Vector3 rightRay = Quaternion.Euler(0, visionAngle * 0.5f, 0) * transform.forward * visionRadius;
        Gizmos.DrawRay(eyePos, leftRay);
        Gizmos.DrawRay(eyePos, rightRay);
        Gizmos.DrawRay(eyePos, transform.forward * visionRadius);

        // 3. Yellow Sight Through line
        if (hasSightThrough && currentTargetPlayer != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(eyePos, currentTargetPlayer.position + Vector3.up);
        }
    }

    #endregion
}
