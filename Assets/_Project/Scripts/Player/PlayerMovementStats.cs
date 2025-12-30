using UnityEngine;

[CreateAssetMenu(menuName = "Player Movement")]
public class PlayerMovementStats : ScriptableObject
{
    [field: Header("Walk")]
    [field: SerializeField, Range(0f, 1f)] public float MoveThreshold { get; private set; } = 0.25f;
    [field: SerializeField, Range(1f, 100f)] public float MaxWalkSpeed { get; private set; } = 12.5f;
    [field: SerializeField, Range(0.25f, 50f)] public float GroundAcceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float GroundDeceleration { get; private set; } = 20f;
    [field: SerializeField, Range(0.25f, 50f)] public float AirAcceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float AirdDeceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float WallJumpMoveAcceleration { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float WallJumpMoveDeceleration { get; private set; } = 5f;

    [field: Header("Run")]
    [field: SerializeField, Range(1f, 100f)] public float MaxRunSpeed { get; private set; } = 20f;

    [field: Header("Ground/Collision Checks")]
    [field: SerializeField] public LayerMask GroundLayer { get; private set; }

    [field: Header("Head Bump Slide")]
    [field: SerializeField] public bool UseHeadBumpSlide { get; private set; } = true;
    [field: SerializeField, Range(1f, 50f)] public float HeadBumpSlideSpeed { get; private set; } = 13f;
    [field: SerializeField, Range(0.01f, 1f)] public float HeadBumpBoxWidth { get; private set; } = 0.3f;
    [field: SerializeField, Range(0.01f, 1f)] public float HeadBumpBoxHeight { get; private set; } = 0.1f;

    [field: Header("Jump")]
    [field: SerializeField] public float JumpHeight { get; private set; } = 6.5f;
    [field: SerializeField, Range(1f, 1.1f)] public float JumpHeightCompensationFactor { get; private set; } = 1.054f;
    [field: SerializeField] public float TimeUntilJumpApex { get; private set; } = 0.35f;
    [field: SerializeField, Range(0.01f, 5f)] public float GravityOnReleaseMultiplier { get; private set; } = 2f;
    [field: SerializeField] public float MaxFallSpeed { get; private set; } = 26f;
    [field: SerializeField, Range(0, 5)] public float NumOfAirJumpsAllowed { get; private set; } = 1;

    [field: Header("Reset Jump Options")]
    [field: SerializeField] public bool ResetJumpsOnWallSlide { get; private set; } = true;

    [field: Header("Jump Cut")]
    [field: SerializeField, Range(0.02f, 0.3f)] public float TimeForUpwardsCancel { get; private set; } = 0.027f;

    [field: Header("Jump Apex")]
    [field: SerializeField, Range(0.5f, 1f)] public float ApexThreshold { get; private set; } = 0.97f;
    [field: SerializeField, Range(0.01f, 1f)] public float ApexHangTime { get; private set; } = 0.075f;

    [field: Header("Jump Buffer")]
    [field: SerializeField, Range(0f, 1f)] public float JumpBufferTime { get; private set; } = 0.125f;

    [field: Header("Jump Coyote Time")]
    [field: SerializeField, Range(0f, 1f)] public float JumpCoyoteTime { get; private set; } = 0.1f;

    [field: Header("Wall Slide")]
    [field: SerializeField, Min(0.01f)] public float WallSlideSpeed { get; private set; } = 5f;
    [field: SerializeField, Range(0.25f, 50f)] public float WallSlideDecelerationSpeed { get; private set; } = 50f;

    [field: Header("Wall Jump")]
    [field: SerializeField] public Vector2 WallJumpDirection { get; private set; } = new Vector2(-20f, 6.5f);
    [field: SerializeField, Range(0f, 1f)] public float WallJumpPostBufferTime { get; private set; } = 0.125f;
    [field: SerializeField, Range(0.01f, 5f)] public float WallJumpGravityOnReleaseMultiplier { get; private set; } = 1f;

    [field: Header("Dash")]
    [field: SerializeField, Range(0f, 1f)] public float DashTime { get; private set; } = 0.11f;
    [field: SerializeField, Range(1f, 200f)] public float DashSpeed { get; private set; } = 40f;
    [field: SerializeField, Range(0f, 1f)] public float TimeBetweenDashesOnGround { get; private set; } = 0.225f;
    [field: SerializeField] public bool ResetDashOnWallSlide { get; private set; } = true;
    [field: SerializeField, Range(0, 5)] public int NumOfDashes { get; private set; } = 2;
    [field: SerializeField, Range(0f, 0.5f)] public float DashDiagonallyBias { get; private set; } = 0.4f;
    [field: SerializeField, Range(0f, 1f)] public float DashBufferTime { get; private set; } = 0.125f;
    [field: SerializeField] public float DashTargetApexHeight { get; private set; }

    [field: Header("Dash Cancel Time")]
    [field: SerializeField, Range(0.01f, 5f)] public float DashGravityOnReleaseMultiplier { get; private set; } = 1f;
    [field: SerializeField, Range(0.02f, 0.3f)] public float DashTimeForUpwardsCancel { get; private set; } = 0.027f;

    [field: Header("Debug")]
    [field: SerializeField] public bool DebugShowIsGrounded { get; private set; }
    [field: SerializeField] public bool DebugShowHeadRays { get; private set; }
    [field: SerializeField] public bool DebugShowWallHit { get; private set; }
    [field: SerializeField] public bool DebugShowHeadBumpBox { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float ExtraRayDebugDistance { get; private set; } = 0.25f;

    [field: Header("Jump Visualization Tool")]
    [field: SerializeField] public bool ShowWalkJumpArc { get; private set; } = false;
    [field: SerializeField] public bool ShowRunJumpArc { get; private set; } = false;
    [field: SerializeField] public bool StopOnCollision { get; private set; } = true;
    [field: SerializeField] public bool DrawRight { get; private set; } = true;
    [field: SerializeField, Range(5, 100)] public int ArcResolution { get; private set; } = 20;
    [field: SerializeField, Range(0, 100)] public int VisualizationSteps { get; private set; } = 90;

    public readonly Vector2[] DashDirections = new Vector2[]
    {
        new(0, 0),   // Nothing
        new(1, 0),   // Right
        new Vector2(1, 1).normalized,   // Up-Right
        new(0, 1),   // Up
        new Vector2(-1, 1).normalized,  // Up-Left
        new(-1, 0),  // Left
        new Vector2(-1, -1).normalized, // Down-Left
        new(0, -1),  // Down
        new Vector2(1, -1).normalized   // Down-Right
    };

    [field: Header("Gravity")]
    [field: SerializeField] public float Gravity { get; private set; }
    [field: SerializeField] public float InitialJumpVelocity { get; private set; }
    [field: SerializeField] public float AdjustedJumpHeight { get; private set; }

    [field: Header("Wall Jump Gravity")]
    [field: SerializeField] public float WallJumpGravity { get; private set; }
    [field: SerializeField] public float InitialWallJumpVelocity { get; private set; }
    [field: SerializeField] public float AdjustedWallJumpHeight { get; private set; }

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
        // JUMP
        AdjustedJumpHeight = JumpHeight * JumpHeightCompensationFactor;
        Gravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeUntilJumpApex, 2f);
        InitialJumpVelocity = Mathf.Abs(Gravity) * TimeUntilJumpApex;

        // WALL JUMP
        AdjustedWallJumpHeight = WallJumpDirection.y * JumpHeightCompensationFactor;
        WallJumpGravity = -(2f * AdjustedWallJumpHeight) / Mathf.Pow(TimeUntilJumpApex, 2f);
        InitialWallJumpVelocity = Mathf.Abs(WallJumpGravity) * TimeUntilJumpApex;

        // DASH
        float step = Time.fixedDeltaTime;
        float dashTimeRounded = Mathf.Ceil(DashTime / step) * step;
        float dashCancelTimeRounded = Mathf.Ceil(DashTimeForUpwardsCancel / step) * step;

        float dashConstantPhaseHeight = DashSpeed * dashTimeRounded;
        float dashCancelPhaseHeight = 0.5f * DashSpeed * dashCancelTimeRounded;
        DashTargetApexHeight = dashConstantPhaseHeight + dashCancelPhaseHeight;
    }
}