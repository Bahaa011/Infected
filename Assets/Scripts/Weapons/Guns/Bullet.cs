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
    private LineRenderer tracerLine;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        spawnTime = Time.time;

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

        // Destroy if too far from origin
        if (Vector3.Distance(transform.position, Vector3.zero) > destroyDistance)
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
        direction = shootDirection.normalized;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    void OnTriggerEnter(Collider collision)
    {
        // Handle collision with enemies
        if (collision.CompareTag("Enemy"))
        {
            Debug.Log($"Bullet hit enemy: {collision.gameObject.name}");
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
        tracerObject.transform.SetParent(transform);
        tracerObject.transform.localPosition = Vector3.zero;

        // Add LineRenderer component
        tracerLine = tracerObject.AddComponent<LineRenderer>();
        tracerLine.positionCount = 2;
        tracerLine.startWidth = tracerWidth;
        tracerLine.endWidth = tracerWidth;

        // Set material
        if (tracerMaterial != null)
        {
            tracerLine.material = tracerMaterial;
        }
        else
        {
            // Default material with glow
            tracerLine.material = new Material(Shader.Find("Sprites/Default"));
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
