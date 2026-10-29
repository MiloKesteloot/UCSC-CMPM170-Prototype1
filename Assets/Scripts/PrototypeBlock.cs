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
        ApplyOwnerColor();
    }

    // Monuments take a tint of the placing player's color; obelisks are always black.
    private void ApplyOwnerColor()
    {
        MaterialPropertyBlock props = new MaterialPropertyBlock();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            Color baseColor = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")
                ? renderer.sharedMaterial.GetColor("_BaseColor") : Color.grey;
            Color color = IsObelisk ? PrototypeGameManager.ObeliskColor
                : Color.Lerp(baseColor, PrototypeGameManager.PlayerColor(PlacedBy), PrototypeGameManager.OwnerTint);
            props.SetColor("_BaseColor", color);
            props.SetColor("_Color", color);
            renderer.SetPropertyBlock(props);
        }
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
