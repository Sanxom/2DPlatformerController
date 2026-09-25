using UnityEngine;
using UnityEditor;

public struct CollisionState
{
    public bool IsGrounded;
    public bool WasGroundedLastFrame;

    public float WallAngle;
    public int WallDirection;
    public bool IsAgainstWall;
    public bool WasAgainstWallLastFrame;

    public Vector2 SlopeNormal;
    public float SlopeAngle;
    public bool IsOnSlope;
    public bool IsAgainstSteepSlope;

    public Vector2 CeilingNormal;
    public float CeilingAngle;
    public bool IsHittingCeiling;

    public int HeadBumpSlideDirection;
    public bool IsHittingCeilingCenter;
    public bool IsHittingBothCorners;

    public void Reset()
    {
        WasGroundedLastFrame = IsGrounded;
        WasAgainstWallLastFrame = IsAgainstWall;

        IsGrounded = false;
        IsAgainstWall = false;
        WallDirection = 0;
        WallAngle = 0f;

        IsOnSlope = false;
        SlopeAngle = 0f;
        SlopeNormal = Vector2.zero;
        IsAgainstSteepSlope = false;

        IsHittingCeiling = false;
        CeilingAngle = 0f;
        CeilingNormal = Vector2.zero;

        HeadBumpSlideDirection = 0;
        IsHittingCeilingCenter = false;
        IsHittingBothCorners = false;
    }
}

public class MovementController : MonoBehaviour
{
    public bool IsSliding => _internalState.IsOnSlope && _internalState.SlopeAngle > _moveStats.MaxSlopeAngle;

    public const float COLLISION_PADDING = 0.015f;

    [field: SerializeField, Range(2, 100)] public int NumOfHorizontalRays { get; private set; } = 4;
    [field: SerializeField, Range(2, 100)] public int NumOfVerticalRays { get; private set; } = 4;

    [Header("Sensors")]
    [SerializeField] private float _verticalProbeDistance = 0.1f;
    [SerializeField] private float _horizontalProbeDistance = 0.1f;

    [Header("Safety")]
    [SerializeField] private float _safetyGraceDuration = 0.08f;
    
    public bool IsClimbingSlope { get; private set; }
    public bool WasClimbingSlopeLastFrame { get; private set; }
    public bool IsDescendingSlope { get; private set; }
    public float SlopeAngle { get; private set; }
    public Vector2 SlopeNormal { get; private set; }
    public int FaceDirection { get; private set; }

    public RaycastCorners raycastCorners;

    private float _horizontalRaySpace;
    private float _verticalRaySpace;

    private BoxCollider2D _collider;
    private PlayerMovementStats _moveStats;
    private PlayerMovement _playerMovement;
    private Rigidbody2D _rb;

    public CollisionState State { get; private set; }
    private CollisionState _internalState;

    private float _lastSafetyGroundFixedTime = -Mathf.Infinity;
    private RaycastHit2D _lastSafetyGroundHit;

    [System.Serializable]
    public struct RaycastCorners
    {
        public Vector2 topLeft;
        public Vector2 topRight;
        public Vector2 bottomLeft;
        public Vector2 bottomRight;
    }

    private void Awake()
    {
        _collider = GetComponent<BoxCollider2D>();
        _rb = GetComponent<Rigidbody2D>();
        _playerMovement =GetComponent<PlayerMovement>();
        _moveStats = _playerMovement.MoveStats;

        FaceDirection = 1;
    }

    private void Start()
    {
        CalculateRaySpacing();
    }

    public void PollSensors(Vector2 moveDelta)
    {
        _internalState.Reset();
        UpdateRaycastCorners();

        if (moveDelta.x != 0f)
            FaceDirection = (int)Mathf.Sign(moveDelta.x);

        HorizontalProbes(moveDelta);

        CeilingProbes(moveDelta);
        CheckCeilingBoxCast(moveDelta);

        GroundProbes(moveDelta);

        State = _internalState;
    }

    public void Move(Vector2 velocity)
    {
        UpdateRaycastCorners();
        ResetCollisionStates();

        if (velocity.y <= 0f && !_playerMovement.IsDashing && !WasClimbingSlopeLastFrame)
            DescendSlope(ref velocity);

        ResolveHorizontalMovement(ref velocity);
        ResolveVerticalMovement(ref velocity);
        _rb.MovePosition(_rb.position + velocity);
    }

    private void CheckCeilingBoxCast(Vector2 velocity)
    {
        if (velocity.y < 0) return;
        if (!_moveStats.UseHeadBumpSlide) return;

        float boxCastDistance = Mathf.Abs(velocity.y) + COLLISION_PADDING;
        Vector2 boxSize = new(_collider.bounds.size.x * _moveStats.HeadBumpBoxWidth, _moveStats.HeadBumpBoxHeight);
        Vector2 boxOrigin = new(_collider.bounds.center.x + velocity.x, _collider.bounds.max.y);

        RaycastHit2D hit = Physics2D.BoxCast(boxOrigin, boxSize, 0f, Vector2.up, boxCastDistance, _moveStats.GroundLayer);
        if (hit)
            _internalState.IsHittingCeilingCenter = true;

        #region Debug Visualization
        if (_moveStats.DebugShowHeadBumpBox)
        {
            Vector2 drawCenter = boxOrigin + (Vector2.up * boxCastDistance * 0.5f);
            Vector2 drawSize = new(boxSize.x, boxSize.y + boxCastDistance);
            Vector2 halfSize = drawSize * 0.5f;

            // 4 CORNERS
            Vector2 topLeft = drawCenter + new Vector2(-halfSize.x, halfSize.y);
            Vector2 topRight = drawCenter + new Vector2(halfSize.x, halfSize.y);
            Vector2 bottomRight = drawCenter + new Vector2(halfSize.x, -halfSize.y);
            Vector2 bottomLeft = drawCenter + new Vector2(-halfSize.x, -halfSize.y);

            Color color = hit ? Color.green : Color.red;

            Debug.DrawLine(topLeft, topRight, color);
            Debug.DrawLine(topRight, bottomRight, color);
            Debug.DrawLine(bottomRight, bottomLeft, color);
            Debug.DrawLine(bottomLeft, topLeft, color);
        }
        #endregion
    }

    private void GroundProbes(Vector2 moveDelta)
    {
        float rayLength = _verticalProbeDistance + COLLISION_PADDING;
        float smallestHitDistance = float.MaxValue;
        bool foundGround = false;
        bool foundWalkableGround = false;
        RaycastHit2D groundHit = new();
        float horizontalProjection = 0f;

        if (moveDelta.y <= 0f)
        {
            horizontalProjection = moveDelta.x;

            if (_internalState.IsAgainstWall || _internalState.IsAgainstSteepSlope)
                horizontalProjection = 0f;
        }

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = raycastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i + horizontalProjection);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization 
            if (_moveStats.DebugShowIsGrounded)
            {
                bool didHit = hit;
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(rayOrigin, Vector2.down * rayLength, rayColor);
            }
            #endregion

            if (hit)
            {
                float hitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isHitWalkable = hitAngle < _moveStats.MaxSlopeAngle;

                if (!foundGround)
                {
                    smallestHitDistance = hit.distance;
                    groundHit = hit;
                    foundGround = true;
                    foundWalkableGround = isHitWalkable;
                }
                else
                {
                    if (!foundWalkableGround && isHitWalkable)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                        foundWalkableGround = true;
                    }
                    else if (foundWalkableGround == isHitWalkable && hit.distance < smallestHitDistance)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                    }
                }
            }
            else if (IsClimbingSlope || IsDescendingSlope)
            {
                _internalState.IsGrounded = true;
                _internalState.SlopeAngle = SlopeAngle;
                _internalState.SlopeNormal = SlopeNormal;

                if (_internalState.SlopeAngle > 0.01f)
                    _internalState.IsOnSlope = true;

                return;
            }
        }

        if (foundGround)
        {
            float slopeAngle = Mathf.Round(Vector2.Angle(groundHit.normal, Vector2.up));
            bool isWallSlideable = _playerMovement.IsWallSlideable(slopeAngle);
            bool isFacingWall = Mathf.Sign(groundHit.normal.x) != FaceDirection;

            bool shouldWallSlide = isFacingWall || _moveStats.CanWallSlideFacingAwayFromWall;

            if (isWallSlideable && shouldWallSlide)
            {
                _internalState.IsAgainstWall = true;
                _internalState.WallAngle = slopeAngle;
                _internalState.WallDirection = (int)-Mathf.Sign(groundHit.normal.x);
            }
            else
            {
                _internalState.IsGrounded = true;
                _internalState.SlopeAngle = slopeAngle;
                _internalState.SlopeNormal = groundHit.normal;

                if (slopeAngle > 0.01f)
                    _internalState.IsOnSlope = true;
            }
        }
        else
        {
            if (WasClimbingSlopeLastFrame)
            {
                float smallestSafetyDistance = float.MaxValue;
                bool foundSafetyGround = false;
                RaycastHit2D safetyGroundHit = new();

                for (int i = 0; i < NumOfVerticalRays; i++)
                {
                    Vector2 rayOriginSafety = raycastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i);
                    RaycastHit2D hitSafety = Physics2D.Raycast(rayOriginSafety, Vector2.down, rayLength * 5f, _moveStats.GroundLayer);
                    if (hitSafety)
                    {
                        safetyGroundHit = hitSafety;
                        smallestSafetyDistance = safetyGroundHit.distance;
                        foundSafetyGround = true;
                    }
                }

                if (foundSafetyGround)
                {
                    float slopeAngle = Mathf.Round(Vector2.Angle(safetyGroundHit.normal, Vector2.up));

                    if (!_playerMovement.IsWallSlideable(slopeAngle))
                    {
                        _lastSafetyGroundFixedTime = Time.fixedTime;
                        _lastSafetyGroundHit = safetyGroundHit;
                        _internalState.IsGrounded = true;
                        _internalState.SlopeAngle = slopeAngle;
                        _internalState.SlopeNormal = safetyGroundHit.normal;

                        if (slopeAngle > 0.01f)
                            _internalState.IsOnSlope = true;
                    }
                }
            }

            bool usedRecentSafety = false;
            if (Time.fixedTime - _lastSafetyGroundFixedTime <= _safetyGraceDuration)
            {
                float reuseSlopeAngle = Mathf.Round(Vector2.Angle(_lastSafetyGroundHit.normal, Vector2.up));

                if (!_playerMovement.IsWallSlideable(reuseSlopeAngle))
                {
                    usedRecentSafety = true;
                    _internalState.IsGrounded = true;
                    _internalState.SlopeAngle = reuseSlopeAngle;
                    _internalState.SlopeNormal = _lastSafetyGroundHit.normal;

                    if (reuseSlopeAngle > 0.01f)
                        _internalState.IsOnSlope = true;
                }
            }

            if (!foundGround && !usedRecentSafety)
            {
                _internalState.IsGrounded = false;
                _internalState.IsOnSlope = false;
            }
        }

        #region Debug Slope Normal
        if (_moveStats.DebugShowSlopeNormal)
        {
            Vector2 drawOrigin = new(_collider.bounds.center.x, _collider.bounds.min.y);
            float drawLength = _moveStats.ExtraRayDebugDistance * 3f;
            Debug.DrawRay(drawOrigin, _internalState.SlopeNormal * drawLength, Color.yellow);
        }
        #endregion
    }

    private void CeilingProbes(Vector2 moveDelta)
    {
        if (moveDelta.y >= 0f)
        {
            float rayLength = _verticalProbeDistance + COLLISION_PADDING;

            bool hitLeftCorner = false;
            bool hitRightCorner = false;

            for (int i = 0; i < NumOfVerticalRays; i++)
            {
                Vector2 rayOrigin = raycastCorners.topLeft + Vector2.right * (_verticalRaySpace * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up, rayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    float rawHitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));

                    if (rawHitAngle > 85f)
                        continue;

                    bool isEdgeRay = (i == 0 || i == NumOfVerticalRays - 1);
                    if (isEdgeRay && hit.distance == 0 && rawHitAngle == 0)
                        continue;

                    float currentCeilingAngle;
                    if (hit.distance == 0f)
                    {
                        Vector2 safetyRayOrigin = rayOrigin + (2f * COLLISION_PADDING * Vector2.down);
                        RaycastHit2D safetyHit = Physics2D.Raycast(safetyRayOrigin, Vector2.up, COLLISION_PADDING * 3f, _moveStats.GroundLayer);

                        if (safetyHit)
                        {
                            currentCeilingAngle = Mathf.Round(Vector2.Angle(safetyHit.normal, Vector2.down));
                            _internalState.CeilingNormal = safetyHit.normal;
                        }
                        else
                        {
                            currentCeilingAngle = 0f;
                            _internalState.CeilingNormal = Vector2.down;
                        }
                    }
                    else
                    {
                        currentCeilingAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));
                        _internalState.CeilingNormal = hit.normal;
                    }

                    _internalState.IsHittingCeiling = true;

                    if (i == 0)
                        hitLeftCorner = true;
                    
                    if (i == NumOfVerticalRays - 1)
                        hitRightCorner = true;

                    if (currentCeilingAngle > _internalState.CeilingAngle)
                    {
                        _internalState.CeilingAngle = currentCeilingAngle;
                        _internalState.CeilingNormal = hit.normal;
                    }

                    if (_moveStats.UseHeadBumpSlide && currentCeilingAngle <= _moveStats.MaxSlopeAngleForHeadBump)
                    {
                        int slideDir = 0;
                        if (i == 0) slideDir = 1;
                        else if (i == NumOfVerticalRays - 1) slideDir = -1;

                        if (slideDir != 0)
                        {
                            Vector2 slideCheckRayOrigin = hit.point + (Vector2.down * COLLISION_PADDING * 2f);
                            float slideCheckRayLength = COLLISION_PADDING * 2f;
                            RaycastHit2D slideCheckHit = Physics2D.Raycast(slideCheckRayOrigin, Vector2.right * slideDir, slideCheckRayLength, _moveStats.GroundLayer);

                            if (!slideCheckHit)
                            {
                                _internalState.HeadBumpSlideDirection = slideDir;
                            }
                        }
                    }
                }

                #region Debug Visualization
                if (_moveStats.DebugShowHeadRays)
                {
                    float debugRayLength = _moveStats.ExtraRayDebugDistance;
                    bool didHit = Physics2D.Raycast(rayOrigin, Vector2.up, debugRayLength, _moveStats.GroundLayer);
                    Color rayColor = didHit ? Color.cyan : Color.red;

                    if (i == 0 || i == NumOfVerticalRays - 1)
                        rayColor = didHit ? Color.green : Color.magenta;

                    Debug.DrawRay(rayOrigin, Vector2.up * debugRayLength, rayColor);
                }
                #endregion
            }

            if (hitLeftCorner && hitRightCorner)
                _internalState.IsHittingBothCorners = true;
        }
    }

    private void HorizontalProbes(Vector2 moveDelta)
    {
        float rayLength = Mathf.Abs(moveDelta.x) + COLLISION_PADDING;
        if (rayLength < _horizontalProbeDistance)
            rayLength = _horizontalProbeDistance;

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            // Check left
            Vector2 rayOriginLeft = raycastCorners.bottomLeft + Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hitLeft = Physics2D.Raycast(rayOriginLeft, Vector2.left, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization
            if (_moveStats.DebugShowWallHit)
                Debug.DrawRay(rayOriginLeft, Vector2.left * rayLength, hitLeft ? Color.cyan : Color.red);
            #endregion

            bool foundWall = false;
            if (hitLeft)
            {
                float wallAngle = Mathf.Round(Vector2.Angle(hitLeft.normal, Vector2.up));
                if (_playerMovement.IsSlideableSlope(wallAngle))
                    _internalState.IsAgainstSteepSlope = true;
                
                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = 1;
                    _internalState.WallAngle = wallAngle;
                    foundWall = true;
                }
            }

            // Check Right
            Vector2 rayOrigin = raycastCorners.bottomRight + Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hitRight = Physics2D.Raycast(rayOrigin, Vector2.right, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization
            if (_moveStats.DebugShowWallHit)
                Debug.DrawRay(rayOrigin, Vector2.right * rayLength, hitRight ? Color.cyan : Color.red);
            #endregion

            if (hitRight)
            {
                float wallAngle = Mathf.Round(Vector2.Angle(hitRight.normal, Vector2.up));
                if (_playerMovement.IsSlideableSlope(wallAngle))
                    _internalState.IsAgainstSteepSlope = true;

                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = -1;
                    _internalState.WallAngle = wallAngle;
                    foundWall = true;
                }
            }

            if (foundWall) break;
        }
    }

    private void ResetCollisionStates()
    {
        WasClimbingSlopeLastFrame = IsClimbingSlope;
        IsClimbingSlope = false;
        IsDescendingSlope = false;
        SlopeAngle = 0f;
        SlopeNormal = Vector2.zero;
    }

    private void ResolveHorizontalMovement(ref Vector2 velocity)
    {
        float originalVelocityX = velocity.x;

        float directionX = Mathf.Sign(velocity.x);
        if (velocity.x == 0f)
            directionX = FaceDirection;

        float rayLength = Mathf.Abs(velocity.x) + COLLISION_PADDING;

        if (Mathf.Abs(velocity.x) < COLLISION_PADDING)
            rayLength = COLLISION_PADDING * 2f;

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOrigin = (directionX == -1) ? raycastCorners.bottomLeft : raycastCorners.bottomRight;
            rayOrigin += Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isSlideableSlope = slopeAngle > _moveStats.MaxSlopeAngle && slopeAngle < _moveStats.MinAngleForWallSlide;

                if (isSlideableSlope)
                {
                    velocity.x = (hit.distance - COLLISION_PADDING) * directionX;
                    rayLength = hit.distance;

                    if (IsClimbingSlope)
                        velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);

                    continue;
                }

                if (IsSliding)
                {
                    bool isWall = _playerMovement.IsWallSlideable(slopeAngle);

                    if (!isWall)
                        continue;
                }

                bool isLowerBodyHit = i <= (NumOfHorizontalRays / 2);
                if (isLowerBodyHit && _playerMovement.IsWalkableSlope(slopeAngle) && slopeAngle > 0f)
                {
                    ClimbSlope(ref velocity, slopeAngle, hit.normal, originalVelocityX);
                    continue;
                }

                if (IsClimbingSlope && slopeAngle <= _moveStats.MaxSlopeAngle)
                    continue;

                velocity.x = (hit.distance - COLLISION_PADDING) * directionX;
                rayLength = hit.distance;

                if (IsClimbingSlope)
                    velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
            }
        }
    }

    private void ResolveVerticalMovement(ref Vector2 velocity)
    {
        #region Ceiling Check
        if (velocity.y >= 0f)
        {
            float upwardRayLength = Mathf.Abs(velocity.y) + COLLISION_PADDING;
            for (int i = 0; i < NumOfVerticalRays; i++)
            {
                Vector2 rayOrigin = raycastCorners.topLeft;

                rayOrigin += Vector2.right * (_verticalRaySpace * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up, upwardRayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    float angle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));

                    if (hit.distance == 0f)
                    {
                        Vector2 shiftDirection = Vector2.zero;

                        if (i == 0) shiftDirection = 2f * COLLISION_PADDING * Vector2.right;
                        else if (i == NumOfVerticalRays - 1) shiftDirection = 2f * COLLISION_PADDING * Vector2.left;

                        if (shiftDirection != Vector2.zero)
                        {
                            Vector2 safetyOrigin = rayOrigin + shiftDirection;

                            RaycastHit2D safetyHit = Physics2D.Raycast(safetyOrigin, Vector2.up, upwardRayLength, _moveStats.GroundLayer);

                            if (safetyHit)
                                angle = Mathf.Round(Vector2.Angle(safetyHit.normal, Vector2.down));
                            else
                                angle = 90f;
                        }
                    }

                    if (angle > 85f)
                        continue;

                    if (hit.distance == 0f)
                        velocity.y = 0f;
                    else
                    {
                        velocity.y = (hit.distance - COLLISION_PADDING);
                        upwardRayLength = hit.distance;
                    }
                }
            }
        }
        #endregion

        #region Ground Check
        float downwardRayLength = Mathf.Abs(velocity.y) + COLLISION_PADDING;
        float smallestHitDistance = float.MaxValue;
        RaycastHit2D groundHit = new();
        bool foundGround = false;
        bool foundWalkableGround = false;

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = raycastCorners.bottomLeft;
            rayOrigin += Vector2.right * (_verticalRaySpace * i + velocity.x);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, downwardRayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float hitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isHitWalkable = hitAngle < _moveStats.MaxSlopeAngle;
                bool isSinking = hit.distance < COLLISION_PADDING;

                if (!isSinking && !isHitWalkable && _internalState.IsGrounded && _internalState.SlopeAngle < _moveStats.MaxSlopeAngle)
                    continue;

                if (!foundGround)
                {
                    smallestHitDistance = hit.distance;
                    groundHit = hit;
                    foundGround = true;
                    foundWalkableGround = isHitWalkable;
                }
                else
                {
                    if (!foundWalkableGround && isHitWalkable)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                        foundWalkableGround = true;
                    }
                    else if (foundWalkableGround == isHitWalkable && hit.distance < smallestHitDistance)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                    }
                }
            }
        }

        if (foundGround)
        {
            if (velocity.y <= 0f)
            {
                float distanceToFloor = groundHit.distance - COLLISION_PADDING;
                if (IsSliding && distanceToFloor > 0f && _internalState.SlopeAngle < 89.9f)
                {
                    float slopeAngleRad = _internalState.SlopeAngle * Mathf.Deg2Rad;
                    float pushOutX = distanceToFloor / Mathf.Tan(slopeAngleRad);
                    velocity.x += pushOutX * Mathf.Sign(_internalState.SlopeNormal.x);
                }

                float calculation = (groundHit.distance - COLLISION_PADDING) * -1f;
                if (calculation > 0f && calculation <= COLLISION_PADDING + 0.001f)
                    calculation = 0f;

                velocity.y = calculation;
            }
        }
        #endregion

        #region Climbing Slope Check
        if (IsClimbingSlope)
        {
            float directionX = Mathf.Sign(velocity.x);
            float rayLength = Mathf.Abs(velocity.x) + COLLISION_PADDING;
            Vector2 rayOrigin = ((directionX == -1) ? raycastCorners.bottomLeft : raycastCorners.bottomRight) + Vector2.up * velocity.y;
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                if (slopeAngle != SlopeAngle)
                {
                    velocity.x = (hit.distance - COLLISION_PADDING) * directionX;
                    SlopeAngle = slopeAngle;
                    SlopeNormal = hit.normal;
                }
            }
        }
        #endregion
    }

    private void UpdateRaycastCorners()
    {
        Bounds bounds = _collider.bounds;
        bounds.Expand(COLLISION_PADDING * -2);

        raycastCorners.bottomLeft = new(bounds.min.x, bounds.min.y);
        raycastCorners.bottomRight = new(bounds.max.x, bounds.min.y);
        raycastCorners.topLeft = new(bounds.min.x, bounds.max.y);
        raycastCorners.topRight = new(bounds.max.x, bounds.max.y);
    }

    private void CalculateRaySpacing()
    {
        Bounds bounds = _collider.bounds;
        bounds.Expand(COLLISION_PADDING * -2);

        _horizontalRaySpace = bounds.size.y / (NumOfHorizontalRays - 1);
        _verticalRaySpace = bounds.size.x / (NumOfVerticalRays - 1);
    }

    #region Slopes
    private void ClimbSlope(ref Vector2 velocity, float slopeAngle, Vector2 slopeNormal, float originalInputX)
    {
        float moveDistance = Mathf.Abs(originalInputX);
        float climbVelocityY = Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * moveDistance;

        if (velocity.y <= climbVelocityY)
        {
            velocity.y = climbVelocityY;
            velocity.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(velocity.x);

            IsClimbingSlope = true;
            SlopeAngle = slopeAngle;
            SlopeNormal = slopeNormal;
        }
    }

    private void DescendSlope(ref Vector2 velocity)
    {
        RaycastHit2D maxSlopeHitLeft = Physics2D.Raycast(raycastCorners.bottomLeft, Vector2.down, Mathf.Abs(velocity.y) + COLLISION_PADDING, _moveStats.GroundLayer);
        RaycastHit2D maxSlopeHitRight = Physics2D.Raycast(raycastCorners.bottomRight, Vector2.down, Mathf.Abs(velocity.y) + COLLISION_PADDING, _moveStats.GroundLayer);

        if (maxSlopeHitLeft ^ maxSlopeHitRight)
        {
            SlideDownMaxSlope(maxSlopeHitLeft, ref velocity);
            SlideDownMaxSlope(maxSlopeHitRight, ref velocity);
        }

        float directionX = FaceDirection;

        Vector2 rayOrigin = (directionX == -1) ? raycastCorners.bottomRight : raycastCorners.bottomLeft;
        float maxExpectedVerticalDrop = Mathf.Tan(_moveStats.MinAngleForWallSlide * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
        float dynamicRayLength = Mathf.Abs(velocity.y) + COLLISION_PADDING + maxExpectedVerticalDrop;
        float rayLength = Mathf.Max(dynamicRayLength, COLLISION_PADDING * 2f);

        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, _moveStats.GroundLayer);

        if (hit)
        {
            float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));

            if (slopeAngle >= _moveStats.MinAngleForWallSlide)
                return;

            ApplySlopeStick(ref velocity, slopeAngle, hit);
        }

        #region Debug Visualization
        if (_moveStats.DebugShowDescendSlopeRay)
        {
            bool isSticking = IsDescendingSlope;
            Color rayColor = Color.blue;

            if (hit && isSticking)
                rayColor = Color.green;
            else if (hit && IsSliding) rayColor = Color.yellow;
            else if (hit && !isSticking) rayColor = Color.red;

            float debugRayLength = hit ? hit.distance + _moveStats.ExtraRayDebugDistance : _moveStats.ExtraRayDebugDistance * 5f;
            Debug.DrawRay(rayOrigin, Vector2.down * debugRayLength, rayColor);
        }
        #endregion
    }

    private void ApplySlopeStick(ref Vector2 moveAmount, float slopeAngle, RaycastHit2D hit)
    {
        if (_playerMovement.IsWallSlideable(slopeAngle))
        {
            bool isFacingWall = Mathf.Sign(hit.normal.x) != FaceDirection;
            bool shouldWallSlide = isFacingWall || _moveStats.CanWallSlideFacingAwayFromWall;

            if (shouldWallSlide)
                return;
            else
                SlideDownMaxSlope(hit, ref moveAmount);
        }

        if (hit.distance - COLLISION_PADDING <= Mathf.Tan(slopeAngle * Mathf.Deg2Rad) * Mathf.Abs(moveAmount.x))
        {
            float moveDistance = Mathf.Abs(moveAmount.x);
            float descendAmountY = Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * moveDistance;

            moveAmount.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(moveAmount.x);
            moveAmount.y -= descendAmountY;

            IsDescendingSlope = true;
            SlopeNormal = hit.normal;
            SlopeAngle = slopeAngle;
        }
    }

    private void SlideDownMaxSlope(RaycastHit2D hit, ref Vector2 velocity)
    {
        if (hit)
        {
            float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
            int wallDirection = (int)Mathf.Sign(hit.normal.x);
            bool isFacingWall = (wallDirection == -1 && _playerMovement.IsFacingRight) || (wallDirection == 1 && !_playerMovement.IsFacingRight);
            bool isNormalSlideableSlope = slopeAngle > _moveStats.MaxSlopeAngle && slopeAngle < _moveStats.MinAngleForWallSlide;
            bool isWallSlope = slopeAngle >= _moveStats.MinAngleForWallSlide;

            if (isNormalSlideableSlope || (isWallSlope && !isFacingWall))
            {
                float tanAngle = Mathf.Clamp(slopeAngle, 0f, 89.9f);

                velocity.x = Mathf.Sign(hit.normal.x) * (Mathf.Abs(velocity.y) - hit.distance) / Mathf.Tan(tanAngle * Mathf.Deg2Rad);

                IsDescendingSlope = true;
                SlopeAngle = slopeAngle;
                SlopeNormal = hit.normal;
            }
        }
    }
    #endregion

    #region Helper Methods
    public bool IsGrounded() => _internalState.IsGrounded;
    public bool BumpedHead() => _internalState.IsHittingCeiling;
    public bool IsTouchingWall() => _internalState.IsAgainstWall;
    public int GetWallDirection() => _internalState.WallDirection;
    #endregion
}

#if UNITY_EDITOR
[CustomEditor(typeof(MovementController))]
public class MovementControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        MovementController controller = (MovementController)target;

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Collision State (Read Only)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);

            EditorGUILayout.Toggle("Is Grounded", controller.State.IsGrounded);
            EditorGUILayout.Toggle("Is Against Wall", controller.State.IsAgainstWall);
            EditorGUILayout.IntField("Wall Direction", controller.State.WallDirection);
            EditorGUILayout.FloatField("Wall Angle", controller.State.WallAngle);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is On Slope", controller.State.IsOnSlope);
            EditorGUILayout.FloatField("Slope Angle", controller.State.SlopeAngle);
            EditorGUILayout.Vector2Field("Slope Normal", controller.State.SlopeNormal);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Against Steep Slope", controller.State.IsAgainstSteepSlope);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Hitting Ceiling", controller.State.IsHittingCeiling);
            EditorGUILayout.FloatField("Ceiling Angle", controller.State.CeilingAngle);
            EditorGUILayout.Vector2Field("Ceiling Normal", controller.State.CeilingNormal);
            EditorGUILayout.IntField("Head Bump Slide Direction", controller.State.HeadBumpSlideDirection);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Sliding", controller.IsSliding);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Descending Slope", controller.IsDescendingSlope);
            EditorGUILayout.Toggle("Is Climbing Slope", controller.IsClimbingSlope);

            EditorGUI.EndDisabledGroup();

            Repaint();
        }
    }
}
#endif