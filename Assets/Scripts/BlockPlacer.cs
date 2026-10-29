using UnityEngine;
using UnityEngine.InputSystem;

// Inputs are enabled only by PrototypeGameManager during placement turns.
public class BlockPlacer : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float surfaceGap = 0.03f;

    private PrototypeGameManager manager;
    private Camera placementCamera;
    private Collider groundCollider;
    private GameObject currentPrefab;
    private GameObject preview;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewColor;
    private Vector3 halfSize;
    private Vector3 centerOffset;
    private float yaw;
    private bool upright;
    private float nextPlacementTime;

    public void Initialize(PrototypeGameManager owner, Camera camera, Collider ground)
    {
        manager = owner;
        placementCamera = camera;
        groundCollider = ground;
        previewColor = new MaterialPropertyBlock();
    }

    public void BeginPlacement(GameObject prefab)
    {
        EndPlacement();
        currentPrefab = prefab;
        yaw = 0f;
        upright = false;
        nextPlacementTime = 0f;
        BoxCollider box = prefab.GetComponent<BoxCollider>();
        Vector3 scale = prefab.transform.localScale;
        halfSize = Vector3.Scale(box.size, scale) * 0.5f;
        centerOffset = Vector3.Scale(box.center, scale);
        preview = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        preview.name = "Placement Preview";
        foreach (Rigidbody rb in preview.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;
        }
        foreach (Collider col in preview.GetComponentsInChildren<Collider>())
            col.enabled = false;
        previewRenderers = preview.GetComponentsInChildren<Renderer>();
        preview.SetActive(false);
    }

    public void EndPlacement()
    {
        if (preview != null)
        {
            preview.SetActive(false);
            Destroy(preview);
            preview = null;
        }
    }

    private void Update()
    {
        if (preview == null || manager == null) return;
        Mouse mouse = Mouse.current;
        if (!manager.CanPlace || mouse == null)
        {
            preview.SetActive(false);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame) yaw = (yaw + 90f) % 360f;
            if (keyboard.tKey.wasPressedThisFrame) upright = !upright;
        }

        Vector2 pointer = mouse.position.ReadValue();
        if (!placementCamera.pixelRect.Contains(pointer) || manager.PointerOverHUD(pointer))
        {
            preview.SetActive(false);
            return;
        }
        Ray ray = placementCamera.ScreenPointToRay(pointer);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore) || hit.collider == groundCollider || hit.normal.y < 0.7f)
        {
            preview.SetActive(false);
            return;
        }

        PrototypeBlock support = hit.collider.GetComponentInParent<PrototypeBlock>();
        if (support != null && support.HasFallen)
        {
            preview.SetActive(false);
            return;
        }

        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f)
            * Quaternion.Euler(0f, 0f, upright ? 90f : 0f);
        float verticalExtent =
            Mathf.Abs((rotation * Vector3.right).y) * halfSize.x +
            Mathf.Abs((rotation * Vector3.up).y) * halfSize.y +
            Mathf.Abs((rotation * Vector3.forward).y) * halfSize.z;
        Vector3 boxCenter = hit.point + Vector3.up * (verticalExtent + surfaceGap);
        Vector3 position = boxCenter - rotation * centerOffset;
        preview.transform.SetPositionAndRotation(position, rotation);
        preview.SetActive(true);

        bool blocked = Physics.CheckBox(boxCenter, halfSize * 0.995f, rotation,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        Color color = blocked ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 1f, 0.35f);
        previewColor.SetColor("_BaseColor", color);
        previewColor.SetColor("_Color", color);
        foreach (Renderer renderer in previewRenderers)
            renderer.SetPropertyBlock(previewColor);

        if (!blocked && mouse.leftButton.wasPressedThisFrame && Time.time >= nextPlacementTime)
        {
            GameObject placed = Instantiate(currentPrefab, position, rotation);
            if (!manager.TryRegisterPlacement(placed))
            {
                Destroy(placed);
                return;
            }
            Rigidbody rb = placed.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.WakeUp();
            Physics.SyncTransforms();
            nextPlacementTime = Time.time + 0.35f;
        }
    }

    private void OnDisable()
    {
        if (preview != null) preview.SetActive(false);
    }

    private void OnDestroy() { EndPlacement(); }
}
