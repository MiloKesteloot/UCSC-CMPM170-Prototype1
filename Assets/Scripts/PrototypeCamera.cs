using UnityEngine;
using UnityEngine.InputSystem;

public class PrototypeCamera : MonoBehaviour
{
    [SerializeField] private Vector3 towerCenter = new Vector3(0f, 2f, 0f);
    [SerializeField, Min(1f)] private float distance = 15.6f;
    [SerializeField] private float heightAboveCenter = 7f;
    [SerializeField, Min(1f)] private float orbitSpeed = 65f;
    [SerializeField, Min(0.01f)] private float glideTime = 0.35f;

    private PrototypeGameManager manager;
    private Camera viewCamera;
    private float angle;
    private float sectionCenter;
    private float sectionHalfWidth;
    private bool restrictSection;
    private bool gliding;
    private float glideTarget;
    private float glideVelocity;

    public void Initialize(PrototypeGameManager owner, Camera camera)
    {
        manager = owner;
        viewCamera = camera;
        angle = 40f;
        ApplyPose();
    }

    // Keeps the current view between turns; shooting turns glide into the player's section.
    public void BeginTurn(int player, bool shooting)
    {
        float sectionWidth = 360f / manager.PlayerCount;
        sectionCenter = 40f + player * sectionWidth;
        sectionHalfWidth = sectionWidth * 0.5f;
        restrictSection = shooting;
        gliding = false;
        if (!shooting) return;

        angle = sectionCenter + Mathf.DeltaAngle(sectionCenter, angle);
        glideTarget = ClampToSection(angle);
        glideVelocity = 0f;
        gliding = !Mathf.Approximately(angle, glideTarget);
    }

    private void Update()
    {
        if (manager == null || viewCamera == null || !manager.TurnActive) return;
        Keyboard keyboard = Keyboard.current;
        float direction = keyboard == null ? 0f : (keyboard.dKey.isPressed ? 1f : 0f)
            - (keyboard.aKey.isPressed ? 1f : 0f);
        float step = direction * orbitSpeed * Time.deltaTime;

        if (gliding)
        {
            glideTarget = ClampToSection(glideTarget + step);
            angle = Mathf.SmoothDamp(angle, glideTarget, ref glideVelocity, glideTime);
            if (Mathf.Abs(angle - glideTarget) < 0.05f)
            {
                angle = glideTarget;
                gliding = false;
            }
        }
        else
        {
            angle += step;
            if (restrictSection) angle = ClampToSection(angle);
        }
        ApplyPose();
    }

    private float ClampToSection(float value)
    {
        return Mathf.Clamp(value, sectionCenter - sectionHalfWidth, sectionCenter + sectionHalfWidth);
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
