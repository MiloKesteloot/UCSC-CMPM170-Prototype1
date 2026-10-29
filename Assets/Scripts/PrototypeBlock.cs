using UnityEngine;

public class PrototypeBlock : MonoBehaviour
{
    public bool IsObelisk { get; private set; }
    public bool HasFallen { get; private set; }
    public int PlacedBy { get; private set; }
    public Rigidbody Body { get; private set; }
    private PrototypeGameManager manager;
    private Collider ground;
    private float fallHeight;

    // Ground is optional; dropping below fallHeight always counts as fallen.
    public void Initialize(PrototypeGameManager owner, Collider groundCollider, float fallBelowY,
        bool obelisk, int player)
    {
        manager = owner;
        ground = groundCollider;
        fallHeight = fallBelowY;
        IsObelisk = obelisk;
        PlacedBy = player;
        Body = GetComponent<Rigidbody>();
        HasFallen = false;
    }

    private void OnCollisionEnter(Collision collision) { CheckGround(collision); }
    private void OnCollisionStay(Collision collision) { CheckGround(collision); }

    private void CheckGround(Collision collision)
    {
        if (ground != null && collision.collider == ground)
            MarkFallen();
    }

    private void FixedUpdate()
    {
        if (!HasFallen && transform.position.y < fallHeight)
            MarkFallen();
    }

    private void MarkFallen()
    {
        if (HasFallen || manager == null) return;
        HasFallen = true;
        manager.NotifyBlockFell(this);
    }
}
