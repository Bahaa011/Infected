using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 20f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private float destroyDistance = 100f;

    // Tracer settings
    [SerializeField] private bool useTracer = true;
    [SerializeField] private Material tracerMaterial;
    [SerializeField] private float tracerWidth = 0.1f;
    [SerializeField] private float tracerLength = 2f;

    private Vector3 direction;
    private Rigidbody rb;
    private float spawnTime;
    private Vector3 spawnPosition;
    private LineRenderer tracerLine;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        spawnTime = Time.time;
        spawnPosition = transform.position;

        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;

        // Setup tracer line renderer
        if (useTracer)
        {
            SetupTracer();
        }

        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
        }
    }

    void Update()
    {
        // Destroy bullet if it exceeds lifetime
        if (Time.time - spawnTime > lifetime)
        {
            Destroy(gameObject);
        }

        // Destroy after traveling too far from where this bullet was fired.
        if (Vector3.Distance(transform.position, spawnPosition) > destroyDistance)
        {
            Destroy(gameObject);
        }

        // Update position if no rigidbody
        if (rb == null)
        {
            transform.position += direction * speed * Time.deltaTime;
        }

        // Update tracer line
        if (useTracer && tracerLine != null)
        {
            UpdateTracerLine();
        }
    }

    public void Initialize(Vector3 shootDirection)
    {
        direction = shootDirection.sqrMagnitude > 0.0001f ? shootDirection.normalized : transform.forward;
        spawnPosition = transform.position;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    void OnTriggerEnter(Collider collision)
    {
        // Handle collision with enemies
        if (collision.CompareTag("Enemy"))
        {
            Debug.Log($"Bullet hit enemy: {collision.gameObject.name}");
            
            // Try to damage the zombie
            Zombie zombie = collision.GetComponent<Zombie>();
            if (zombie != null)
            {
                zombie.OnBulletHit(damage);
                CrosshairUIToolkit.RegisterZombieHit();
            }
            
            Destroy(gameObject);
        }
        // Don't collide with bullet source
        else if (collision.CompareTag("Player"))
        {
            // Do nothing - pass through player
        }
    }

    void SetupTracer()
    {
        // Create a child game object for the line renderer
        GameObject tracerObject = new GameObject("Tracer");
        tracerObject.transform.SetParent(transform, false);
        tracerObject.transform.localPosition = Vector3.zero;

        // Add LineRenderer component
        tracerLine = tracerObject.AddComponent<LineRenderer>();
        tracerLine.positionCount = 2;
        tracerLine.startWidth = tracerWidth;
        tracerLine.endWidth = tracerWidth;
        tracerLine.useWorldSpace = true;
        tracerLine.alignment = LineAlignment.View;
        tracerLine.numCapVertices = 2;

        // Set material
        if (tracerMaterial != null)
        {
            tracerLine.material = tracerMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                Material material = new Material(shader);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", new Color(1f, 0.75f, 0.05f, 1f));
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", new Color(1f, 0.75f, 0.05f, 1f));
                tracerLine.material = material;
            }
        }

        // Set colors (white with fade)
        tracerLine.startColor = new Color(1, 1, 0, 1); // Yellow start
        tracerLine.endColor = new Color(1, 0.5f, 0, 0.5f); // Orange fade
    }

    void UpdateTracerLine()
    {
        if (tracerLine == null)
            return;

        // Position 1: Bullet current position
        tracerLine.SetPosition(0, transform.position);

        // Position 2: Behind bullet (tracer trail)
        Vector3 trailStart = transform.position - direction * tracerLength;
        tracerLine.SetPosition(1, trailStart);
    }
}
