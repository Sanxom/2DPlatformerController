using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [field: SerializeField] public PlayerMovementStats MoveStats { get; private set; }
    [SerializeField] private Collider2D _bodyCollider;
    [SerializeField] private Collider2D _feetCollider;

    private Rigidbody2D _rb;

    [Header("Movement")]
    [field: SerializeField] public float HorizontalVelocity { get; private set; }
    private bool _isFacingRight;

    [Header("Collision Check")]
    private RaycastHit2D _groundHit;
    private RaycastHit2D _headHit;
    private RaycastHit2D _wallHit;
    private RaycastHit2D _lastWallHit;
    private bool _isGrounded;
    private bool _bumpedHead;
    private bool _isTouchingWall;

    [Header("Jump")]
    public float VerticalVelocity { get; private set; }
    private float _fastFallTime;
    private float _fastFallReleaseSpeed;
    private int _numOfJumpsUsed;
    private bool _isJumping;
    private bool _isFastFalling;
    private bool _isFalling;

    [Header("Jump Apex")]
    private float _apexPoint;
    private float _timePastApexThreshold;
    private bool _isPastApexThreshold;

    [Header("Jump Buffer")]
    private float _jumpBufferTimer;
    private bool _jumpReleasedDuringBuffer;

    [Header("Coyote Time")]
    private float _coyoteTimer;

    [Header("Wall Slide")]
    private bool _isWallSliding;
    private bool _isWallSlideFalling;

    [Header("Wall Jump")]
    private float _wallJumpTime;
    private float _wallJumpFastFallTime;
    private float _wallJumpFastFallReleaseSpeed;
    private float _wallJumpPostBufferTimer;
    private float _wallJumpApexPoint;
    private float _timePastWallJumpApexThreshold;
    private bool _useWallJumpMoveStats;
    private bool _isWallJumping;
    private bool _isWallJumpFastFalling;
    private bool _isWallJumpFalling;
    private bool _isPastWallJumpApexThreshold;

    [Header("Dash")]
    private Vector2 _dashDirection;
    private float _dashTimer;
    private float _dashOnGroundTimer;
    private float _dashFastFallTime;
    private float _dashFastFallReleaseSpeed;
    private int _numOfDashesUsed;
    private bool _isDashing;
    private bool _isAirDashing;
    private bool _isDashFastFalling;

    #region Unity Callbacks
    private void Awake()
    {
        _isFacingRight = true;
        _rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        CountTimers();
        JumpChecks();
        LandCheck();
    }

    private void FixedUpdate()
    {
        CollisionChecks();
        Jump();
        Fall();

        if (_isGrounded)
            Move(MoveStats.GroundAcceleration, MoveStats.GroundDeceleration, InputManager.Instance.MoveVector);
        else
            Move(MoveStats.AirAcceleration, MoveStats.AirdDeceleration, InputManager.Instance.MoveVector);

        ApplyVelocity();
    }

    private void OnDrawGizmos()
    {
        if (MoveStats.ShowWalkJumpArc)
            DrawJumpArc(MoveStats.MaxWalkSpeed, Color.white);

        if (MoveStats.ShowRunJumpArc)
            DrawJumpArc(MoveStats.MaxRunSpeed, Color.red);
    }
    #endregion

    private void ApplyVelocity()
    {
        // CLAMP FALL SPEED
        VerticalVelocity = Mathf.Clamp(VerticalVelocity, -MoveStats.MaxFallSpeed, 50f);
        _rb.linearVelocity = new Vector2(HorizontalVelocity, VerticalVelocity);
    }

    #region Movement
    private void Move(float acceleration, float deceleration, Vector2 moveInput)
    {
        if (Mathf.Abs(moveInput.x) >= MoveStats.MoveThreshold)
        {
            TurnCheck(moveInput);

            float targetVelocity;

            if (InputManager.Instance.RunIsHeld)
                targetVelocity = moveInput.x * MoveStats.MaxRunSpeed;
            else
                targetVelocity = moveInput.x * MoveStats.MaxWalkSpeed;

            HorizontalVelocity = Mathf.Lerp(HorizontalVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        }
        else if (Mathf.Abs(moveInput.x) < MoveStats.MoveThreshold)
            HorizontalVelocity = Mathf.Lerp(HorizontalVelocity, 0f, deceleration * Time.fixedDeltaTime);
    }

    private void TurnCheck(Vector2 moveInput)
    {
        if (_isFacingRight && moveInput.x < 0)
            Turn(false);
        else if (!_isFacingRight && moveInput.x > 0)
            Turn(true);
    }

    private void Turn (bool turnRight)
    {
        if (turnRight)
        {
            _isFacingRight = true;
            transform.Rotate(0f, 180f, 0f);
        }
        else
        {
            _isFacingRight = false;
            transform.Rotate(0f, -180f, 0f);
        }
    }
    #endregion

    #region Land/Fall
    private void LandCheck()
    {
        // LANDED
        if ((_isJumping || _isFalling) && _isGrounded && VerticalVelocity <= 0f)
        {
            _isJumping = false;
            _isFalling = false;
            _isFastFalling = false;
            _fastFallTime = 0f;
            _isPastApexThreshold = false;
            _numOfJumpsUsed = 0;
            VerticalVelocity = Physics2D.gravity.y;
        }
    }

    private void Fall()
    {
        // NORMAL GRAVITY WHILE FALLING
        if (!_isGrounded && !_isJumping)
        {
            if (!_isFalling)
                _isFalling = true;

            VerticalVelocity += MoveStats.Gravity * Time.fixedDeltaTime;
        }
    }
    #endregion

    #region Jump
    private void JumpChecks()
    {
        if (InputManager.Instance.JumpWasPressed)
        {
            _jumpBufferTimer = MoveStats.JumpBufferTime;
            _jumpReleasedDuringBuffer = false;
        }

        if (InputManager.Instance.JumpWasReleased)
        {
            if (_jumpBufferTimer > 0f)
                _jumpReleasedDuringBuffer = true;

            if (_isJumping && VerticalVelocity > 0f)
            {
                if (_isPastApexThreshold)
                {
                    _isPastApexThreshold = false;
                    _isFastFalling = true;
                    _fastFallTime = MoveStats.TimeForUpwardsCancel;
                    VerticalVelocity = 0f;
                }
                else
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = VerticalVelocity;
                }
            }
        }

        if (_jumpBufferTimer > 0f)
        {
            // SINGLE JUMP
            if (!_isJumping && (_isGrounded || _coyoteTimer > 0f))
            {
                InitiateJump(1);

                if (_jumpReleasedDuringBuffer)
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = VerticalVelocity;
                }
            }

            // DOUBLE JUMP
            else if (_isJumping && _numOfJumpsUsed < MoveStats.NumOfJumpsAllowed)
            {
                _isFastFalling = false;
                InitiateJump(1);
            }

            // AIR JUMP AFTER COYOTE TIME LAPSED ( TAKE OFF EXTRA JUMP SO WE DON'T GET A BONUS JUMP)
            else if (_isFalling && _numOfJumpsUsed < MoveStats.NumOfJumpsAllowed - 1)
            {
                InitiateJump(2);
                _isFastFalling = false;
            }
        }
    }

    private void InitiateJump(int numOfJumpsUsed)
    {
        if (!_isJumping)
            _isJumping = true;

        _jumpBufferTimer = 0f;
        _numOfJumpsUsed += numOfJumpsUsed;
        VerticalVelocity = MoveStats.InitialJumpVelocity;
    }

    private void Jump()
    {
        // APPLY GRAVITY WHILE JUMPING
        if (_isJumping)
        {
            // CHECK FOR HEAD BUMP
            if (_bumpedHead)
                _isFastFalling = true;

            // GRAVITY ON ASCENDING
            if (VerticalVelocity >= 0f)
            {
                // APEX CONTROLS
                _apexPoint = Mathf.InverseLerp(MoveStats.InitialJumpVelocity, 0f, VerticalVelocity);

                if (_apexPoint > MoveStats.ApexThreshold)
                {
                    if (!_isPastApexThreshold)
                    {
                        _isPastApexThreshold = true;
                        _timePastApexThreshold = 0f;
                    }
                    else
                    {
                        _timePastApexThreshold += Time.fixedDeltaTime;
                        if (_timePastApexThreshold < MoveStats.ApexHangTime)
                            VerticalVelocity = 0f;
                        else
                            VerticalVelocity = -0.01f;
                    }
                }

                // GRAVITY ON ASCENDING BUT NOT PAST APEX THRESHOLD
                else if (!_isFastFalling)
                {
                    VerticalVelocity += MoveStats.Gravity * Time.fixedDeltaTime;
                    if (_isPastApexThreshold)
                        _isPastApexThreshold = false;
                }
            }

            // GRAVITY ON DESCENDING
            else if (!_isFastFalling)
                VerticalVelocity += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * Time.fixedDeltaTime;

            else if (VerticalVelocity < 0f)
            {
                if (!_isFalling)
                    _isFalling = true;
            }
        }

        // JUMP CUT
        if (_isFastFalling)
        {
            if (_fastFallTime >= MoveStats.TimeForUpwardsCancel)
                VerticalVelocity += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * Time.fixedDeltaTime;
            else if (_fastFallTime < MoveStats.TimeForUpwardsCancel)
                VerticalVelocity = Mathf.Lerp(_fastFallReleaseSpeed, 0f, (_fastFallTime / MoveStats.TimeForUpwardsCancel));

            _fastFallTime += Time.fixedDeltaTime;
        }
    }
    #endregion

    #region Wall Slide

    #endregion

    #region Collision Checks
    private void IsGrounded()
    {
        Vector2 boxCastOrigin = new(_feetCollider.bounds.center.x, _feetCollider.bounds.min.y);
        Vector2 boxCastSize = new(_feetCollider.bounds.size.x, MoveStats.GroundDetectionRayLength);
        _groundHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, Vector2.down, MoveStats.GroundDetectionRayLength, MoveStats.GroundLayer);
        if (_groundHit.collider != null)
            _isGrounded = true;
        else
            _isGrounded = false;

        #region Debug Visualization
        if (MoveStats.DebugShowIsGroundedBox)
        {
            Color rayColor;
            if (_isGrounded)
                rayColor = Color.green;
            else
                rayColor = Color.red;

            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x * 0.5f, boxCastOrigin.y), Vector2.down * MoveStats.GroundDetectionRayLength, rayColor);
            Debug.DrawRay(new Vector2(boxCastOrigin.x + boxCastSize.x * 0.5f, boxCastOrigin.y), Vector2.down * MoveStats.GroundDetectionRayLength, rayColor);
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x * 0.5f, boxCastOrigin.y - MoveStats.GroundDetectionRayLength), Vector2.right * boxCastSize.x, rayColor);
        }
        #endregion
    }

    private void BumpedHead()
    {
        Vector2 boxCastOrigin = new(_feetCollider.bounds.center.x, _bodyCollider.bounds.max.y);
        Vector2 boxCastSize = new(_feetCollider.bounds.size.x * MoveStats.HeadWidth, MoveStats.HeadDetectionRayLength);
        _headHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, Vector2.up, MoveStats.HeadDetectionRayLength, MoveStats.GroundLayer);
        if (_headHit.collider != null)
            _bumpedHead = true;
        else
            _bumpedHead = false;

        #region Debug Visualization
        if (MoveStats.DebugShowHeadBumpBox)
        {
            float headWidth = MoveStats.HeadWidth;
            Color rayColor;
            if (_bumpedHead)
                rayColor = Color.green;
            else
                rayColor = Color.red;

            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x * 0.5f * headWidth, boxCastOrigin.y), Vector2.up * MoveStats.HeadDetectionRayLength, rayColor);
            Debug.DrawRay(new Vector2((boxCastOrigin.x + (boxCastSize.x * 0.5f)) * headWidth, boxCastOrigin.y), Vector2.up * MoveStats.HeadDetectionRayLength, rayColor);
            Debug.DrawRay(new Vector2(boxCastOrigin.x - boxCastSize.x * 0.5f * headWidth, boxCastOrigin.y + MoveStats.HeadDetectionRayLength), Vector2.right * boxCastSize.x * headWidth, rayColor);
        }
        #endregion
    }

    private void DrawJumpArc(float moveSpeed, Color gizmoColor)
    {
        Vector2 startPosition = new(_feetCollider.bounds.center.x, _feetCollider.bounds.min.y);
        Vector2 previousPosition = startPosition;
        float speed;
        if (MoveStats.DrawRight)
            speed = moveSpeed;
        else
            speed = -moveSpeed;

        Vector2 velocity = new(speed, MoveStats.InitialJumpVelocity);
        Gizmos.color = gizmoColor;
        float timeStep = 2 * MoveStats.TimeUntilJumpApex / MoveStats.ArcResolution; // Time step for the simulation
        // float totalTime = (2 * MoveStats.TimeUntilJumpApex) + MoveStats.ApexHangTime; // Total time of arc including hang time

        for (int i = 0; i < MoveStats.VisualizationSteps; i++)
        {
            float simulationTime = i * timeStep;
            Vector2 displacement;
            Vector2 drawPoint;

            // ASCENDING
            if (simulationTime < MoveStats.TimeUntilJumpApex)
            {
                displacement = velocity * simulationTime + 0.5f * simulationTime * simulationTime * new Vector2(0, MoveStats.Gravity);
            }

            // APEX HANG TIME
            else if (simulationTime < MoveStats.TimeUntilJumpApex + MoveStats.ApexHangTime)
            {
                float apexTime = simulationTime - MoveStats.TimeUntilJumpApex;
                displacement = velocity * MoveStats.TimeUntilJumpApex + 0.5f * MoveStats.TimeUntilJumpApex * MoveStats.TimeUntilJumpApex * new Vector2(0, MoveStats.Gravity);
                displacement += new Vector2(speed, 0) * apexTime; // No vertical movement during hang time
            }

            // DESCENDING
            else
            {
                float descendTime = simulationTime - (MoveStats.TimeUntilJumpApex + MoveStats.ApexHangTime);
                displacement = velocity * MoveStats.TimeUntilJumpApex + 0.5f * MoveStats.TimeUntilJumpApex * MoveStats.TimeUntilJumpApex * new Vector2(0, MoveStats.Gravity);
                displacement += new Vector2(speed, 0) * MoveStats.ApexHangTime; // Horizontal movement during hang time
                displacement += new Vector2(speed, 0) * descendTime + 0.5f * descendTime * descendTime * new Vector2(0, MoveStats.Gravity);
            }

            drawPoint = startPosition + displacement;

            #region Debug Visualization
            if (MoveStats.StopOnCollision)
            {
                RaycastHit2D hit = Physics2D.Raycast(previousPosition, drawPoint - previousPosition, Vector2.Distance(previousPosition, drawPoint), MoveStats.GroundLayer);
                if (hit.collider != null)
                {
                    // If a hit is detected, stop drawing the arc at the hit point
                    Gizmos.DrawLine(previousPosition, hit.point);
                    break;
                }
            }

            Gizmos.DrawLine(previousPosition, drawPoint);
            #endregion

            previousPosition = drawPoint;
        }
    }

    private void IsTouchingWall()
    {
        float originEndPoint;
        if (_isFacingRight)
            originEndPoint = _bodyCollider.bounds.max.x;
        else
            originEndPoint = _bodyCollider.bounds.min.x;

        float adjustedHeight = _bodyCollider.bounds.size.y * MoveStats.WallDetectionRayHeightMultiplier;

        Vector2 boxCastOrigin = new(originEndPoint, _bodyCollider.bounds.center.y);
        Vector2 boxCastSize = new(MoveStats.WallDetectionRayLength, adjustedHeight);

        _wallHit = Physics2D.BoxCast(boxCastOrigin, boxCastSize, 0f, transform.right, MoveStats.WallDetectionRayLength, MoveStats.GroundLayer);
        if (_wallHit.collider != null)
        {
            _lastWallHit = _wallHit;
            _isTouchingWall = true;
        }
        else
            _isTouchingWall = false;

        #region Debug Visualization
        if (MoveStats.DebugShowWallHitBox)
        {
            Color rayColor;
            if (_isTouchingWall)
                rayColor = Color.green;
            else
                rayColor = Color.red;

            Vector2 boxBottomLeft = new(boxCastOrigin.x - boxCastSize.x * 0.5f, boxCastOrigin.y - boxCastSize.y * 0.5f);
            Vector2 boxBottomRight = new(boxCastOrigin.x + boxCastSize.x * 0.5f, boxCastOrigin.y - boxCastSize.y * 0.5f);
            Vector2 boxTopLeft = new(boxCastOrigin.x - boxCastSize.x * 0.5f, boxCastOrigin.y + boxCastSize.y * 0.5f);
            Vector2 boxTopRight = new(boxCastOrigin.x + boxCastSize.x * 0.5f, boxCastOrigin.y + boxCastSize.y * 0.5f);

            Debug.DrawLine(boxBottomLeft, boxBottomRight, rayColor);
            Debug.DrawLine(boxBottomRight, boxTopRight, rayColor);
            Debug.DrawLine(boxTopRight, boxTopLeft, rayColor);
            Debug.DrawLine(boxTopLeft, boxBottomLeft, rayColor);
        }
        #endregion
    }

    private void CollisionChecks()
    {
        IsGrounded();
        BumpedHead();
        IsTouchingWall();
    }
    #endregion

    #region Timers
    private void CountTimers()
    {
        _jumpBufferTimer -= Time.deltaTime;

        if (!_isGrounded)
            _coyoteTimer -= Time.deltaTime;
        else
            _coyoteTimer = MoveStats.JumpCoyoteTime;
    }
    #endregion
}