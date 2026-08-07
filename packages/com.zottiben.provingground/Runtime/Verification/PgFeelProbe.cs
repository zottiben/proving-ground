using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ProvingGround.Actuation;
using ProvingGround.Perception;

namespace ProvingGround.Verification
{
    /// <summary>
    /// Watches the player during a run and derives the numbers a feel spec is written
    /// against.
    ///
    /// Everything here is measured from observed motion rather than read out of the
    /// controller's fields. That is deliberate: reading the intended jump height from a
    /// serialized field proves the field, whereas measuring the arc proves the game.
    /// </summary>
    public sealed class PgFeelProbe
    {
        readonly List<float> _frameTimes = new List<float>();
        readonly List<float> _jumpApexHeights = new List<float>();
        readonly List<float> _jumpAirtimes = new List<float>();
        readonly List<float> _jumpTimesToApex = new List<float>();
        readonly List<float> _hitstopDurations = new List<float>();
        readonly List<int> _moveLatencies = new List<int>();

        Transform _player;
        CharacterController _controller;

        Vector3 _lastPosition;
        float _maxHorizontalSpeed;
        float _currentSpeed;

        bool _wasGrounded = true;
        float _takeoffTime;
        float _takeoffY;
        float _apexY;
        float _apexTime;

        // Acceleration and stopping are measured across the first clean transition of each
        // kind; later ones are usually contaminated by terrain or by the scenario turning.
        float _accelStartTime = -1f;
        float _accelTime = -1f;
        float _stopStartTime = -1f;
        float _stopTime = -1f;

        Vector2 _lastCommandedMove;
        int _moveCommandFrame = -1;
        float _speedAtCommand;

        float _hitstopStart = -1f;
        float _leftGroundTime = -1f;
        float _coyoteObserved = -1f;

        // Frame time is taken from a real clock rather than from Time.unscaledDeltaTime,
        // because a run under a captured clock reports whatever step it was told to use and
        // would otherwise show a flawless frame rate on a game that stutters.
        readonly System.Diagnostics.Stopwatch _frameTimer = new System.Diagnostics.Stopwatch();
        double _lastFrameMs;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// True when the clock was driven by frame count during the run, which makes the
        /// gameplay timings exact and the performance numbers unrepresentative.
        /// </summary>
        public bool TimeWasCaptured { get; private set; }

        public void Begin(Transform player = null)
        {
            _player = player != null ? player : PgLocate.Player();
            _controller = _player != null ? _player.GetComponent<CharacterController>() : null;

            _frameTimes.Clear();
            _jumpApexHeights.Clear();
            _jumpAirtimes.Clear();
            _jumpTimesToApex.Clear();
            _hitstopDurations.Clear();
            _moveLatencies.Clear();

            _maxHorizontalSpeed = 0f;
            _accelStartTime = _accelTime = _stopStartTime = _stopTime = -1f;
            _coyoteObserved = -1f;
            _leftGroundTime = -1f;
            _hitstopStart = -1f;
            _moveCommandFrame = -1;

            if (_player != null) _lastPosition = _player.position;
            _wasGrounded = IsGrounded();
            TimeWasCaptured = Time.captureDeltaTime > 0f;

            _frameTimer.Restart();
            _lastFrameMs = 0;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;

        /// <summary>Call once per frame while a scenario runs.</summary>
        public void Tick()
        {
            if (!IsRunning) return;

            var elapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
            var frameMs = elapsedMs - _lastFrameMs;
            _lastFrameMs = elapsedMs;
            if (frameMs > 0) _frameTimes.Add((float)frameMs);

            // Gameplay timings still come from the game's own clock, which is what the
            // player experiences and what the feel spec is written against.
            var dt = Time.deltaTime;

            TrackHitstop();

            if (_player == null)
            {
                _player = PgLocate.Player();
                if (_player == null) return;
                _lastPosition = _player.position;
                _controller = _player.GetComponent<CharacterController>();
            }

            var position = _player.position;
            var delta = position - _lastPosition;
            var horizontal = new Vector3(delta.x, 0f, delta.z);
            _currentSpeed = dt > 0f ? horizontal.magnitude / dt : 0f;
            _maxHorizontalSpeed = Mathf.Max(_maxHorizontalSpeed, _currentSpeed);

            TrackInputLatency();
            TrackAcceleration();
            TrackJump(position);

            _lastPosition = position;
        }

        bool IsGrounded()
        {
            if (_controller != null) return _controller.isGrounded;
            if (_player == null) return true;

            // Fall back to a short downward probe from just above the pivot.
            var origin = _player.position + Vector3.up * 0.1f;
            return Physics.Raycast(origin, Vector3.down, 0.35f);
        }

        void TrackHitstop()
        {
            var scale = Time.timeScale;
            if (scale < 0.99f && _hitstopStart < 0f)
            {
                _hitstopStart = Time.unscaledTime;
            }
            else if (scale >= 0.99f && _hitstopStart >= 0f)
            {
                _hitstopDurations.Add(Time.unscaledTime - _hitstopStart);
                _hitstopStart = -1f;
            }
        }

        /// <summary>
        /// Frames between the probe commanding movement from a standstill and the player
        /// actually moving. This is the number players describe as "responsiveness".
        /// </summary>
        void TrackInputLatency()
        {
            PgInput.CurrentSticks.TryGetValue(PgInput.StickMove, out var move);

            if (move != _lastCommandedMove)
            {
                if (move.sqrMagnitude > 0.01f && _lastCommandedMove.sqrMagnitude <= 0.01f)
                {
                    _moveCommandFrame = Time.frameCount;
                    _speedAtCommand = _currentSpeed;
                }

                _lastCommandedMove = move;
            }

            if (_moveCommandFrame >= 0 && _currentSpeed > _speedAtCommand + 0.05f)
            {
                _moveLatencies.Add(Time.frameCount - _moveCommandFrame);
                _moveCommandFrame = -1;
            }
        }

        void TrackAcceleration()
        {
            var target = _maxHorizontalSpeed * 0.9f;

            if (_accelTime < 0f)
            {
                if (_accelStartTime < 0f && _currentSpeed < 0.1f && _lastCommandedMove.sqrMagnitude > 0.01f)
                    _accelStartTime = Time.time;
                else if (_accelStartTime >= 0f && target > 0.1f && _currentSpeed >= target)
                    _accelTime = Time.time - _accelStartTime;
            }

            if (_stopTime < 0f)
            {
                if (_stopStartTime < 0f && _currentSpeed > target && target > 0.1f &&
                    _lastCommandedMove.sqrMagnitude <= 0.01f)
                    _stopStartTime = Time.time;
                else if (_stopStartTime >= 0f && _currentSpeed < 0.1f)
                    _stopTime = Time.time - _stopStartTime;
            }
        }

        void TrackJump(Vector3 position)
        {
            var grounded = IsGrounded();

            if (_wasGrounded && !grounded)
            {
                _takeoffTime = Time.time;
                _takeoffY = position.y;
                _apexY = position.y;
                _apexTime = Time.time;
                _leftGroundTime = Time.time;
            }
            else if (!grounded)
            {
                if (position.y > _apexY)
                {
                    _apexY = position.y;
                    _apexTime = Time.time;
                }

                // A jump that starts after the player has already left the ground is
                // evidence of coyote time, and its length is how long the window is.
                if (_leftGroundTime >= 0f && PgInput.IsPressed("jump"))
                {
                    _coyoteObserved = Mathf.Max(_coyoteObserved, Time.time - _leftGroundTime);
                    _leftGroundTime = -1f;
                }
            }
            else if (!_wasGrounded)
            {
                var airtime = Time.time - _takeoffTime;
                var apexHeight = _apexY - _takeoffY;

                // Ignore steps off a ledge: those are falls, not jumps.
                if (apexHeight > 0.05f)
                {
                    _jumpAirtimes.Add(airtime);
                    _jumpApexHeights.Add(apexHeight);
                    _jumpTimesToApex.Add(_apexTime - _takeoffTime);
                    PgEventLog.Record(PgEventLog.ChannelGameplay, "feel.jump",
                        $"apex={apexHeight:0.###}m airtime={airtime:0.###}s");
                }

                _leftGroundTime = -1f;
            }

            _wasGrounded = grounded;
        }

        /// <summary>
        /// The measurements, keyed by the ids a feel spec uses. Metrics with no observation
        /// are absent rather than zero, so a spec diff can tell "not measured" from "measured
        /// as nothing".
        /// </summary>
        public Dictionary<string, double> Results()
        {
            var results = new Dictionary<string, double>();

            if (_maxHorizontalSpeed > 0.01f)
                results["locomotion.moveSpeed"] = Round(_maxHorizontalSpeed);
            if (_accelTime >= 0f) results["locomotion.accelTime"] = Round(_accelTime);
            if (_stopTime >= 0f) results["locomotion.stopTime"] = Round(_stopTime);

            if (_jumpApexHeights.Count > 0)
            {
                results["jump.apexHeight"] = Round(_jumpApexHeights.Average());
                results["jump.airtime"] = Round(_jumpAirtimes.Average());
                results["jump.timeToApex"] = Round(_jumpTimesToApex.Average());
                results["jump.count"] = _jumpApexHeights.Count;
            }

            if (_coyoteObserved >= 0f) results["jump.coyoteTime"] = Round(_coyoteObserved);

            if (_moveLatencies.Count > 0)
                results["input.moveLatency"] = Round(_moveLatencies.Average());

            if (_hitstopDurations.Count > 0)
                results["combat.hitstop"] = Round(_hitstopDurations.Average());

            if (_frameTimes.Count > 0)
            {
                var sorted = _frameTimes.OrderBy(t => t).ToList();
                results["perf.frameTimeMean"] = Round(sorted.Average());
                results["perf.frameTimeP95"] = Round(Percentile(sorted, 0.95f));
                results["perf.frameTimeMax"] = Round(sorted[sorted.Count - 1]);
                results["perf.frameCount"] = sorted.Count;
            }

            return results;
        }

        /// <summary>
        /// Frame timings only describe what a player would see when the run was not driven
        /// by a captured clock, and when the renderer was actually running.
        /// </summary>
        public bool PerformanceIsRepresentative => !TimeWasCaptured && !Application.isBatchMode;

        internal static float Percentile(IReadOnlyList<float> sortedAscending, float percentile)
        {
            if (sortedAscending.Count == 0) return 0f;
            var index = Mathf.Clamp(
                Mathf.CeilToInt(percentile * sortedAscending.Count) - 1,
                0, sortedAscending.Count - 1);
            return sortedAscending[index];
        }

        static double Round(double value) => System.Math.Round(value, 4);
    }
}
