using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Zombie : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 30f;
    private float currentHealth;

    [Header("Detection")]
    [SerializeField] private float sightRange = 50f;
    [SerializeField] private float fieldOfViewAngle = 110f;
    [SerializeField] private LayerMask visionObstructionLayers = ~0;
    [SerializeField] private float attackRange = 2f;

    [Header("Stealth Perception")]
    [SerializeField] private float visionBuildPerSecond = 60f;
    [SerializeField] private float visionDecayPerSecond = 12f;
    [SerializeField] private float hearingDecayPerSecond = 14f;
    [SerializeField] private float crouchVisibilityMultiplier = 0.55f;
    [SerializeField] private float sprintVisibilityMultiplier = 1.25f;
    [SerializeField] private float hearingGainScale = 55f;
    [SerializeField] private float investigateThreshold = 15f;
    [SerializeField] private float chaseThreshold = 60f;
    [SerializeField] private float rememberTargetDuration = 5f;

    [Header("Awareness Meters (Runtime)")]
    [SerializeField, Range(0f, 100f)] private float visionMeter = 0f;
    [SerializeField, Range(0f, 100f)] private float hearingMeter = 0f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 5f;
    private NavMeshAgent navMeshAgent;

    [Header("Attack")]
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackWindup = 1f; // time the zombie must stay near the player before damage lands
    [SerializeField] private float attackDistancePadding = 0.4f; // extra spacing so zombie doesn't overlap player
    [Header("Hit Stun")]
    [SerializeField] private float rangedHitStunDuration = 0.1f;
    [SerializeField] private float meleeHitStunDuration = 1f;
    [SerializeField] private bool refreshStunOnHit = true;
    private float lastAttackTime = 0f;
    private Coroutine attackRoutine;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private Transform playerTransform;
    private bool isChasing = false;
    private bool isAttacking = false;
    private bool isDead = false;
    private float stunnedUntilTime = 0f;
    private Vector3 lastKnownTargetPosition;
    private float lastKnownTargetTime = -999f;
    private Player player;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;

        // Find player
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
            player = playerObject.GetComponent<Player>();
        }
        else
        {
            Debug.LogWarning("Player not found! Make sure Player is tagged as 'Player'");
        }
    }

    private void Start()
    {
        if (navMeshAgent != null)
        {
            navMeshAgent.speed = walkSpeed;
            navMeshAgent.stoppingDistance = attackRange + attackDistancePadding;
        }
    }

    private void Update()
    {
        // Update animator movement values every frame
        if (animator != null)
        {
            float velocity = 0f;
            if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
            {
                velocity = navMeshAgent.velocity.magnitude;
            }

            animator.SetFloat("velocity", velocity);
        }

        if (isDead || playerTransform == null) return;

        if (IsStunned())
        {
            isChasing = false;
            isAttacking = false;
            StopAllAttackAndMovement();

            if (animator != null)
            {
                animator.SetBool("isChasing", false);
                animator.SetBool("attack", false);
            }

            return;
        }

        bool canSeePlayer = CanSeePlayer(out float visionGainFactor);
        UpdateAwarenessMeters(canSeePlayer, visionGainFactor);

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        bool isAlerted = IsAlerted();

        if (canSeePlayer)
        {
            lastKnownTargetPosition = playerTransform.position;
            lastKnownTargetTime = Time.time;
        }

        if (isAlerted)
        {
            isChasing = true;
            float attackTriggerRange = GetAttackTriggerRange();

            // Attack only when target is visible and in range
            if (canSeePlayer && distanceToPlayer <= attackTriggerRange)
            {
                StopMovingForAttack();
                AttackPlayer();
            }
            else
            {
                ResumeMovement();

                bool hasRecentKnownPosition = Time.time - lastKnownTargetTime <= rememberTargetDuration;
                if (canSeePlayer)
                    ChasePlayer();
                else if (hasRecentKnownPosition)
                    InvestigateLastKnownPosition();
                else
                    Wander();
            }
        }
        else
        {
            isChasing = false;
            ResumeMovement();
            Wander();
        }

        // Update animator state
        if (animator != null)
        {
            animator.SetBool("isChasing", isChasing);
            animator.SetBool("attack", isAttacking);
        }
    }

    private void UpdateAwarenessMeters(bool canSeePlayer, float visionGainFactor)
    {
        float dt = Time.deltaTime;

        if (canSeePlayer)
        {
            float gain = visionBuildPerSecond * Mathf.Clamp01(visionGainFactor) * dt;
            visionMeter += gain;
        }
        else
        {
            visionMeter -= visionDecayPerSecond * dt;
        }

        hearingMeter -= hearingDecayPerSecond * dt;

        visionMeter = Mathf.Clamp(visionMeter, 0f, 100f);
        hearingMeter = Mathf.Clamp(hearingMeter, 0f, 100f);
    }

    private bool CanSeePlayer(out float visionGainFactor)
    {
        visionGainFactor = 0f;

        if (playerTransform == null)
            return false;

        Vector3 eyePosition = transform.position + Vector3.up * 1.55f;
        Vector3 targetPosition = playerTransform.position + Vector3.up * 1.2f;
        Vector3 toTarget = targetPosition - eyePosition;

        float distance = toTarget.magnitude;
        if (distance <= 0.001f || distance > sightRange)
            return false;

        Vector3 direction = toTarget / distance;
        float angleToTarget = Vector3.Angle(transform.forward, direction);
        if (angleToTarget > fieldOfViewAngle * 0.5f)
            return false;

        if (Physics.Raycast(eyePosition, direction, out RaycastHit hit, distance, visionObstructionLayers, QueryTriggerInteraction.Ignore))
        {
            Transform hitTransform = hit.transform;
            if (hitTransform != playerTransform && !hitTransform.IsChildOf(playerTransform))
                return false;
        }

        float distanceFactor = 1f - Mathf.Clamp01(distance / sightRange);
        float movementVisibility = 1f;

        if (player != null)
        {
            if (player.IsCrouching())
                movementVisibility *= crouchVisibilityMultiplier;
            if (player.IsCurrentlySprinting())
                movementVisibility *= sprintVisibilityMultiplier;
        }

        // Better vision gain: even at max range, there's a decent base gain (0.3 instead of 0.15)
        // This makes detection faster and more consistent at distance
        visionGainFactor = Mathf.Clamp01(distanceFactor * movementVisibility * 0.8f + 0.35f);
        return true;
    }

    private bool IsAlerted()
    {
        return visionMeter >= investigateThreshold || hearingMeter >= investigateThreshold;
    }

    private void InvestigateLastKnownPosition()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.speed = walkSpeed;
            navMeshAgent.SetDestination(lastKnownTargetPosition);
        }
    }

    private void ChasePlayer()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.speed = chaseSpeed;
            navMeshAgent.SetDestination(playerTransform.position);
            
            // Face the player only on horizontal plane (avoid pitch/roll tipping)
            Vector3 directionToPlayer = playerTransform.position - transform.position;
            directionToPlayer.y = 0f;
            if (directionToPlayer.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer.normalized);
                transform.rotation = targetRotation;
            }
        }
    }

    private void Wander()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.speed = walkSpeed;
            
            // Keep agent moving if no destination or reached it
            if (!navMeshAgent.hasPath || navMeshAgent.remainingDistance < 0.5f)
            {
                Vector3 randomDirection = Random.insideUnitSphere * 10f;
                randomDirection += transform.position;
                
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomDirection, out hit, 10f, NavMesh.AllAreas))
                {
                    navMeshAgent.SetDestination(hit.position);
                }
            }
        }
    }

    private void AttackPlayer()
    {
        // Respect cooldown and avoid stacking windups
        if (Time.time - lastAttackTime < attackCooldown)
            return;
        if (attackRoutine != null)
            return;

        attackRoutine = StartCoroutine(AttackAfterWindup());
    }

    private float GetAttackTriggerRange()
    {
        if (navMeshAgent != null)
            return Mathf.Max(attackRange, navMeshAgent.stoppingDistance);

        return attackRange;
    }

    private void StopMovingForAttack()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }
    }

    private void ResumeMovement()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = false;
        }
    }

    private IEnumerator AttackAfterWindup()
    {
        lastAttackTime = Time.time; // start cooldown when attack is declared
        isAttacking = true;

        // Optional: pause movement during wind-up to mimic bite/swing prep
        bool wasStopped = false;
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            wasStopped = navMeshAgent.isStopped;
            navMeshAgent.isStopped = true;
        }

        float start = Time.time;
        while (Time.time - start < attackWindup)
        {
            if (isDead || playerTransform == null)
            {
                if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
                    navMeshAgent.isStopped = wasStopped;
                isAttacking = false;
                attackRoutine = null;
                yield break;
            }

            if (IsStunned())
            {
                if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
                    navMeshAgent.isStopped = true;
                isAttacking = false;
                attackRoutine = null;
                yield break;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > GetAttackTriggerRange())
            {
                // Player moved away; attack fails
                if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
                    navMeshAgent.isStopped = wasStopped;
                isAttacking = false;
                attackRoutine = null;
                yield break;
            }

            yield return null;
        }

        // Resume movement state
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = wasStopped;
        }

        // Final validation before dealing damage
        if (!isDead && playerTransform != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer <= GetAttackTriggerRange())
            {
                Player player = playerTransform.GetComponent<Player>();
                if (player != null)
                {
                    player.TakeDamageWithInjury(attackDamage);

                    Debug.Log($"Zombie attacking player for {attackDamage} damage!");
                }
            }
        }

        isAttacking = false;
        attackRoutine = null;
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        ApplyHitStun(rangedHitStunDuration);
        visionMeter = Mathf.Max(visionMeter, chaseThreshold);
        hearingMeter = Mathf.Max(hearingMeter, chaseThreshold);
        if (playerTransform != null)
        {
            lastKnownTargetPosition = playerTransform.position;
            lastKnownTargetTime = Time.time;
        }
        
        // Play hit animation
        if (animator != null)
        {
            animator.SetTrigger("hit");
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        isAttacking = false;

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        
        // Stop movement
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.enabled = false;
        }

        // Play death animation
        if (animator != null)
        {
            animator.SetBool("dead", true);
        }

        // Disable colliders
        Collider[] colliders = GetComponents<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        // Destroy zombie after animation
        Destroy(gameObject, 2f);

        Debug.Log("Zombie died!");
    }

    // Call this from the Bullet script when it hits
    public void OnBulletHit(float bulletDamage)
    {
        TakeDamageWithStun(bulletDamage, rangedHitStunDuration);
    }

    // Call this from melee hits
    public void OnMeleeHit(float meleeDamage)
    {
        TakeDamageWithStun(meleeDamage, meleeHitStunDuration);
    }

    public void RegisterNoise(Vector3 noisePosition, float radius, float strength, ZombieNoiseSystem.NoiseType noiseType)
    {
        if (isDead)
            return;

        float distance = Vector3.Distance(transform.position, noisePosition);
        if (distance > radius)
            return;

        float distanceFalloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, radius));

        float occlusionFactor = 1f;
        Vector3 earPosition = transform.position + Vector3.up * 1.5f;
        Vector3 toNoise = noisePosition - earPosition;
        float noiseDistance = toNoise.magnitude;
        if (noiseDistance > 0.01f)
        {
            Vector3 noiseDirection = toNoise / noiseDistance;
            if (Physics.Raycast(earPosition, noiseDirection, out RaycastHit hit, noiseDistance, visionObstructionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform != null && hit.transform != playerTransform && !hit.transform.IsChildOf(playerTransform))
                    occlusionFactor = 0.55f;
            }
        }

        float typeMultiplier = noiseType == ZombieNoiseSystem.NoiseType.Gunshot ? 1.25f : 1f;
        float awarenessGain = hearingGainScale * strength * distanceFalloff * occlusionFactor * typeMultiplier;
        hearingMeter = Mathf.Clamp(hearingMeter + awarenessGain, 0f, 100f);

        if (hearingMeter >= investigateThreshold)
        {
            lastKnownTargetPosition = noisePosition;
            lastKnownTargetTime = Time.time;
        }
    }

    private bool IsStunned()
    {
        return Time.time < stunnedUntilTime;
    }

    private void ApplyHitStun(float duration)
    {
        if (duration <= 0f)
            return;

        float targetStunEnd = Time.time + duration;

        if (refreshStunOnHit)
            stunnedUntilTime = Mathf.Max(stunnedUntilTime, targetStunEnd);
        else if (!IsStunned())
            stunnedUntilTime = targetStunEnd;

        StopAllAttackAndMovement();
    }

    private void TakeDamageWithStun(float damage, float stunDuration)
    {
        if (isDead) return;

        currentHealth -= damage;
        ApplyHitStun(stunDuration);
        visionMeter = Mathf.Max(visionMeter, chaseThreshold);
        hearingMeter = Mathf.Max(hearingMeter, chaseThreshold);
        if (playerTransform != null)
        {
            lastKnownTargetPosition = playerTransform.position;
            lastKnownTargetTime = Time.time;
        }

        if (animator != null)
            animator.SetTrigger("hit");

        if (currentHealth <= 0)
            Die();
    }

    private void StopAllAttackAndMovement()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }
    }
}
