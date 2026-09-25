using UnityEngine;
using UnityEngine.UIElements;

public class PlayerMovement : MonoBehaviour
{
    #region Fields
    [Header("References")]
    [field: SerializeField] public PlayerMovementStats MoveStats { get; private set; }
    [SerializeField] private Collider2D _collider;
    [SerializeField] private Transform _visualsTransform;

    private Rigidbody2D _rb;

    [Header("Movement")]
    public bool IsFacingRight { get; private set; }
    public MovementController Controller { get; private set; }

    public Vector2 velocity;

    [Header("Input")]
    private Vector2 _moveInput;
    private bool _runIsHeld;
    private bool _jumpWasPressed;
    private bool _jumpWasReleased;
    private bool _dashWasPressed;

    [Header("Gravity")]
    private const float STANDARD_GRAVITY_RATE = -2f;

    [Header("Jump")]
    private float _fastFallTime;
    private float _fastFallReleaseSpeed;
    private int _numOfAirJumpsUsed;
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
    private int _lastWallDirection;
    private bool _useWallJumpMoveStats;
    private bool _isWallJumping;
    private bool _isWallJumpFastFalling;
    private bool _isWallJumpFalling;
    private bool _isPastWallJumpApexThreshold;

    [Header("Dash")]
    public bool IsDashing { get; private set; }
    private Vector2 _dashDirection;
    private float _dashTimer;
    private float _dashOnGroundTimer;
    private float _dashFastFallTime;
    private float _dashFastFallReleaseSpeed;
    private float _dashBufferTimer;
    private int _numOfDashesUsed;
    private bool _isAirDashing;
    private bool _isDashFastFalling;

    [Header("Head Bump Slide")]
    public bool IsHeadBumpSliding { get; private set; }
    private float _jumpStartY;
    private float _dashStartY;
    private bool _justFinishedSlide;
    private bool _headBumpSlideFromDash;
    private bool _didHeadBumpSlideThisAirborneState;

    [Header("Slope")]
    private bool _isPerformingSlopeDash;
    private float _slopeDashAngle;
    #endregion

    #region Unity Callbacks
    private void Awake()
    {
        IsFacingRight = true;
        _rb = GetComponent<Rigidbody2D>();
        Controller = GetComponent<MovementController>();
    }

    private void Update()
    {
        _moveInput = InputManager.Instance.MoveVector;
        _runIsHeld = InputManager.Instance.RunIsHeld;
        if (InputManager.Instance.JumpWasPressed) _jumpWasPressed = true;
        if (InputManager.Instance.JumpWasReleased) _jumpWasReleased = true;
        if (InputManager.Instance.DashWasPressed) _dashWasPressed = true;
    }

    private void FixedUpdate()
    {
        Controller.PollSensors(velocity * Time.fixedDeltaTime);

        _justFinishedSlide = false;

        CountTimers(Time.fixedDeltaTime);

        JumpChecks();
        LandCheck();
        WallJumpCheck();
        WallSlideCheck();
        DashCheck();
        VelocityReset();

        HandleHorizontalMovement(Time.fixedDeltaTime);
        HandleHeadBumpSlide();
        Jump(Time.fixedDeltaTime);
        WallSlide(Time.fixedDeltaTime);
        WallJump(Time.fixedDeltaTime);
        Dash(Time.fixedDeltaTime);
        Fall(Time.fixedDeltaTime);
        HandleSlide(Time.fixedDeltaTime);

        ClampVelocity();
        Controller.Move(velocity * Time.fixedDeltaTime);

        // RESET INPUT BOOLS
        _jumpWasPressed = false;
        _jumpWasReleased = false;
        _dashWasPressed = false;
    }

    private void OnDrawGizmos()
    {
        if (MoveStats.ShowWalkJumpArc)
            DrawJumpArc(MoveStats.MaxWalkSpeed, Color.white);

        if (MoveStats.ShowRunJumpArc)
            DrawJumpArc(MoveStats.MaxRunSpeed, Color.red);
    }
    #endregion

    #region Movement
    private void HandleHorizontalMovement(float timeStep)
    {
        if (IsHeadBumpSliding) return;

        if (!IsDashing)
        {
            float acceleration = Controller.IsGrounded() ? MoveStats.GroundAcceleration : MoveStats.AirAcceleration;
            float deceleration = Controller.IsGrounded() ? MoveStats.GroundDeceleration : MoveStats.AirdDeceleration;

            if (_useWallJumpMoveStats)
            {
                acceleration = MoveStats.WallJumpMoveAcceleration;
                deceleration = MoveStats.WallJumpMoveDeceleration;
            }

            if (Mathf.Abs(_moveInput.x) >= MoveStats.MoveThreshold)
            {
                TurnCheck(_moveInput);
                float moveDirection = Mathf.Sign(_moveInput.x);
                float targetVelocityX = _runIsHeld ? moveDirection * MoveStats.MaxRunSpeed : moveDirection * MoveStats.MaxWalkSpeed;

                float t = Mathf.Clamp01(acceleration * timeStep);
                velocity.x = Mathf.Lerp(velocity.x, targetVelocityX, acceleration * timeStep);

                if (Mathf.Abs(velocity.x - targetVelocityX) <= 0.01f)
                    velocity.x = targetVelocityX;
            }
            else
            {
                float t = Mathf.Clamp01(deceleration * timeStep);
                velocity.x = Mathf.Lerp(velocity.x, 0f, t);

                if (Mathf.Abs(velocity.x) <= 0.01f)
                    velocity.x = 0f;
            }
        }
    }

    private void TurnCheck(Vector2 moveInput)
    {
        if (IsFacingRight && moveInput.x < 0)
            Turn(false);
        else if (!IsFacingRight && moveInput.x > 0)
            Turn(true);
    }

    private void Turn (bool turnRight)
    {
        if (turnRight)
        {
            IsFacingRight = true;
            _visualsTransform.Rotate(0f, 180f, 0f);
        }
        else
        {
            IsFacingRight = false;
            _visualsTransform.Rotate(0f, -180f, 0f);
        }
    }

    private void ClampVelocity()
    {
        if (Controller.IsSliding)
            velocity.y = Mathf.Clamp(velocity.y, -MoveStats.SlideSpeed, 50f);
        else if (IsDashing)
            velocity.y = Mathf.Clamp(velocity.y, -50f, 50f);
        else
            velocity.y = Mathf.Clamp(velocity.y, -MoveStats.MaxFallSpeed, 50f);

    }

    private void HandleHeadBumpSlide()
    {
        if (!IsHeadBumpSliding 
            && !_didHeadBumpSlideThisAirborneState 
            && !Controller.IsTouchingWall()
            && (_isJumping || IsDashing || _isWallJumping) 
            && Controller.BumpedHead() 
            && !Controller.State.IsHittingBothCorners 
            && !Controller.State.IsHittingCeilingCenter)
        {
            if (_isWallSliding || Controller.IsSliding) return;

            if (Controller.State.CeilingAngle <= MoveStats.MaxSlopeAngleForHeadBump)
            {
                IsHeadBumpSliding = true;
                _didHeadBumpSlideThisAirborneState = true;
            }
        }

        if (IsHeadBumpSliding)
        {
            velocity.y = 0f;

            if (Controller.State.HeadBumpSlideDirection == 0 || !Controller.BumpedHead() || Controller.State.IsHittingCeilingCenter || Controller.State.IsHittingBothCorners)
            {
                IsHeadBumpSliding = false;
                velocity.x = 0f;

                if (!_headBumpSlideFromDash)
                {
                    float compensationFactor = (1 - MoveStats.JumpHeightCompensationFactor) + 1;
                    float jumpPeakY = _jumpStartY + (MoveStats.JumpHeight * compensationFactor);
                    float remainingHeight = jumpPeakY - _rb.position.y;

                    if (remainingHeight > 0f)
                    {
                        float requiredVelocity = Mathf.Sqrt(2 * Mathf.Abs(MoveStats.Gravity) * remainingHeight);
                        velocity.y = requiredVelocity;
                    }
                }
                else if (_headBumpSlideFromDash)
                {
                    float targetApexY = _dashStartY + MoveStats.DashTargetApexHeight;
                    float remainingHeight = targetApexY - _rb.position.y;

                    if (remainingHeight > 0f)
                    {
                        float requiredVelocity = Mathf.Sqrt(2 * Mathf.Abs(MoveStats.Gravity) * remainingHeight);
                        velocity.y = requiredVelocity;
                    }
                }

                _headBumpSlideFromDash = false;
                _justFinishedSlide = true;
            }
            else
                velocity.x = Controller.State.HeadBumpSlideDirection * MoveStats.HeadBumpSlideSpeed;
        }
    }
    #endregion

    #region Land/Fall
    private void LandCheck()
    {
        if (Controller.IsGrounded())
        {
            bool isGroundAWall = Controller.State.SlopeAngle >= MoveStats.MinAngleForWallSlide && Controller.State.SlopeAngle <= MoveStats.MaxAngleForWallSlide;
            if (isGroundAWall) return;

            // LANDED
            if ((_isJumping || _isFalling || _isWallJumpFalling || _isWallJumping || _isWallSlideFalling || _isWallSliding || _isDashFastFalling || IsHeadBumpSliding)
                && velocity.y <= 0f)
            {
                IsHeadBumpSliding = false;
                _didHeadBumpSlideThisAirborneState = false;
                ResetJumpValues();
                StopWallSlide();
                ResetWallJumpValues();
                ResetDashes();
                ResetDashValues();
            }

            bool isStable = Controller.State.SlopeAngle <= MoveStats.MaxSlopeAngle;
            bool isWedged = Controller.State.IsAgainstWall;

            if (MoveStats.ResetAirJumpsOnMaxSlopeLand || isStable || isWedged)
                _numOfAirJumpsUsed = 0;
        }
    }

    private void Fall(float timeStep)
    {
        // NORMAL GRAVITY WHILE FALLING
        if (!Controller.IsGrounded() && !_isJumping && !_isWallSliding && !_isWallJumping && !IsDashing && !_isDashFastFalling)
        {
            if (!_isFalling)
                _isFalling = true;

            velocity.y += MoveStats.Gravity * timeStep;
        }
    }

    private void VelocityReset()
    {
        if (Controller.IsSliding) return;

        if (Controller.IsGrounded() 
            && !IsSlideableSlope(Controller.State.SlopeAngle) 
            && velocity.y <= 0f)
            velocity.y = -2f;
    }
    #endregion

    #region Jump
    private void ResetJumpValues()
    {
        _isJumping = false;
        _isFalling = false;
        _isFastFalling = false;
        _fastFallTime = 0f;
        _isPastApexThreshold = false;
    }

    private void JumpChecks()
    {
        // WHEN JUMP BUTTON IS PRESSED
        if (_jumpWasPressed)
        {
            if (_isWallSlideFalling && _wallJumpPostBufferTimer >= 0f) 
                return;
            else if (_isWallSliding || (Controller.IsTouchingWall() && (!Controller.IsGrounded() || Controller.IsSliding || Controller.State.IsAgainstSteepSlope))) 
                return;

            _jumpBufferTimer = MoveStats.JumpBufferTime;
            _jumpReleasedDuringBuffer = false;
        }

        // WHEN JUMP IS RELEASED
        if (_jumpWasReleased)
        {
            if (_jumpBufferTimer > 0f)
                _jumpReleasedDuringBuffer = true;

            if (_isJumping && velocity.y > 0f)
            {
                if (_isPastApexThreshold)
                {
                    _isPastApexThreshold = false;
                    _isFastFalling = true;
                    _fastFallTime = MoveStats.TimeForUpwardsCancel;
                    velocity.y = 0f;
                }
                else
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = velocity.y;
                }
            }
        }

        if (_jumpBufferTimer > 0f)
        {
            // INITIATE SINGLE JUMP WITH JUMP BUFFERING AND COYOTE TIME
            if (!_isJumping && (Controller.IsGrounded() || _coyoteTimer > 0f) && (MoveStats.CanJumpOnMaxSlopes || Controller.State.SlopeAngle <= MoveStats.MaxSlopeAngle))
            {
                InitiateJump(0);

                if (_jumpReleasedDuringBuffer)
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = velocity.y;
                }
            }

            // DOUBLE JUMP
            else if ((_isJumping || _isWallJumping || _isWallSlideFalling || _isAirDashing || _isDashFastFalling || Controller.IsSliding) 
                && !Controller.IsTouchingWall() 
                && _numOfAirJumpsUsed < MoveStats.NumOfAirJumpsAllowed)
            {
                _isFastFalling = false;
                InitiateJump(1);

                if (_isDashFastFalling)
                    _isDashFastFalling = false;
            }

            // AIR JUMP AFTER COYOTE TIME LAPSED ( TAKE OFF EXTRA JUMP SO WE DON'T GET A BONUS JUMP)
            else if (_isFalling && !_isWallSlideFalling && _numOfAirJumpsUsed < MoveStats.NumOfAirJumpsAllowed)
            {
                InitiateJump(1);
                _isFastFalling = false;
            }
        }
    }

    private void InitiateJump(int numOfAirJumpsUsed)
    {
        if (!_isJumping)
            _isJumping = true;

        _jumpWasPressed = false;

        ResetWallJumpValues();

        _jumpBufferTimer = 0f;
        _numOfAirJumpsUsed += numOfAirJumpsUsed;
        velocity.y = MoveStats.InitialJumpVelocity;
        _didHeadBumpSlideThisAirborneState = false;

        _jumpStartY = _rb.position.y;
    }

    private void Jump(float timeStep)
    {
        // APPLY GRAVITY WHILE JUMPING
        if (_isJumping)
        {
            // CHECK FOR HEAD BUMP
            if (Controller.BumpedHead() && !IsHeadBumpSliding)
            {
                if (Controller.State.HeadBumpSlideDirection != 0 && !Controller.State.IsHittingCeilingCenter && !Controller.State.IsHittingBothCorners)
                    _headBumpSlideFromDash = false;
                else if (MoveStats.JumpFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    velocity -= (Vector2.Dot(velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    velocity.y = 0f;
                    _isFastFalling = true;
                }
            }

            if (IsHeadBumpSliding)
            {
                velocity.y = 0f;
                return;
            }

            if (!_justFinishedSlide)
            {
                // GRAVITY ON ASCENDING
                if (velocity.y >= 0f)
                {
                    // APEX CONTROLS
                    _apexPoint = Mathf.InverseLerp(MoveStats.InitialJumpVelocity, 0f, velocity.y);

                    if (_apexPoint > MoveStats.ApexThreshold)
                    {
                        if (!_isPastApexThreshold)
                        {
                            _isPastApexThreshold = true;
                            _timePastApexThreshold = 0f;
                        }
                        else
                        {
                            _timePastApexThreshold += timeStep;
                            if (_timePastApexThreshold < MoveStats.ApexHangTime)
                                velocity.y = 0f;
                            else
                                velocity.y = -0.01f;
                        }
                    }

                    // GRAVITY ON ASCENDING BUT NOT PAST APEX THRESHOLD
                    else if (!_isFastFalling)
                    {
                        velocity.y += MoveStats.Gravity * timeStep;
                        if (_isPastApexThreshold)
                            _isPastApexThreshold = false;
                    }
                }

                // GRAVITY ON DESCENDING
                else if (!_isFastFalling)
                    velocity.y += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * timeStep;

                else if (velocity.y < 0f)
                {
                    if (!_isFalling)
                        _isFalling = true;
                }
            }
        }

        // JUMP CUT
        if (_isFastFalling)
        {
            if (_fastFallTime >= MoveStats.TimeForUpwardsCancel)
                velocity.y += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * timeStep;
            else if (_fastFallTime < MoveStats.TimeForUpwardsCancel)
                velocity.y = Mathf.Lerp(_fastFallReleaseSpeed, 0f, (_fastFallTime / MoveStats.TimeForUpwardsCancel));

            _fastFallTime += timeStep;
        }
    }
    #endregion

    #region Wall Slide
    private void WallSlideCheck()
    {
        bool isTouchingSideWall = Controller.IsTouchingWall();
        bool isSideWallAngle = Controller.State.WallAngle >= MoveStats.MinAngleForWallSlide && Controller.State.WallAngle <= MoveStats.MaxAngleForWallSlide;

        if (!IsDashing && isTouchingSideWall && isSideWallAngle && !Controller.IsGrounded())
        {
            if (velocity.y < 0f && !_isWallSliding)
            {
                ResetJumpValues();
                ResetWallJumpValues();
                ResetDashValues();

                if (MoveStats.ResetDashOnWallSlide)
                    ResetDashes();

                _isWallSlideFalling = false;
                _isWallSliding = true;

                if (MoveStats.ResetJumpsOnWallSlide)
                {
                    _numOfAirJumpsUsed = 0;
                }
            }
        }
        else if (_isWallSliding && !isTouchingSideWall)
        {
            _isWallSlideFalling = true;
            StopWallSlide();
        }
        else
            StopWallSlide();
    }

    private void StopWallSlide()
    {
        if (_isWallSliding)
            _isWallSliding = false;
    }

    private void WallSlide(float timeStep)
    {
        if (_isWallSliding)
            velocity.y = Mathf.Lerp(velocity.y, -MoveStats.WallSlideSpeed, MoveStats.WallSlideDecelerationSpeed * timeStep);
    }
    #endregion

    #region Wall Jump
    private bool ShouldApplyPostWallJumpBuffer()
    {
        bool isWallAngleValid = Controller.State.WallAngle >= MoveStats.MinAngleForWallSlide && Controller.State.WallAngle <= MoveStats.MaxAngleForWallSlide;

        if (Controller.IsTouchingWall() && isWallAngleValid || _isWallSliding)
        {
            if (Controller.State.WallDirection != 0)
                _lastWallDirection = Controller.GetWallDirection();

            return true;
        }
        else
            return false;
    }

    private void ResetWallJumpValues()
    {
        _isWallSlideFalling = false;
        _useWallJumpMoveStats = false;
        _isWallJumping = false;
        _isWallJumpFastFalling = false;
        _isWallJumpFalling = false;
        _isPastWallJumpApexThreshold = false;

        _wallJumpFastFallTime = 0f;
        _wallJumpTime = 0f;
    }

    private void WallJumpCheck()
    {
        if (ShouldApplyPostWallJumpBuffer())
            _wallJumpPostBufferTimer = MoveStats.WallJumpPostBufferTime;

        // WALL JUMP FAST FALLING
        if (_jumpWasReleased && !_isWallSliding && !Controller.IsTouchingWall() && _isWallJumping)
        {
            if (velocity.y > 0f)
            {
                if (_isPastWallJumpApexThreshold)
                {
                    _isPastWallJumpApexThreshold = false;
                    _isWallJumpFastFalling = true;
                    _wallJumpFastFallTime = MoveStats.TimeForUpwardsCancel;

                    velocity.y = 0f;
                }
                else
                {
                    _isWallJumpFastFalling = true;
                    _wallJumpFastFallReleaseSpeed = velocity.y;
                }
            }
        }

        // ACTUAL JUMP WITH POST WALL JUMP BUFFER TIME
        if (!Controller.IsGrounded() && _jumpWasPressed && _wallJumpPostBufferTimer > 0f)
        {
            InitiateWallJump();
        }
    }

    private void InitiateWallJump()
    {
        if (!_isWallJumping)
        {
            _isWallJumping = true;
            _useWallJumpMoveStats = true;
        }

        _jumpWasPressed = false;

        StopWallSlide();
        ResetJumpValues();
        _wallJumpTime = 0f;

        velocity.y = MoveStats.InitialWallJumpVelocity;
        velocity.x = Mathf.Abs(MoveStats.WallJumpDirection.x) * _lastWallDirection;
        _didHeadBumpSlideThisAirborneState = false;

        _jumpStartY = _rb.position.y;
    }

    private void WallJump(float timeStep)
    {
        // APPLY WALL JUMP GRAVITY
        if (_isWallJumping)
        {
            // TIME TO TAKE OVER MOVEMENT CONTROLS WHILE JUMPING
            _wallJumpTime += timeStep;
            if (_wallJumpTime >= MoveStats.TimeUntilJumpApex)
                _useWallJumpMoveStats = false;

            // HIT HEAD
            if (Controller.BumpedHead() && !IsHeadBumpSliding)
            {
                if (Controller.State.HeadBumpSlideDirection != 0 && !Controller.State.IsHittingCeilingCenter && !Controller.State.IsHittingBothCorners)
                    _headBumpSlideFromDash = false;
                else if (MoveStats.JumpFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    velocity = velocity - (Vector2.Dot(velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    velocity.y = 0f;
                    _isWallJumpFastFalling = true;
                    _useWallJumpMoveStats = false;
                }
            }

            if (IsHeadBumpSliding)
            {
                velocity.y = 0f;
                return;
            }

            if (!_justFinishedSlide)
            {
                // GRAVITY IN ASCENDING
                if (velocity.y >= 0f)
                {
                    // APEX CONTROLS
                    _wallJumpApexPoint = Mathf.InverseLerp(MoveStats.WallJumpDirection.y, 0f, velocity.y);

                    if (_wallJumpApexPoint > MoveStats.ApexThreshold)
                    {
                        if (!_isPastWallJumpApexThreshold)
                        {
                            _isPastWallJumpApexThreshold = true;
                            _timePastWallJumpApexThreshold = 0f;
                        }
                        else
                        {
                            _timePastWallJumpApexThreshold += timeStep;

                            if (_timePastWallJumpApexThreshold < MoveStats.ApexHangTime)
                                velocity.y = 0f;
                            else
                                velocity.y = -0.01f;
                        }
                    }

                    // GRAVITY IN ASCENDING BUT NOT PAST APEX THRESHOLD
                    else if (!_isWallJumpFastFalling)
                    {
                        velocity.y += MoveStats.WallJumpGravity * timeStep;

                        if (_isPastWallJumpApexThreshold)
                            _isPastWallJumpApexThreshold = false;
                    }
                }

                // GRAVITY ON DESCENDING
                else if (!_isWallJumpFastFalling)
                    velocity.y += MoveStats.WallJumpGravity * timeStep;
                else if (velocity.y < 0f)
                {
                    if (!_isWallJumpFalling)
                        _isWallJumpFalling = true;
                }
            }
        }

        // HANDLE WALL JUMP CUT TIME
        if (_isWallJumpFastFalling)
        {
            if (_wallJumpFastFallTime >= MoveStats.TimeForUpwardsCancel)
                velocity.y += MoveStats.WallJumpGravity * MoveStats.WallJumpGravityOnReleaseMultiplier * timeStep;
            else if (_wallJumpFastFallTime < MoveStats.TimeForUpwardsCancel)
                velocity.y = Mathf.Lerp(_wallJumpFastFallReleaseSpeed, 0f, (_wallJumpFastFallTime / MoveStats.TimeForUpwardsCancel));

            _wallJumpFastFallTime += timeStep;
        }
    }
    #endregion

    #region Dash
    private void ResetDashValues()
    {
        _isDashFastFalling = false;
        _dashOnGroundTimer = -0.01f;

        _dashFastFallReleaseSpeed = 0f;
        _dashFastFallTime = 0f;
        _dashDirection = Vector2.zero;
        _isPerformingSlopeDash = false;
    }

    private void ResetDashes()
    {
        _numOfDashesUsed = 0;
    }

    private void DashCheck()
    {
        if (_dashWasPressed)
            _dashBufferTimer = MoveStats.DashBufferTime;

        if (_dashBufferTimer > 0f)
        {
            // GROUND DASH
            if (Controller.IsGrounded() && _dashOnGroundTimer < 0 && !IsDashing)
            {
                InitiateDash();
                _dashBufferTimer = 0f;
            }

            // AIR DASH
            else if (!Controller.IsGrounded() && !IsDashing && _numOfDashesUsed < MoveStats.NumOfDashes)
            {
                _isAirDashing = true;
                InitiateDash();
                _dashBufferTimer = 0f;
            }
        }
    }

    private void InitiateDash()
    {
        _dashWasPressed = false;

        _dashStartY = _rb.position.y;

        _dashDirection = _moveInput;
        TurnCheck(_dashDirection);

        Vector2 closestDirection = Vector2.zero;
        float minDistance = Vector2.Distance(_dashDirection, MoveStats.DashDirections[0]);

        for (int i = 0; i < MoveStats.DashDirections.Length; i++)
        {
            // SKIP IF WE HIT IT DEAD ON
            if (_dashDirection == MoveStats.DashDirections[i])
            {
                closestDirection = _dashDirection;
                break;
            }

            float distance = Vector2.Distance(_dashDirection, MoveStats.DashDirections[i]);

            // CHECK IF THIS IS A DIAGONAL DIRECTION AND APPLY BIAS
            bool isDiagonal = (Mathf.Abs(MoveStats.DashDirections[i].x) == 1 && Mathf.Abs(MoveStats.DashDirections[i].y) == 1);
            if (isDiagonal)
                distance -= MoveStats.DashDiagonallyBias;
            else if (distance < minDistance)
            {
                minDistance = distance;
                closestDirection = MoveStats.DashDirections[i];
            }
        }

        // HANDLE DIRECTION WITH NO INPUT
        if (closestDirection == Vector2.zero)
        {
            if (IsFacingRight)
                closestDirection = Vector2.right;
            else
                closestDirection = Vector2.left;
        }

        if (Controller.IsGrounded() && closestDirection.y < 0f && closestDirection.x != 0f)
            closestDirection = new Vector2(Mathf.Sign(closestDirection.x), 0f);

        _dashDirection = closestDirection;
        _numOfDashesUsed++;
        IsDashing = true;
        _dashTimer = 0f;
        _dashOnGroundTimer = MoveStats.TimeBetweenDashesOnGround;

        ResetJumpValues();
        ResetWallJumpValues();
        StopWallSlide();

        if (_dashDirection.y > 0f)
            _didHeadBumpSlideThisAirborneState = false;

        _isPerformingSlopeDash = Controller.IsGrounded() 
            && Controller.State.SlopeAngle > 0f 
            && _dashDirection.y == 0f 
            && !_isJumping 
            && Mathf.Sign(_dashDirection.x) != Mathf.Sign(Controller.State.SlopeNormal.x);

        if (_isPerformingSlopeDash)
            _slopeDashAngle = Controller.State.SlopeAngle;
    }

    private void Dash(float timeStep)
    {
        if (_justFinishedSlide) return;

        if (IsDashing)
        {
            if (Controller.BumpedHead() && !IsHeadBumpSliding)
            {
                if (Controller.State.HeadBumpSlideDirection != 0 && !Controller.State.IsHittingCeilingCenter && !Controller.State.IsHittingBothCorners)
                {
                    _headBumpSlideFromDash = true;
                    _dashTimer = 0f;
                }
                else if (MoveStats.DashFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    velocity -= (Vector2.Dot(velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    velocity.y = 0f;
                    IsDashing = false;
                    _isAirDashing = false;
                    _dashTimer = 0f;
                }
            }

            if (IsHeadBumpSliding)
            {
                velocity.y = 0f;
                return;
            }

            // STOP DASH AFTER TIMER
            _dashTimer += timeStep;
            if (_dashTimer >= MoveStats.DashTime)
            {
                if (Controller.IsGrounded())
                    ResetDashes();

                _isAirDashing = false;
                IsDashing = false;

                if (!_isJumping && !_isWallJumping)
                {
                    _dashFastFallTime = 0f;
                    _dashFastFallReleaseSpeed = velocity.y;

                    if (!Controller.IsGrounded())
                        _isDashFastFalling = true;
                    else
                        velocity.y = 0f;
                }

                return;
            }

            if (MoveStats.DashDirectionMatchesSlopeDireciton && _isPerformingSlopeDash)
            {
                velocity.x = Mathf.Cos(_slopeDashAngle * Mathf.Deg2Rad) * MoveStats.DashSpeed * _dashDirection.x;
                velocity.y = Mathf.Sin(_slopeDashAngle * Mathf.Deg2Rad) * MoveStats.DashSpeed;
            }
            else
            {
                velocity.x = MoveStats.DashSpeed * _dashDirection.x;

                if (_dashDirection.y != 0f || _isAirDashing)
                    velocity.y = MoveStats.DashSpeed * _dashDirection.y;
                else if (!_isJumping && _dashDirection.y == 0f)
                    velocity.y = -0.001f;
            }

            #region Debug Dash Angle Visualization
            if (MoveStats.DebugShowDashAngle)
            {
                Vector2 drawOrigin = _collider.bounds.center;
                Vector2 drawDirection = velocity.normalized;
                float drawLength = MoveStats.ExtraRayDebugDistance * 4f;

                Debug.DrawRay(drawOrigin, drawDirection * drawLength, Color.cyan);
            }
            #endregion
        }

        // HANDLE DASH CUT TIME
        else if (_isDashFastFalling)
        {
            if (velocity.y > 0f)
            {
                if (_dashFastFallTime < MoveStats.DashTimeForUpwardsCancel)
                    velocity.y = Mathf.Lerp(_dashFastFallReleaseSpeed, 0f, (_dashFastFallTime / MoveStats.DashTimeForUpwardsCancel));
                else if (_dashFastFallTime >= MoveStats.DashTimeForUpwardsCancel)
                    velocity.y += MoveStats.Gravity * MoveStats.DashGravityOnReleaseMultiplier * timeStep;

                _dashFastFallTime += timeStep;
            }
            else
                velocity.y += MoveStats.Gravity * MoveStats.DashGravityOnReleaseMultiplier * timeStep;
        }
    }
    #endregion

    #region Slide
    private void HandleSlide(float timeStep)
    {
        if (Controller.IsSliding)
        {
            if (_isJumping) return;
            if (_isWallJumping) return;

            velocity.y += MoveStats.Gravity * timeStep;
        }
    }
    #endregion

    #region Visualization
    private void DrawJumpArc(float moveSpeed, Color gizmoColor)
    {
        Vector2 startPosition = new(_collider.bounds.center.x, _collider.bounds.min.y);
        Vector2 previousPosition = startPosition;
        float speed = 0f;
        if (MoveStats.DrawRight)
            speed = moveSpeed;
        else
            speed = -moveSpeed;

        Vector2 velocity = new(speed, MoveStats.InitialJumpVelocity);
        Gizmos.color = gizmoColor;

        float timeStep = (2 * MoveStats.TimeUntilJumpApex) / MoveStats.ArcResolution;
        float totalTime = (2 * MoveStats.TimeUntilJumpApex) + MoveStats.ApexHangTime;

        for (int i = 0; i < MoveStats.VisualizationSteps; i++)
        {
            float simulationTime = i * timeStep;
            Vector2 displacement;
            Vector2 drawPoint;

            // ASCENDING
            if (simulationTime < MoveStats.TimeUntilJumpApex)
                displacement = velocity * simulationTime + 0.5f * new Vector2(0, MoveStats.Gravity) * simulationTime * simulationTime;

            // APEX HANG TIME
            else if (simulationTime < MoveStats.TimeUntilJumpApex + MoveStats.ApexHangTime)
            {
                float apexTime = simulationTime - MoveStats.TimeUntilJumpApex;
                displacement = velocity * MoveStats.TimeUntilJumpApex + 0.5f * new Vector2(0f, MoveStats.Gravity) * MoveStats.TimeUntilJumpApex * MoveStats.TimeUntilJumpApex;
                displacement += new Vector2(speed, 0f) * apexTime; // NO VERTICAL MOVEMENT DURING HANG TIME
            }

            // DESCENDING
            else
            {
                float descendTime = simulationTime - (MoveStats.TimeUntilJumpApex + MoveStats.ApexHangTime);
                displacement = velocity * MoveStats.TimeUntilJumpApex + 0.5f * new Vector2(0f, MoveStats.Gravity) * MoveStats.TimeUntilJumpApex * MoveStats.TimeUntilJumpApex;
                displacement += new Vector2(speed, 0f) * MoveStats.ApexHangTime; // HORIZONTAL MOVEMENT DURING HANG TIME
                displacement += new Vector2(speed, 0f) * descendTime + 0.5f * new Vector2(0f, MoveStats.Gravity) * descendTime * descendTime;
            }

            drawPoint = startPosition + displacement;

            if (MoveStats.StopOnCollision)
            {
                RaycastHit2D hit = Physics2D.Raycast(previousPosition, drawPoint - previousPosition, Vector2.Distance(previousPosition, drawPoint), MoveStats.GroundLayer);
                if (hit.collider != null)
                {
                    // IF HIT DETECTED, STOP DRAWING ARC AT HIT POINT
                    Gizmos.DrawLine(previousPosition, hit.point);
                    break;
                }
            }

            Gizmos.DrawLine(previousPosition, drawPoint);
            previousPosition = drawPoint;
        }
    }
    #endregion

    #region Helper Methods
    public bool IsSlideableSlope(float slopeAngle) => slopeAngle >= MoveStats.MaxSlopeAngle && slopeAngle < MoveStats.MinAngleForWallSlide;
    public bool IsWalkableSlope(float slopeAngle) => slopeAngle <= MoveStats.MaxSlopeAngle && slopeAngle < MoveStats.MinAngleForWallSlide;
    public bool IsWallSlideable(float slopeAngle) => slopeAngle >= MoveStats.MinAngleForWallSlide && slopeAngle <= MoveStats.MaxAngleForWallSlide;
    #endregion

    #region Timers
    private void CountTimers(float timeStep)
    {
        // JUMP BUFFER TIMER
        _jumpBufferTimer -= timeStep;

        // JUMP COYOTE TIMER
        HandleCoyoteTimer(timeStep);

        // WALL JUMP BUFFER TIMER
        _wallJumpPostBufferTimer -= timeStep;

        // DASH TIMER
        HandleDashOnGroundTimer(timeStep);

        // DASH BUFFER TIMER
        _dashBufferTimer -= timeStep;
    }

    private void HandleCoyoteTimer(float timeStep)
    {
        if (Controller.IsGrounded() && !Controller.IsSliding)
            _coyoteTimer = MoveStats.JumpCoyoteTime;
        else
            _coyoteTimer -= timeStep;
    }

    private void HandleDashOnGroundTimer(float timeStep)
    {
        if (Controller.IsGrounded() && !Controller.IsSliding)
            _dashOnGroundTimer -= timeStep;
    }
    #endregion
}