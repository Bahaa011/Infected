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
    private float lastAttackTime = 0f;
    private Coroutine attackRoutine;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private Transform playerTransform;
    private bool isChasing = false;
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
            navMeshAgent.stoppingDistance = attackRange;
        }
    }

    private void Update()
    {
        if (isDead || playerTransform == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // Check if player is in sight range
        if (distanceToPlayer <= sightRange)
        {
            isChasing = true;
            ChasePlayer();

            // Try to attack if in range
            if (distanceToPlayer <= attackRange)
                AttackPlayer();
        }
        else
        {
            isChasing = false;
            Wander();
        }

        // Update animator
        if (animator != null)
        {
            animator.SetBool("isChasing", isChasing);
            float speed = navMeshAgent.velocity.magnitude;
            animator.SetFloat("speed", speed);
        }
    }

    private void ChasePlayer()
    {
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.speed = chaseSpeed;
            navMeshAgent.SetDestination(playerTransform.position);
            
            // Face the player
            Vector3 directionToPlayer = (playerTransform.position - transform.position).normalized;
            transform.rotation = Quaternion.LookRotation(directionToPlayer);
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

    private IEnumerator AttackAfterWindup()
    {
        lastAttackTime = Time.time; // start cooldown when attack is declared

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
                attackRoutine = null;
                yield break;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > attackRange)
            {
                // Player moved away; attack fails
                if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
                    navMeshAgent.isStopped = wasStopped;
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
            if (distanceToPlayer <= attackRange)
            {
                Player player = playerTransform.GetComponent<Player>();
                if (player != null)
                {
                    player.TakeDamageWithInjury(attackDamage);

                    if (animator != null)
                        animator.SetTrigger("attack");

                    Debug.Log($"Zombie attacking player for {attackDamage} damage!");
                }
            }
        }

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
        
        // Stop movement
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.enabled = false;
        }

        // Play death animation
        if (animator != null)
        {
            animator.SetBool("isDead", true);
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
