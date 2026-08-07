using System.Collections.Generic;
using UnityEngine;

namespace ProvingGround.Authoring
{
    /// <summary>
    /// Marks an object as owned by a scene recipe.
    ///
    /// This is what makes re-applying a recipe converge instead of duplicating. Without an
    /// identity that survives a rebuild, the only options are to wipe the scene every time
    /// or to create a second copy of everything, and both are worse.
    ///
    /// Objects carrying this marker are replaced or removed on rebuild. Anything without
    /// it is left alone, so hand-placed work in the same scene survives.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PgManaged : MonoBehaviour
    {
        [Tooltip("Recipe that owns this object.")]
        public string Recipe;

        [Tooltip("Stable id within the recipe.")]
        public string Id;

        /// <summary>Set false to keep an object across rebuilds after editing it by hand.</summary>
        [Tooltip("Uncheck to stop the recipe overwriting this object.")]
        public bool Rebuild = true;

        /// <summary>
        /// Component types this recipe added, so a rebuild can remove the ones it no longer
        /// declares without touching anything else.
        ///
        /// The alternative - clearing every component and re-adding - looks simpler and is
        /// wrong: it destroys the MeshRenderer and Collider that came with a primitive, so
        /// the object silently loses its visuals and its collision on the second build.
        /// </summary>
        [Tooltip("Components this recipe added. Managed automatically.")]
        public List<string> AppliedComponents = new List<string>();
    }
}
