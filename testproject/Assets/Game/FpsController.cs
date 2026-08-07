using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class FpsController : MonoBehaviour
{
    public float MoveSpeed = 6f;
    public float JumpHeight = 1.15f;
    public float TimeToApex = 0.35f;
    public float FallMultiplier = 1.8f;
    public float LookSensitivity = 0.12f;

    CharacterController _controller;
    Transform _eye;
    float _vertical;
    float _pitch;

    float Gravity => -2f * JumpHeight / (TimeToApex * TimeToApex);
    float JumpVelocity => 2f * JumpHeight / TimeToApex;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        var camera = GetComponentInChildren<Camera>();
        if (camera != null) _eye = camera.transform;
    }

    void Update()
    {
        var move = Vector2.zero;
        var look = Vector2.zero;
        var jump = false;

#if ENABLE_INPUT_SYSTEM
        var pad = Gamepad.current;
        if (pad != null)
        {
            move += pad.leftStick.ReadValue();
            look += pad.rightStick.ReadValue() * 4f;
            jump |= pad.buttonSouth.isPressed;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            jump |= keyboard.spaceKey.isPressed;
        }

        var mouse = Mouse.current;
        if (mouse != null) look += mouse.delta.ReadValue();
#endif

        if (_eye != null)
        {
            transform.Rotate(Vector3.up, look.x * LookSensitivity, Space.World);
            _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, -89f, 89f);
            _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        if (_controller.isGrounded)
        {
            _vertical = -2f;
            if (jump) _vertical = JumpVelocity;
        }
        else
        {
            _vertical += Gravity * (_vertical < 0f ? FallMultiplier : 1f) * Time.deltaTime;
        }

        var motion = (transform.right * move.x + transform.forward * move.y) * MoveSpeed;
        motion.y = _vertical;
        _controller.Move(motion * Time.deltaTime);
    }
}
