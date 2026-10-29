using UnityEngine;
using UnityEngine.InputSystem;

// Updated from the standalone test
public class TestShooter : MonoBehaviour
{
    [SerializeField, Min(1f)] private float shotSpeed = 60f;
    [SerializeField, Min(0.5f)] private float projectileLifetime = 3f;
    [Header("Crosshair")]
    [SerializeField] private Color crosshairColor = new Color(1f, 0.15f, 0.15f, 0.9f);
    [SerializeField, Min(4f)] private float crosshairRadius = 12f;
    private PrototypeGameManager manager;
    private Camera shootingCamera;
    private Rigidbody projectilePrefab;
    private Rigidbody liveProjectile;
    private Texture2D ringTexture;

    public bool HasLiveProjectile => liveProjectile != null;

    public void Initialize(PrototypeGameManager owner, Camera camera, Rigidbody prefab)
    {
        manager = owner;
        shootingCamera = camera;
        projectilePrefab = prefab;
    }

    private void Update()
    {
        Cursor.visible = !CrosshairVisible(out _);
        if (manager == null || !manager.CanShoot) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        Vector2 pointer = mouse.position.ReadValue();
        if (!shootingCamera.pixelRect.Contains(pointer) || manager.PointerOverHUD(pointer)) return;
        if (!manager.TryUseShot()) return;

        Ray ray = shootingCamera.ScreenPointToRay(pointer);
        liveProjectile = Instantiate(projectilePrefab,
            ray.origin + ray.direction * 0.75f, Quaternion.identity);
        liveProjectile.isKinematic = false;
        liveProjectile.useGravity = true;
        liveProjectile.detectCollisions = true;
        liveProjectile.interpolation = RigidbodyInterpolation.Interpolate;
        liveProjectile.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        liveProjectile.linearVelocity = ray.direction * shotSpeed;
        Destroy(liveProjectile.gameObject, projectileLifetime);
    }

    // Shown for the whole shooting turn so the mode is obvious, hidden over the HUD.
    private bool CrosshairVisible(out Vector2 pointer)
    {
        pointer = Vector2.zero;
        if (manager == null || shootingCamera == null || Mouse.current == null || !manager.TurnActive
            || manager.Phase != PrototypeGameManager.GamePhase.Shooting) return false;
        pointer = Mouse.current.position.ReadValue();
        return shootingCamera.pixelRect.Contains(pointer) && !manager.PointerOverHUD(pointer);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !CrosshairVisible(out Vector2 pointer)) return;
        if (ringTexture == null) ringTexture = CreateRingTexture(64, 4.5f);

        // Mouse position is bottom-left origin; GUI is top-left.
        Vector2 center = new Vector2(pointer.x, Screen.height - pointer.y);
        float r = crosshairRadius;
        Color previous = GUI.color;
        GUI.color = crosshairColor;
        GUI.DrawTexture(new Rect(center.x - r, center.y - r, r * 2f, r * 2f), ringTexture);

        const float thickness = 2f;
        float inner = r * 0.45f;
        float outer = r + 8f;
        Texture line = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(center.x - thickness * 0.5f, center.y - outer, thickness, outer - inner), line);
        GUI.DrawTexture(new Rect(center.x - thickness * 0.5f, center.y + inner, thickness, outer - inner), line);
        GUI.DrawTexture(new Rect(center.x - outer, center.y - thickness * 0.5f, outer - inner, thickness), line);
        GUI.DrawTexture(new Rect(center.x + inner, center.y - thickness * 0.5f, outer - inner, thickness), line);
        GUI.color = previous;
    }

    private static Texture2D CreateRingTexture(int size, float thickness)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[size * size];
        Vector2 middle = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f - thickness;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float edge = Mathf.Abs(Vector2.Distance(new Vector2(x, y), middle) - radius);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(thickness * 0.5f + 0.5f - edge));
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private void OnDisable() { Cursor.visible = true; }

    private void OnDestroy()
    {
        Cursor.visible = true;
        if (ringTexture != null) Destroy(ringTexture);
        if (liveProjectile != null) Destroy(liveProjectile.gameObject);
    }
}
