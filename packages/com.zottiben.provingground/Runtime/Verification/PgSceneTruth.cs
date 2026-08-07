using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if PG_NAVMESH
using UnityEngine.AI;
#endif

namespace ProvingGround.Verification
{
    /// <summary>
    /// Answers the questions about a level that a screenshot cannot: can the player get
    /// there, is there floor under the playable area, do the spawns work, and is the
    /// navigable space one connected region or several.
    /// </summary>
    public static class PgSceneTruth
    {
        /// <summary>
        /// Runs every scene check against the open scene.
        /// </summary>
        /// <param name="objectiveTags">Tags whose objects must be reachable from a spawn.</param>
        public static PgReport Analyze(IEnumerable<string> objectiveTags = null)
        {
            var report = new PgReport("scene");
            var scene = SceneManager.GetActiveScene();
            report.Datum("scene", scene.name);

            CheckSpawns(report);
            CheckFloor(report);
#if PG_NAVMESH
            CheckNavMesh(report, objectiveTags);
#else
            report.Add(PgFinding.Info("scene.navmesh.unavailable",
                "The AI module is not installed, so reachability was not analysed"));
#endif
            return report;
        }

        /// <summary>Spawn points that would drop the player into geometry or into space.</summary>
        static void CheckSpawns(PgReport report)
        {
            var spawns = new List<Transform>();
            foreach (var tag in new[] { "Respawn", "PlayerSpawn", "SpawnPoint" })
            {
                try
                {
                    spawns.AddRange(GameObject.FindGameObjectsWithTag(tag).Select(g => g.transform));
                }
                catch (UnityException)
                {
                    // Tag is not defined in this project, which is not itself a problem.
                }
            }

            if (spawns.Count == 0)
            {
                report.Add(PgFinding.Info("scene.spawns.none",
                    "No objects tagged Respawn, PlayerSpawn or SpawnPoint were found"));
                return;
            }

            foreach (var spawn in spawns)
            {
                var position = spawn.position;

                // Inside geometry: a small sphere at the spawn overlaps a non-trigger collider.
                var overlaps = Physics.OverlapSphere(position + Vector3.up * 0.9f, 0.35f)
                    .Where(c => !c.isTrigger)
                    .ToList();

                if (overlaps.Count > 0)
                {
                    report.Add(PgFinding
                        .Fail("scene.spawn.blocked", $"Spawn '{spawn.name}' is inside {overlaps[0].name}")
                        .At(Perception.PgViewDigest.PathOf(spawn))
                        .Fix("Move the spawn clear of geometry; the player will be ejected or trapped."));
                }

                if (!Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out _, 50f))
                {
                    report.Add(PgFinding
                        .Fail("scene.spawn.noFloor", $"Spawn '{spawn.name}' has no floor within 50m below it")
                        .At(Perception.PgViewDigest.PathOf(spawn))
                        .Fix("The player will fall on spawn. Add floor, or move the spawn."));
                }
            }
        }

        /// <summary>
        /// Casts a grid of rays down over the bounds of the level's colliders, looking for
        /// gaps a player could fall through. Holes in collision are invisible in the
        /// Editor and obvious to the first person who walks into one.
        /// </summary>
        static void CheckFloor(PgReport report, float spacing = 4f, int maxSamples = 4000)
        {
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => !c.isTrigger && c.gameObject.isStatic)
                .ToList();

            if (colliders.Count == 0)
            {
                report.Add(PgFinding.Info("scene.floor.noStatic",
                    "No static colliders found, so the floor was not sampled"));
                return;
            }

            var bounds = colliders[0].bounds;
            foreach (var collider in colliders) bounds.Encapsulate(collider.bounds);

            var columns = Mathf.CeilToInt(bounds.size.x / spacing);
            var rows = Mathf.CeilToInt(bounds.size.z / spacing);

            if (columns * rows > maxSamples)
            {
                // Widen the grid rather than sampling forever on a large world.
                spacing = Mathf.Sqrt(bounds.size.x * bounds.size.z / maxSamples);
                columns = Mathf.CeilToInt(bounds.size.x / spacing);
                rows = Mathf.CeilToInt(bounds.size.z / spacing);
            }

            var holes = new List<Vector3>();
            var top = bounds.max.y + 5f;
            var depth = bounds.size.y + 20f;

            for (var i = 0; i <= columns; i++)
            for (var j = 0; j <= rows; j++)
            {
                var x = bounds.min.x + i * spacing;
                var z = bounds.min.z + j * spacing;
                var origin = new Vector3(x, top, z);
                if (!Physics.Raycast(origin, Vector3.down, depth)) holes.Add(new Vector3(x, 0f, z));
            }

            var sampled = (columns + 1) * (rows + 1);
            report.Datum("floorSamples", sampled);
            report.Datum("floorHoles", holes.Count);

            // Some empty space at the edges of the bounding box is normal; a large
            // fraction of it is not.
            var ratio = sampled > 0 ? (float)holes.Count / sampled : 0f;
            if (ratio > 0.5f)
            {
                report.Add(PgFinding
                    .Info("scene.floor.sparse",
                        $"{ratio:P0} of the sampled area has nothing below it, which is normal for a level that does not fill its bounding box")
                    .Datum("holes", holes.Count));
            }
            else if (holes.Count > 0)
            {
                foreach (var hole in holes.Take(10))
                {
                    report.Add(PgFinding
                        .Warn("scene.floor.hole", "No collision below this point inside the playable bounds")
                        .At($"({hole.x:0.#}, ~, {hole.z:0.#})")
                        .Fix("Confirm this is intended. If the player can walk here, they will fall through."));
                }

                if (holes.Count > 10)
                    report.Add(PgFinding.Info("scene.floor.moreHoles",
                        $"{holes.Count - 10} further points with no collision below them"));
            }
        }

#if PG_NAVMESH
        /// <summary>
        /// Splits the baked navmesh into connected regions and checks that objectives can
        /// be walked to from a spawn.
        /// </summary>
        static void CheckNavMesh(PgReport report, IEnumerable<string> objectiveTags)
        {
            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            {
                report.Add(PgFinding.Info("scene.navmesh.none",
                    "No navmesh is baked in this scene, so reachability was not analysed"));
                return;
            }

            // Sample one point per triangle centroid, then group by mutual reachability.
            var samples = new List<Vector3>();
            for (var i = 0; i + 2 < triangulation.indices.Length; i += 3)
            {
                var centroid = (triangulation.vertices[triangulation.indices[i]] +
                                triangulation.vertices[triangulation.indices[i + 1]] +
                                triangulation.vertices[triangulation.indices[i + 2]]) / 3f;
                samples.Add(centroid);
            }

            report.Datum("navmeshTriangles", samples.Count);

            var representatives = Decimate(samples, 200);
            var islands = GroupIntoIslands(representatives);

            report.Datum("navmeshIslands", islands.Count);

            if (islands.Count > 1)
            {
                var sizes = string.Join(", ", islands.Select(i => i.Count));
                report.Add(PgFinding
                    .Warn("scene.navmesh.islands",
                        $"The navmesh is split into {islands.Count} disconnected regions (sample counts: {sizes})")
                    .Fix("Agents cannot path between regions. Add off-mesh links, or close the gap."));
            }

            var tags = (objectiveTags ?? new[] { "Objective", "Interactable", "Pickup" }).ToList();
            var origin = FindReachabilityOrigin(representatives);
            if (!origin.HasValue) return;

            foreach (var tag in tags)
            {
                GameObject[] objects;
                try
                {
                    objects = GameObject.FindGameObjectsWithTag(tag);
                }
                catch (UnityException)
                {
                    continue;
                }

                foreach (var objective in objects)
                {
                    if (!NavMesh.SamplePosition(objective.transform.position, out var hit, 5f, NavMesh.AllAreas))
                    {
                        report.Add(PgFinding
                            .Fail("scene.objective.offNavmesh",
                                $"'{objective.name}' is more than 5m from any navigable surface")
                            .At(Perception.PgViewDigest.PathOf(objective.transform))
                            .Fix("The player may be unable to reach it. Extend the navmesh or move the object."));
                        continue;
                    }

                    var path = new NavMeshPath();
                    NavMesh.CalculatePath(origin.Value, hit.position, NavMesh.AllAreas, path);

                    // CalculatePath returns true for a partial path, so the status is the
                    // only trustworthy signal that the destination is actually reachable.
                    if (path.status != NavMeshPathStatus.PathComplete)
                    {
                        report.Add(PgFinding
                            .Fail("scene.objective.unreachable",
                                $"'{objective.name}' cannot be walked to from the spawn area")
                            .At(Perception.PgViewDigest.PathOf(objective.transform))
                            .With("PathComplete", path.status.ToString())
                            .Fix("Check for a gap in the navmesh, or a door the probe cannot open."));
                    }
                }
            }
        }

        static Vector3? FindReachabilityOrigin(List<Vector3> samples)
        {
            var player = PgLocate.Player();
            var candidate = player != null ? player.position : samples.FirstOrDefault();
            return NavMesh.SamplePosition(candidate, out var hit, 10f, NavMesh.AllAreas)
                ? hit.position
                : (Vector3?)null;
        }

        /// <summary>Thins a sample set to at most <paramref name="target"/> well-spread points.</summary>
        static List<Vector3> Decimate(List<Vector3> samples, int target)
        {
            if (samples.Count <= target) return samples;
            var step = (float)samples.Count / target;
            var result = new List<Vector3>(target);
            for (var i = 0; i < target; i++) result.Add(samples[Mathf.FloorToInt(i * step)]);
            return result;
        }

        static List<List<Vector3>> GroupIntoIslands(List<Vector3> samples)
        {
            var islands = new List<List<Vector3>>();
            var path = new NavMeshPath();

            foreach (var sample in samples)
            {
                var placed = false;
                foreach (var island in islands)
                {
                    NavMesh.CalculatePath(island[0], sample, NavMesh.AllAreas, path);
                    if (path.status != NavMeshPathStatus.PathComplete) continue;
                    island.Add(sample);
                    placed = true;
                    break;
                }

                if (!placed) islands.Add(new List<Vector3> { sample });
            }

            return islands;
        }
#endif
    }
}
