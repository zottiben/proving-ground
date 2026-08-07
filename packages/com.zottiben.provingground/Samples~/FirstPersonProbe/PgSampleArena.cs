using UnityEngine;

namespace ProvingGround.Samples
{
    /// <summary>
    /// Builds a small test arena at runtime: floor, walls, a step, a ledge, a gap, and an
    /// objective on the far side.
    ///
    /// It is generated in code rather than shipped as a scene so the sample cannot break
    /// when serialized references drift, and so the geometry that the probe is meant to
    /// find problems in is right here in readable form. The gap is deliberate: run the
    /// probe bot and it should eventually fall in.
    /// </summary>
    public sealed class PgSampleArena : MonoBehaviour
    {
        [Tooltip("Leave a hole in the floor, so the probe bot has something to find.")]
        public bool IncludeGap = true;

        [Tooltip("Spawn a player if none is present.")]
        public bool SpawnPlayer = true;

        void Awake()
        {
            BuildFloor();
            BuildWalls();
            BuildObstacles();
            BuildObjective();
            if (SpawnPlayer && GameObject.FindGameObjectWithTag("Player") == null) BuildPlayer();
        }

        void BuildFloor()
        {
            // Four quadrants rather than one plane, so a gap can be left in the middle.
            for (var x = -1; x <= 1; x += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                if (IncludeGap && x == 1 && z == 1) continue;

                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"Floor_{x}_{z}";
                tile.transform.SetParent(transform);
                tile.transform.localScale = new Vector3(20, 1, 20);
                tile.transform.position = new Vector3(x * 10, -0.5f, z * 10);
                tile.isStatic = true;
            }
        }

        void BuildWalls()
        {
            var positions = new[]
            {
                new Vector3(0, 2, 21), new Vector3(0, 2, -21),
                new Vector3(21, 2, 0), new Vector3(-21, 2, 0)
            };

            for (var i = 0; i < positions.Length; i++)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall_" + i;
                wall.transform.SetParent(transform);
                wall.transform.position = positions[i];
                wall.transform.localScale = i < 2 ? new Vector3(44, 5, 2) : new Vector3(2, 5, 44);
                wall.isStatic = true;
            }
        }

        void BuildObstacles()
        {
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Step";
            step.transform.SetParent(transform);
            step.transform.position = new Vector3(-6, 0.15f, 4);
            step.transform.localScale = new Vector3(4, 0.3f, 4);
            step.isStatic = true;

            var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = "Ledge";
            ledge.transform.SetParent(transform);
            ledge.transform.position = new Vector3(6, 0.5f, -6);
            ledge.transform.localScale = new Vector3(6, 1f, 6);
            ledge.isStatic = true;
        }

        void BuildObjective()
        {
            var objective = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            objective.name = "Objective";
            objective.transform.SetParent(transform);
            objective.transform.position = new Vector3(-14, 1f, -14);
            objective.GetComponent<Collider>().isTrigger = true;
        }

        void BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0, 1.2f, -12);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.stepOffset = 0.4f;
            controller.slopeLimit = 50f;

            var eye = new GameObject("Eye");
            eye.transform.SetParent(player.transform);
            eye.transform.localPosition = new Vector3(0, 0.7f, 0);
            var camera = eye.AddComponent<Camera>();
            camera.tag = "MainCamera";

            var fps = player.AddComponent<PgSampleFirstPersonController>();
            fps.Eye = eye.transform;

            // Tagging the player is what lets Proving Ground find it without configuration.
            try
            {
                player.tag = "Player";
            }
            catch (UnityException)
            {
                Debug.LogWarning("[ProvingGround sample] The 'Player' tag does not exist in this project. " +
                                 "Add it, or set PgLocate.PlayerOverride.");
            }
        }
    }
}
