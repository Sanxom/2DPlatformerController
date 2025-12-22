using UnityEngine;

[CreateAssetMenu(menuName = "Player Movement")]
public class PlayerMovementStats : ScriptableObject
{
    [field: Header("Walk")]
    [field: SerializeField, Range(1f, 100f)] public float MaxWalkSpeed { get; private set; } = 12.5f;
    [field: SerializeField, Range(0.25f, 50f)] public float GroundAcceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float GroundDeceleration { get; private set; } = 20f;
    [field: SerializeField, Range(0.25f, 50f)] public float AirAcceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float AirdDeceleration { get; private set; } = 5f;

    [field: Header("Run")]
    [field: SerializeField, Range(1f, 100f)] public float MaxRunSpeed { get; private set; } = 20f;

    [field: Header("Ground/Collision Checks")]
    [field: SerializeField] public LayerMask GroundLayer { get; private set; }
    [field: SerializeField] public float GroundDetectionRayLength { get; private set; } = 0.02f;
    [field: SerializeField] public float HeadDetectionRayLength { get; private set; } = 0.02f;
    [field: SerializeField, Range(0f, 1f)] public float HeadWidth { get; private set; } = 0.75f;

    [field: Header("Jump")]
    [field: SerializeField] public float JumpHeight { get; private set; } = 6.5f;
    [field: SerializeField, Range(1f, 1.1f)] public float JumpHeightCompensationFactor { get; private set; } = 1.054f;
    [field: SerializeField] public float TimeUntilJumpApex { get; private set; } = 0.35f;
    [field: SerializeField, Range(0.01f, 5f)] public float GravityOnReleaseMultiplier { get; private set; } = 2f;
    [field: SerializeField] public float MaxFallSpeed { get; private set; } = 26f;
    [field: SerializeField, Range(1, 5)] public float NumOfJumpsAllowed { get; private set; } = 2;

    [field: Header("Jump Cut")]
    [field: SerializeField, Range(0.02f, 0.3f)] public float TimeForUpwardsCancel { get; private set; } = 0.027f;

    [field: Header("Jump Apex")]
    [field: SerializeField, Range(0.5f, 1f)] public float ApexThreshold { get; private set; } = 0.97f;
    [field: SerializeField, Range(0.01f, 1f)] public float ApexHangTime { get; private set; } = 0.075f;

    [field: Header("Jump Buffer")]
    [field: SerializeField, Range(0f, 1f)] public float JumpBufferTime { get; private set; } = 0.125f;

    [field: Header("Jump Coyote Time")]
    [field: SerializeField, Range(0f, 1f)] public float JumpCoyoteTime { get; private set; } = 0.1f;

    [field: Header("Debug")]
    [field: SerializeField] public bool DebugShowIsGroundedBox { get; private set; }
    [field: SerializeField] public bool DebugShowHeadBumpBox { get; private set; }

    [field: Header("Jump Visualization Tool")]
    [field: SerializeField] public bool ShowWalkJumpArc { get; private set; } = false;
    [field: SerializeField] public bool ShowRunJumpArc { get; private set; } = false;
    [field: SerializeField] public bool StopOnCollision { get; private set; } = true;
    [field: SerializeField] public bool DrawRight { get; private set; } = true;
    [field: SerializeField, Range(5, 100)] public int ArcResolution { get; private set; } = 20;
    [field: SerializeField, Range(0, 100)] public int VisualizationSteps { get; private set; } = 90;

    [field: Header("Gravity")]
    [field: SerializeField] public float Gravity { get; private set; }
    [field: SerializeField] public float InitialJumpVelocity { get; private set; }
    [field: SerializeField] public float AdjustedJumpHeight { get; private set; }

    private void OnValidate()
    {
        CalculateValues();
    }

    private void OnEnable()
    {
        CalculateValues();
    }

    public void CalculateValues()
    {
        AdjustedJumpHeight = JumpHeight * JumpHeightCompensationFactor;
        Gravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeUntilJumpApex, 2f);
        InitialJumpVelocity = Mathf.Abs(Gravity) * TimeUntilJumpApex;
    }
}