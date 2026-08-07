using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ProvingGround.Authoring;
using ProvingGround.EditorTools;

namespace ProvingGround.Tests
{
    public class PgTypesTests
    {
        [Test]
        public void ResolvesBuiltInComponentsByShortName()
        {
            Assert.AreEqual(typeof(Rigidbody), PgTypes.Component("Rigidbody"));
            Assert.AreEqual(typeof(BoxCollider), PgTypes.Component("BoxCollider"));
            Assert.AreEqual(typeof(Light), PgTypes.Component("light"), "resolution should be case insensitive");
        }

        [Test]
        public void ResolvesByFullyQualifiedName()
        {
            Assert.AreEqual(typeof(Rigidbody), PgTypes.Component("UnityEngine.Rigidbody"));
        }

        [Test]
        public void UnknownTypesReturnNullAndOfferSuggestions()
        {
            Assert.IsNull(PgTypes.Component("RigidBodyy"));
            CollectionAssert.Contains(PgTypes.Suggest("RigidBodyy"), "Rigidbody");
        }
    }

    public class PgPropertyBinderTests
    {
        GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("BinderTarget");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void SetsFloatsIntsAndBools()
        {
            var body = _go.AddComponent<Rigidbody>();

            Assert.IsNull(PgPropertyBinder.Set(body, "mass", 12.5));
            Assert.AreEqual(12.5f, body.mass, 0.001f);

            Assert.IsNull(PgPropertyBinder.Set(body, "isKinematic", true));
            Assert.IsTrue(body.isKinematic);
        }

        [Test]
        public void SetsVectorsFromArrays()
        {
            var box = _go.AddComponent<BoxCollider>();

            Assert.IsNull(PgPropertyBinder.Set(box, "size", new[] { 2f, 3f, 4f }));
            Assert.AreEqual(new Vector3(2, 3, 4), box.size);
        }

        [Test]
        public void SetsEnumsByName()
        {
            var light = _go.AddComponent<Light>();

            Assert.IsNull(PgPropertyBinder.Set(light, "type", "Directional"));
            Assert.AreEqual(LightType.Directional, light.type);
        }

        [Test]
        public void SetsColoursFromHex()
        {
            var light = _go.AddComponent<Light>();

            Assert.IsNull(PgPropertyBinder.Set(light, "color", "#FF0000"));
            Assert.AreEqual(1f, light.color.r, 0.01f);
            Assert.AreEqual(0f, light.color.g, 0.01f);
        }

        [Test]
        public void ReportsUnknownPropertiesRatherThanFailingSilently()
        {
            var body = _go.AddComponent<Rigidbody>();
            var error = PgPropertyBinder.Set(body, "definitelyNotAProperty", 1);

            Assert.IsNotNull(error, "an unknown property must be reported, not ignored");
            StringAssert.Contains("definitelyNotAProperty", error);
        }

        [Test]
        public void ReportsUnconvertibleValues()
        {
            var body = _go.AddComponent<Rigidbody>();
            Assert.IsNotNull(PgPropertyBinder.Set(body, "mass", "not a number"));
        }
    }

    public class PgSceneBuilderTests
    {
        const string RecipeName = "pg-test-recipe";

        [TearDown]
        public void TearDown()
        {
            foreach (var managed in Object
                         .FindObjectsByType<PgManaged>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(m => m.Recipe == RecipeName)
                         .ToList())
            {
                if (managed != null) Object.DestroyImmediate(managed.gameObject);
            }
        }

        static PgSceneRecipe Recipe(params PgObjectSpec[] objects) => new PgSceneRecipe
        {
            Name = RecipeName,
            Seed = 1,
            EnsureCamera = false,
            EnsureLight = false,
            Objects = objects.ToList()
        };

        static int Managed() => Object
            .FindObjectsByType<PgManaged>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(m => m.Recipe == RecipeName);

        [Test]
        public void BuildsObjectsWithTransformsAndComponents()
        {
            var report = PgSceneBuilder.Build(Recipe(new PgObjectSpec
            {
                Id = "Floor",
                Primitive = "Cube",
                Position = new[] { 0f, -0.5f, 0f },
                Scale = new[] { 20f, 1f, 20f },
                Components = new List<PgComponentSpec>
                {
                    new PgComponentSpec
                    {
                        Type = "Rigidbody",
                        Set = new Dictionary<string, object> { ["isKinematic"] = true, ["mass"] = 5.0 }
                    }
                }
            }));

            Assert.IsTrue(report.Passed, report.ToConsole());

            var floor = GameObject.Find("Floor");
            Assert.IsNotNull(floor, "the object was not created");
            Assert.AreEqual(new Vector3(20, 1, 20), floor.transform.localScale);

            var body = floor.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "the component was not added");
            Assert.IsTrue(body.isKinematic, "the component property was not applied");
            Assert.AreEqual(5f, body.mass, 0.001f);
        }

        [Test]
        public void RebuildingConvergesInsteadOfDuplicating()
        {
            var recipe = Recipe(new PgObjectSpec { Id = "Block", Primitive = "Cube" });

            PgSceneBuilder.Build(recipe);
            var afterFirst = Managed();

            PgSceneBuilder.Build(recipe);
            PgSceneBuilder.Build(recipe);

            Assert.AreEqual(afterFirst, Managed(),
                "re-applying a recipe must converge, not create another copy each time");
        }

        [Test]
        public void RebuildingKeepsThePrimitivesOwnRendererAndCollider()
        {
            // Regression: an earlier build cleared every component before re-applying the
            // recipe, which destroyed the MeshRenderer and BoxCollider that came with the
            // primitive. The object count stayed identical, so it looked idempotent while
            // the level quietly lost its visuals and its collision.
            var recipe = Recipe(new PgObjectSpec
            {
                Id = "Block", Primitive = "Cube", Material = "#4488FF"
            });

            PgSceneBuilder.Build(recipe);
            PgSceneBuilder.Build(recipe);

            var block = GameObject.Find("Block");
            Assert.IsNotNull(block);
            Assert.IsNotNull(block.GetComponent<MeshRenderer>(), "the rebuild destroyed the renderer");
            Assert.IsNotNull(block.GetComponent<MeshFilter>(), "the rebuild destroyed the mesh");
            Assert.IsNotNull(block.GetComponent<Collider>(), "the rebuild destroyed the collider");
            Assert.IsNotNull(block.GetComponent<MeshRenderer>().sharedMaterial,
                "the material was not re-applied");
        }

        [Test]
        public void RebuildingRemovesComponentsDroppedFromTheRecipe()
        {
            var recipe = Recipe(new PgObjectSpec
            {
                Id = "Block",
                Primitive = "Cube",
                Components = new List<PgComponentSpec> { new PgComponentSpec { Type = "Rigidbody" } }
            });

            PgSceneBuilder.Build(recipe);
            Assert.IsNotNull(GameObject.Find("Block").GetComponent<Rigidbody>());

            recipe.Objects[0].Components.Clear();
            PgSceneBuilder.Build(recipe);

            Assert.IsNull(GameObject.Find("Block").GetComponent<Rigidbody>(),
                "a component dropped from the recipe should be removed on rebuild");
            Assert.IsNotNull(GameObject.Find("Block").GetComponent<MeshRenderer>(),
                "removing a declared component must not disturb the primitive's own");
        }

        [Test]
        public void RebuildingAppliesChangedValues()
        {
            var recipe = Recipe(new PgObjectSpec
            {
                Id = "Block", Primitive = "Cube", Position = new[] { 0f, 0f, 0f }
            });

            PgSceneBuilder.Build(recipe);

            recipe.Objects[0].Position = new[] { 5f, 0f, 0f };
            PgSceneBuilder.Build(recipe);

            Assert.AreEqual(5f, GameObject.Find("Block").transform.localPosition.x, 0.001f);
        }

        [Test]
        public void ObjectsDroppedFromTheRecipeAreRemoved()
        {
            var recipe = Recipe(
                new PgObjectSpec { Id = "Keep", Primitive = "Cube" },
                new PgObjectSpec { Id = "Drop", Primitive = "Cube" });

            PgSceneBuilder.Build(recipe);
            Assert.IsNotNull(GameObject.Find("Drop"));

            recipe.Objects.RemoveAt(1);
            PgSceneBuilder.Build(recipe);

            Assert.IsNull(GameObject.Find("Drop"), "an object no longer in the recipe should be removed");
            Assert.IsNotNull(GameObject.Find("Keep"), "objects still in the recipe must survive");
        }

        [Test]
        public void UnmanagedObjectsSurviveARebuild()
        {
            var handmade = new GameObject("HandPlaced");
            try
            {
                PgSceneBuilder.Build(Recipe(new PgObjectSpec { Id = "Block", Primitive = "Cube" }));
                Assert.IsNotNull(GameObject.Find("HandPlaced"),
                    "a rebuild must not touch objects the recipe does not own");
            }
            finally
            {
                Object.DestroyImmediate(handmade);
            }
        }

        [Test]
        public void RepeatExpandsIntoNumberedCopies()
        {
            PgSceneBuilder.Build(Recipe(new PgObjectSpec
            {
                Id = "Pillar",
                Primitive = "Cylinder",
                Repeat = new PgRepeat { Count = 4, Offset = new[] { 3f, 0f, 0f } }
            }));

            Assert.AreEqual(4, Managed());
            Assert.IsNotNull(GameObject.Find("Pillar_0"));
            Assert.IsNotNull(GameObject.Find("Pillar_3"));
            Assert.AreEqual(9f, GameObject.Find("Pillar_3").transform.localPosition.x, 0.001f);
        }

        [Test]
        public void RingRepeatPlacesCopiesAroundACircle()
        {
            PgSceneBuilder.Build(Recipe(new PgObjectSpec
            {
                Id = "Wall",
                Primitive = "Cube",
                Repeat = new PgRepeat { Count = 4, Ring = 10f }
            }));

            var first = GameObject.Find("Wall_0").transform.localPosition;
            var second = GameObject.Find("Wall_1").transform.localPosition;

            Assert.AreEqual(10f, first.magnitude, 0.01f, "copies should sit on the ring radius");
            Assert.AreEqual(10f, second.magnitude, 0.01f);
            Assert.AreNotEqual(first, second, "copies should be at different angles");
        }

        [Test]
        public void ChildrenAreParentedRegardlessOfDeclarationOrder()
        {
            PgSceneBuilder.Build(Recipe(
                new PgObjectSpec { Id = "Child", Primitive = "Sphere", Parent = "Parent" },
                new PgObjectSpec { Id = "Parent", Primitive = "Cube" }));

            var child = GameObject.Find("Child");
            Assert.IsNotNull(child);
            Assert.IsNotNull(child.transform.parent, "the child was left at the scene root");
            Assert.AreEqual("Parent", child.transform.parent.name);
        }

        [Test]
        public void AnUnknownComponentIsReportedWithSuggestions()
        {
            var report = PgSceneBuilder.Build(Recipe(new PgObjectSpec
            {
                Id = "Broken",
                Primitive = "Cube",
                Components = new List<PgComponentSpec> { new PgComponentSpec { Type = "RigidBodyy" } }
            }));

            Assert.IsFalse(report.Passed, "an unknown component type must fail the build");
            var finding = report.Findings.First(f => f.Id == "build.unknownComponent");
            StringAssert.Contains("Rigidbody", finding.Remedy);
        }

        [Test]
        public void SeededJitterIsReproducible()
        {
            var spec = new PgObjectSpec
            {
                Id = "Rock",
                Primitive = "Sphere",
                Repeat = new PgRepeat { Count = 5, Offset = new[] { 2f, 0f, 0f }, Jitter = new[] { 1f, 0f, 1f } }
            };

            PgSceneBuilder.Build(Recipe(spec));
            var first = GameObject.Find("Rock_3").transform.localPosition;

            TearDown();
            PgSceneBuilder.Build(Recipe(spec));
            var second = GameObject.Find("Rock_3").transform.localPosition;

            Assert.AreEqual(first, second, "the same seed must place jittered copies identically");
        }
    }
}
