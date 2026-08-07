using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProvingGround.Samples
{
    /// <summary>
    /// A first-person controller with nothing clever in it, written the way a real one is
    /// written. Proving Ground drives it through synthetic input without the controller
    /// knowing anything about the harness, which is the point of the sample.
    ///
    /// The tuning fields correspond one to one with the metrics in the sample feel spec,
    /// so you can change a number here, re-run the scenario, and watch the diff move.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PgSampleFirstPersonController : MonoBehaviour
    {
        [Header("Locomotion")]
        [Tooltip("Ground speed in metres per second. Genre norm for a shooter is 5-8.")]
        public float MoveSpeed = 6f;

        [Tooltip("Seconds to reach full speed from a standstill. Above ~0.12s reads as ice.")]
        public float AccelerationTime = 0.08f;

        [Tooltip("Seconds to come to rest from full speed.")]
        public float DecelerationTime = 0.08f;

        [Header("Jump")]
        [Tooltip("Peak height in metres. Genre norm for a shooter is 0.9-1.4.")]
        public float JumpHeight = 1.15f;

        [Tooltip("Seconds from leaving the ground to the top of the arc.")]
        public float TimeToApex = 0.35f;

        [Tooltip("Gravity multiplier while descending. 1 gives a symmetric, floaty arc.")]
        public float FallMultiplier = 1.8f;

        [Tooltip("Grace period after leaving a ledge during which a jump still works.")]
        public float CoyoteTime = 0.08f;

        [Tooltip("How long an early jump press is remembered and fired when it becomes legal.")]
        public float JumpBuffer = 0.1f;

        [Header("Look")]
        public float LookSensitivity = 0.12f;
        public float MaxPitch = 89f;

        [Header("References")]
        [Tooltip("Leave empty to use the first child camera.")]
        public Transform Eye;

        CharacterController _controller;
        Vector3 _velocity;
        Vector3 _smoothedHorizontal;
        float _verticalVelocity;
        float _pitch;
        float _lastGroundedTime = -99f;
        float _lastJumpPressedTime = -99f;
        bool _wasGrounded;

        float Gravity => -2f * JumpHeight / (TimeToApex * TimeToApex);
        float JumpVelocity => 2f * JumpHeight / TimeToApex;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (Eye == null && GetComponentInChildren<Camera>() != null)
                Eye = GetComponentInChildren<Camera>().transform;
        }

        void Update()
        {
            ReadInput(out var move, out var look, out var jumpPressed, out var jumpHeld);

            Look(look);
            Move(move);
            Jump(jumpPressed, jumpHeld);

            _velocity = _smoothedHorizontal;
            _velocity.y = _verticalVelocity;
            _controller.Move(_velocity * Time.deltaTime);
        }

        void ReadInput(out Vector2 move, out Vector2 look, out bool jumpPressed, out bool jumpHeld)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jumpPressed = false;
            jumpHeld = false;

#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                look += pad.rightStick.ReadValue() * 4f;
                jumpPressed |= pad.buttonSouth.wasPressedThisFrame;
                jumpHeld |= pad.buttonSouth.isPressed;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed) move.x -= 1f;
                jumpPressed |= keyboard.spaceKey.wasPressedThisFrame;
                jumpHeld |= keyboard.spaceKey.isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null) look += mouse.delta.ReadValue();
#endif

            move = Vector2.ClampMagnitude(move, 1f);
        }

        void Look(Vector2 look)
        {
            if (Eye == null) return;

            transform.Rotate(Vector3.up, look.x * LookSensitivity, Space.World);
            _pitch = Mathf.Clamp(_pitch - look.y * LookSensitivity, -MaxPitch, MaxPitch);
            Eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move(Vector2 move)
        {
            var desired = (transform.right * move.x + transform.forward * move.y) * MoveSpeed;

            // Separate ramps for starting and stopping: a single smoothing constant makes
            // the character feel identical to accelerate and to halt, which is rarely wanted.
            var ramp = desired.sqrMagnitude > _smoothedHorizontal.sqrMagnitude
                ? AccelerationTime
                : DecelerationTime;

            _smoothedHorizontal = ramp <= 0f
                ? desired
                : Vector3.MoveTowards(_smoothedHorizontal, desired, MoveSpeed / ramp * Time.deltaTime);
        }

        void Jump(bool jumpPressed, bool jumpHeld)
        {
            var grounded = _controller.isGrounded;
            if (grounded) _lastGroundedTime = Time.time;
            if (jumpPressed) _lastJumpPressedTime = Time.time;

            if (grounded && _verticalVelocity < 0f) _verticalVelocity = -2f;

            var withinCoyote = Time.time - _lastGroundedTime <= CoyoteTime;
            var withinBuffer = Time.time - _lastJumpPressedTime <= JumpBuffer;

            if (withinCoyote && withinBuffer)
            {
                _verticalVelocity = JumpVelocity;
                _lastJumpPressedTime = -99f;
                _lastGroundedTime = -99f;
            }

            // Falling faster than rising is what stops a jump reading as floaty.
            var gravity = Gravity * (_verticalVelocity < 0f || !jumpHeld ? FallMultiplier : 1f);
            _verticalVelocity += gravity * Time.deltaTime;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -50f);

            _wasGrounded = grounded;
        }
    }
}
