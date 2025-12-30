using UnityEngine;

public class MovementController : MonoBehaviour
{
    public const float COLLISION_PADDING = 0.015f;

    [field: SerializeField, Range(2, 100)] public int NumOfHorizontalRays { get; private set; } = 4;
    [field: SerializeField, Range(2, 100)] public int NumOfVerticalRays { get; private set; } = 4;
    public bool IsCollidingAbove { get; private set; }
    public bool IsCollidingBelow { get; private set; }
    public bool IsCollidingLeft { get; private set; }
    public bool IsCollidingRight { get; private set; }

    public int HeadBumpSlideDirection { get; private set; }
    public bool IsHittingCeilingCenter {  get; private set; }
    public bool IsHittingBothCorners {  get; private set; }

    public RaycastCorners raycastCorners;

    private float _horizontalRaySpace;
    private float _verticalRaySpace;

    private BoxCollider2D _collider;
    private PlayerMovementStats _moveStats;
    private PlayerMovement _playerMovement;
    private Rigidbody2D _rb;

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
    }

    private void Start()
    {
        CalculateRaySpacing();
    }

    public void Move(Vector2 velocity)
    {
        UpdateRaycastCorners();
        ResetCollisionStates();
        CheckCeilingBoxCast(velocity);

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
            IsHittingCeilingCenter = true;

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

    private void ResetCollisionStates()
    {
        IsCollidingAbove = false;
        IsCollidingBelow = false;
        IsCollidingLeft = false;
        IsCollidingRight = false;
        HeadBumpSlideDirection = 0;
        IsHittingCeilingCenter = false;
        IsHittingBothCorners = false;
    }

    private void ResolveHorizontalMovement(ref Vector2 velocity)
    {
        float directionX = Mathf.Sign(velocity.x);
        float rayLength = Mathf.Abs(velocity.x) + COLLISION_PADDING;

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOrigin = (directionX == -1) ? raycastCorners.bottomLeft : raycastCorners.bottomRight;
            rayOrigin += Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                velocity.x = (hit.distance - COLLISION_PADDING) * directionX;
                rayLength = hit.distance;

                if (directionX == -1)
                    IsCollidingLeft = true;
                else if (directionX == 1)
                    IsCollidingRight = true;
            }

            #region Debug Visualization
            if (_moveStats.DebugShowWallHit)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = (directionX  == -1) ? raycastCorners.bottomLeft : raycastCorners.bottomRight;
                debugRayOrigin += Vector2.up * (_horizontalRaySpace * i);

                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.right * directionX, debugRayLength, _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(debugRayOrigin, debugRayLength * directionX * Vector2.right, rayColor);
            }
            #endregion
        }
    }

    private void ResolveVerticalMovement(ref Vector2 velocity)
    {
        float directionY = Mathf.Sign(velocity.y);
        float rayLength = Mathf.Abs(velocity.y) + COLLISION_PADDING;
        bool hitLeftCorner = false;
        bool hitRightCorner = false;

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = (directionY == -1) ? raycastCorners.bottomLeft : raycastCorners.topLeft;
            rayOrigin += Vector2.right * (_verticalRaySpace * i + velocity.x);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up * directionY, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                velocity.y = (hit.distance - COLLISION_PADDING) * directionY;
                rayLength = hit.distance;

                if (directionY == -1)
                    IsCollidingBelow = true;
                else
                {
                    IsCollidingAbove = true;

                    if (i == 0) hitLeftCorner = true;
                    if (i == NumOfVerticalRays - 1) hitRightCorner = true;

                    if (_moveStats.UseHeadBumpSlide)
                    {
                        int slideDirection = 0;
                        if (i == 0) slideDirection = 1;
                        else if (i == NumOfVerticalRays - 1) slideDirection = -1;

                        if (slideDirection != 0)
                        {
                            Vector2 slideCheckRayOrigin = hit.point + (2 * COLLISION_PADDING * Vector2.down);
                            float slideCheckRayLength = COLLISION_PADDING * 2;
                            RaycastHit2D slideCheckHit = Physics2D.Raycast(slideCheckRayOrigin, Vector2.right * slideDirection, slideCheckRayLength, _moveStats.GroundLayer);

                            if (!slideCheckHit)
                                HeadBumpSlideDirection = slideDirection;
                        }
                    }
                }
            }

            #region Debug Visualization
            if (_moveStats.DebugShowIsGrounded)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = raycastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i);

                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.down, debugRayLength, _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(debugRayOrigin, Vector2.down * debugRayLength, rayColor);
            }

            if (_moveStats.DebugShowHeadRays)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = raycastCorners.topLeft + Vector2.right * (_verticalRaySpace * i);

                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.up, debugRayLength, _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;

                if (i == 0 || i == NumOfVerticalRays - 1)
                {
                    rayColor = didHit ? Color.green : Color.magenta;
                }

                Debug.DrawRay(debugRayOrigin, Vector2.up * debugRayLength, rayColor);
            }
            #endregion
        }

        IsHittingBothCorners = hitLeftCorner && hitRightCorner;
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

    #region Helper Methods
    public bool IsGrounded() => IsCollidingBelow;
    public bool BumpedHead() => IsCollidingAbove;
    public bool IsTouchingWall(bool isFacingRight) => (isFacingRight && IsCollidingRight) || (!isFacingRight && IsCollidingLeft);
    public int GetWallDirection()
    {
        if (IsCollidingLeft) return -1;
        if (IsCollidingRight) return 1;
        return 0;
    }
    #endregion
}