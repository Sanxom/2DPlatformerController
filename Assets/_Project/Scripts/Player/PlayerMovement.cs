using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    #region Fields
    [Header("References")]
    [field: SerializeField] public PlayerMovementStats MoveStats { get; private set; }
    [SerializeField] private Collider2D _collider;

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
    private Vector2 _dashDirection;
    private float _dashTimer;
    private float _dashOnGroundTimer;
    private float _dashFastFallTime;
    private float _dashFastFallReleaseSpeed;
    private float _dashBufferTimer;
    private int _numOfDashesUsed;
    private bool _isDashing;
    private bool _isAirDashing;
    private bool _isDashFastFalling;

    [Header("Head Bump Slide")]
    private float _jumpStartY;
    private float _dashStartY;
    private int _headBumpSlideDirection;
    private bool _isHeadBumpSliding;
    private bool _justFinishedSlide;
    private bool _headBumpSlideFromDash;
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
        _justFinishedSlide = false;

        CountTimers(Time.fixedDeltaTime);

        JumpChecks();
        LandCheck();
        WallJumpCheck();
        WallSlideCheck();
        DashCheck();

        HandleHorizontalMovement(Time.fixedDeltaTime);
        HandleHeadBumpSlide();
        Jump(Time.fixedDeltaTime);
        WallSlide(Time.fixedDeltaTime);
        WallJump(Time.fixedDeltaTime);
        Dash(Time.fixedDeltaTime);
        Fall(Time.fixedDeltaTime);

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
        if (_isHeadBumpSliding) return;

        if (!_isDashing)
        {
            TurnCheck(_moveInput);
            float targetVelocityX = 0f;
            if (Mathf.Abs(_moveInput.x) >= MoveStats.MoveThreshold)
            {
                float moveDirection = Mathf.Sign(_moveInput.x);
                targetVelocityX = _runIsHeld ? moveDirection * MoveStats.MaxRunSpeed : moveDirection * MoveStats.MaxWalkSpeed;
            }

            float acceleration = Controller.IsGrounded() ? MoveStats.GroundAcceleration : MoveStats.AirAcceleration;
            float deceleration = Controller.IsGrounded() ? MoveStats.GroundDeceleration : MoveStats.AirdDeceleration;

            if (_useWallJumpMoveStats)
            {
                acceleration = MoveStats.WallJumpMoveAcceleration;
                deceleration = MoveStats.WallJumpMoveDeceleration;
            }

            if (Mathf.Abs(_moveInput.x) >= MoveStats.MoveThreshold)
                velocity.x = Mathf.Lerp(velocity.x, targetVelocityX, acceleration * timeStep);
            else
                velocity.x = Mathf.Lerp(velocity.x, 0f, deceleration * timeStep);
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
            transform.Rotate(0f, 180f, 0f);
        }
        else
        {
            IsFacingRight = false;
            transform.Rotate(0f, -180f, 0f);
        }
    }

    private void ClampVelocity()
    {
        // CLAMP FALL SPEED
        if (!_isDashing)
            velocity.y = Mathf.Clamp(velocity.y, -MoveStats.MaxFallSpeed, 50f);
        else
            velocity.y = Mathf.Clamp(velocity.y, -50f, 50f);
    }

    private void HandleHeadBumpSlide()
    {
        if (!_isHeadBumpSliding 
            && (_isJumping || _isDashing || _isWallJumping) 
            && Controller.BumpedHead() 
            && !Controller.IsHittingBothCorners 
            && !Controller.IsHittingCeilingCenter)
        {
            _isHeadBumpSliding = true;
            _headBumpSlideDirection = Controller.HeadBumpSlideDirection;
        }

        if (_isHeadBumpSliding)
        {
            velocity.y = 0f;

            if (Controller.HeadBumpSlideDirection == 0 || !Controller.BumpedHead() || Controller.IsHittingCeilingCenter || Controller.IsHittingBothCorners)
            {
                _isHeadBumpSliding = false;
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
                velocity.x = _headBumpSlideDirection * MoveStats.HeadBumpSlideSpeed;
        }
    }
    #endregion

    #region Land/Fall
    private void LandCheck()
    {
        if (Controller.IsGrounded())
        {
            // LANDED
            if ((_isJumping || _isFalling || _isWallJumpFalling || _isWallJumping || _isWallSlideFalling || _isWallSliding || _isDashFastFalling || _isHeadBumpSliding)
                && velocity.y <= 0f)
            {
                _isHeadBumpSliding = false;
                ResetJumpValues();
                StopWallSlide();
                ResetWallJumpValues();
                ResetDashes();
                ResetDashValues();

                _numOfAirJumpsUsed = 0;
            }

            // KEEP Y-VELOCITY CONSTANT WHILE ON GROUND
            if (velocity.y <= 0f)
                velocity.y = STANDARD_GRAVITY_RATE;
        }
    }

    private void Fall(float timeStep)
    {
        // NORMAL GRAVITY WHILE FALLING
        if (!Controller.IsGrounded() && !_isJumping && !_isWallSliding && !_isWallJumping && !_isDashing && !_isDashFastFalling)
        {
            if (!_isFalling)
                _isFalling = true;

            velocity.y += MoveStats.Gravity * timeStep;
        }
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
            else if (_isWallSliding || (Controller.IsTouchingWall(IsFacingRight) && !Controller.IsGrounded())) 
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
            // SINGLE JUMP
            if (!_isJumping && (Controller.IsGrounded() || _coyoteTimer > 0f))
            {
                InitiateJump(0);

                if (_jumpReleasedDuringBuffer)
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = velocity.y;
                }
            }

            // DOUBLE JUMP
            else if ((_isJumping || _isWallJumping || _isWallSlideFalling || _isAirDashing || _isDashFastFalling) 
                && !Controller.IsTouchingWall(IsFacingRight) 
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

        ResetWallJumpValues();

        _jumpBufferTimer = 0f;
        _numOfAirJumpsUsed += numOfAirJumpsUsed;
        velocity.y = MoveStats.InitialJumpVelocity;

        _jumpStartY = _rb.position.y;
    }

    private void Jump(float timeStep)
    {
        // APPLY GRAVITY WHILE JUMPING
        if (_isJumping)
        {
            // CHECK FOR HEAD BUMP
            if (Controller.BumpedHead() && !_isHeadBumpSliding)
            {
                if (Controller.HeadBumpSlideDirection != 0 && !Controller.IsHittingCeilingCenter && !Controller.IsHittingBothCorners)
                    _headBumpSlideFromDash = false;
                else
                {
                    velocity.y = 0f;
                    _isFastFalling = true;
                }
            }

            if (_isHeadBumpSliding)
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
        if (Controller.IsTouchingWall(IsFacingRight) && !Controller.IsGrounded() && !_isDashing)
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
        else if (_isWallSliding && !Controller.IsTouchingWall(IsFacingRight) && !Controller.IsGrounded() && !_isWallSlideFalling)
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
        if (Controller.IsTouchingWall(IsFacingRight) || _isWallSliding)
        {
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
        if (_jumpWasReleased && !_isWallSliding && !Controller.IsTouchingWall(IsFacingRight) && _isWallJumping)
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
        if (_jumpWasPressed && _wallJumpPostBufferTimer > 0f)
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

        StopWallSlide();
        ResetJumpValues();
        _wallJumpTime = 0f;

        velocity.y = MoveStats.InitialWallJumpVelocity;
        velocity.x = Mathf.Abs(MoveStats.WallJumpDirection.x) * -_lastWallDirection;

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
            if (Controller.BumpedHead() && !_isHeadBumpSliding)
            {
                if (Controller.HeadBumpSlideDirection != 0 && !Controller.IsHittingCeilingCenter && !Controller.IsHittingBothCorners)
                    _headBumpSlideFromDash = false;
                else
                {
                    velocity.y = 0f;
                    _isWallJumpFastFalling = true;
                    _useWallJumpMoveStats = false;
                }
            }

            if (_isHeadBumpSliding)
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
            if (Controller.IsGrounded() && _dashOnGroundTimer < 0 && !_isDashing)
            {
                InitiateDash();
                _dashBufferTimer = 0f;
            }

            // AIR DASH
            else if (!Controller.IsGrounded() && !_isDashing && _numOfDashesUsed < MoveStats.NumOfDashes)
            {
                _isAirDashing = true;
                InitiateDash();
                _dashBufferTimer = 0f;
            }
        }
    }

    private void InitiateDash()
    {
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
        _isDashing = true;
        _dashTimer = 0f;
        _dashOnGroundTimer = MoveStats.TimeBetweenDashesOnGround;

        ResetJumpValues();
        ResetWallJumpValues();
        StopWallSlide();
    }

    private void Dash(float timeStep)
    {
        if (_justFinishedSlide) return;

        if (_isDashing)
        {
            if (Controller.BumpedHead() && !_isHeadBumpSliding)
            {
                if (Controller.HeadBumpSlideDirection != 0 && !Controller.IsHittingCeilingCenter && !Controller.IsHittingBothCorners)
                {
                    _headBumpSlideFromDash = true;
                    _dashTimer = 0f;
                }
                else
                {
                    velocity.y = 0f;
                    _isDashing = false;
                    _isAirDashing = false;
                    _dashTimer = 0f;
                }
            }

            if (_isHeadBumpSliding)
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
                _isDashing = false;

                if (!_isJumping && !_isWallJumping)
                {
                    _dashFastFallTime = 0f;
                    _dashFastFallReleaseSpeed = velocity.y;

                    if (!Controller.IsGrounded())
                        _isDashFastFalling = true;
                }

                return;
            }

            velocity.x = MoveStats.DashSpeed * _dashDirection.x;

            if (_dashDirection.y != 0f || _isAirDashing)
                velocity.y = MoveStats.DashSpeed * _dashDirection.y;
            else if (!_isJumping && _dashDirection.y == 0f)
                velocity.y = -0.001f;
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

    #region Timers
    private void CountTimers(float timeStep)
    {
        // JUMP BUFFER TIMER
        _jumpBufferTimer -= timeStep;

        // JUMP COYOTE TIMER
        if (!Controller.IsGrounded())
            _coyoteTimer -= timeStep;
        else
            _coyoteTimer = MoveStats.JumpCoyoteTime;

        // WALL JUMP BUFFER TIMER
        _wallJumpPostBufferTimer -= timeStep;

        // DASH TIMER
        if (Controller.IsGrounded())
            _dashOnGroundTimer -= timeStep;

        // DASH BUFFER TIMER
        _dashBufferTimer -= timeStep;
    }
    #endregion
}