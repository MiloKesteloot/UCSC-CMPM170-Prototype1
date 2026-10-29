using UnityEngine;
using UnityEngine.InputSystem;

public class PrototypeCamera : MonoBehaviour
{
    [SerializeField] private Vector3 towerCenter = new Vector3(0f, 2f, 0f);
    [SerializeField, Min(1f)] private float distance = 15.6f;
    [SerializeField] private float heightAboveCenter = 7f;
    [SerializeField, Min(1f)] private float orbitSpeed = 65f;

    private PrototypeGameManager manager;
    private Camera viewCamera;
    private float angle;
    private float sectionCenter;
    private float sectionHalfWidth;
    private bool restrictSection;

    public void Initialize(PrototypeGameManager owner, Camera camera)
    {
        manager = owner;
        viewCamera = camera;
    }

    public void BeginTurn(int player, bool shooting)
    {
        float sectionWidth = 360f / manager.PlayerCount;
        sectionCenter = 40f + player * sectionWidth;
        sectionHalfWidth = sectionWidth * 0.5f;
        angle = sectionCenter;
        restrictSection = shooting;
        ApplyPose();
    }

    private void Update()
    {
        if (manager == null || viewCamera == null || !manager.TurnActive) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        float direction = (keyboard.dKey.isPressed ? 1f : 0f)
            - (keyboard.aKey.isPressed ? 1f : 0f);
        angle += direction * orbitSpeed * Time.deltaTime;
        if (restrictSection)
            angle = Mathf.Clamp(angle, sectionCenter - sectionHalfWidth, sectionCenter + sectionHalfWidth);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float radians = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(radians) * distance, heightAboveCenter,
            -Mathf.Cos(radians) * distance);
        viewCamera.transform.position = towerCenter + offset;
        viewCamera.transform.LookAt(towerCenter);
    }
}
