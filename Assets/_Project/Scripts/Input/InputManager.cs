using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public Vector2 MoveVector { get; private set; }
    public bool JumpWasPressed { get; private set; }
    public bool JumpIsHeld { get; private set; }
    public bool JumpWasReleased { get; private set; }
    public bool RunIsHeld { get; private set; }
    public bool DashWasPressed { get; private set; }

    private GameInput _gameInput;
    private InputAction _moveAction;
    private InputAction _sprintAction;
    private InputAction _jumpAction;
    private InputAction _dashAction;

    private void Awake()
    {
        if (Instance != this && Instance != null)
            Destroy(gameObject);

        Instance = this;

        _gameInput = new();

        _moveAction = _gameInput.Gameplay.Move;
        _sprintAction = _gameInput.Gameplay.Sprint;
        _jumpAction = _gameInput.Gameplay.Jump;
        _dashAction = _gameInput.Gameplay.Dash;
    }

    private void OnEnable()
    {
        _gameInput.Enable();
    }

    private void Update()
    {
        MoveVector = _moveAction.ReadValue<Vector2>();

        JumpWasPressed = _jumpAction.WasPressedThisFrame();
        JumpIsHeld = _jumpAction.IsPressed();
        JumpWasReleased = _jumpAction.WasReleasedThisFrame();

        RunIsHeld = _sprintAction.IsPressed();

        DashWasPressed = _dashAction.WasPressedThisFrame();
    }

    private void OnDisable()
    {
        _gameInput.Disable();
    }
}