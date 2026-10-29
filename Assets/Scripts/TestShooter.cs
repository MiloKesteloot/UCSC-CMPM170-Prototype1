using UnityEngine;
using UnityEngine.InputSystem;

// Updated from the standalone test
public class TestShooter : MonoBehaviour
{
    [SerializeField, Min(1f)] private float shotSpeed = 30f;
    [SerializeField, Min(0.5f)] private float projectileLifetime = 3f;
    private PrototypeGameManager manager;
    private Camera shootingCamera;
    private Rigidbody projectilePrefab;
    private Rigidbody liveProjectile;

    public bool HasLiveProjectile => liveProjectile != null;

    public void Initialize(PrototypeGameManager owner, Camera camera, Rigidbody prefab)
    {
        manager = owner;
        shootingCamera = camera;
        projectilePrefab = prefab;
    }

    private void Update()
    {
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

    private void OnDestroy()
    {
        if (liveProjectile != null) Destroy(liveProjectile.gameObject);
    }
}
