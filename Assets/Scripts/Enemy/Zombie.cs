using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Zombie : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 30f;
    private float currentHealth;

    [Header("Detection")]
    [SerializeField] private float sightRange = 20f;
    [SerializeField] private float attackRange = 2f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 5f;
    private NavMeshAgent navMeshAgent;

    [Header("Attack")]
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackWindup = 1f; // time the zombie must stay near the player before damage lands
    [SerializeField] private float attackDistancePadding = 0.4f; // extra spacing so zombie doesn't overlap player
    private float lastAttackTime = 0f;
    private Coroutine attackRoutine;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private Transform playerTransform;
    private bool isChasing = false;
    private bool isAttacking = false;
    private bool isDead = false;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;

        // Find player
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
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
            animator.SetFloat("speed", velocity);
        }

        if (isDead || playerTransform == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // Check if player is in sight range
        if (distanceToPlayer <= sightRange)
        {
            isChasing = true;
            float attackTriggerRange = GetAttackTriggerRange();

            // Try to attack if in range
            if (distanceToPlayer <= attackTriggerRange)
            {
                StopMovingForAttack();
                AttackPlayer();
            }
            else
            {
                ResumeMovement();
                ChasePlayer();
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
        TakeDamage(bulletDamage);
    }
}
