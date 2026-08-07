using NUnit.Framework;
using ProvingGround.EditorTools;

namespace ProvingGround.Tests
{
    /// <summary>
    /// Version comparison decides whether every user sees an update banner, so getting it
    /// wrong is either a permanent phantom nag or a notice nobody ever gets.
    /// </summary>
    public class PgUpdateCheckTests
    {
        [Test]
        public void ANewerTagIsNewerThanTheInstalledPackageVersion()
        {
            Assert.IsTrue(PgUpdateCheck.IsNewer("v0.2.0", "0.1.0"));
            Assert.IsTrue(PgUpdateCheck.IsNewer("v0.1.1", "0.1.0"));
            Assert.IsTrue(PgUpdateCheck.IsNewer("v1.0.0", "0.9.9"));
        }

        [Test]
        public void TheSameVersionIsNotAnUpdate()
        {
            // The tag carries a leading v and package.json does not; treating that as a
            // difference would show an update banner forever.
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.1.0", "0.1.0"));
            Assert.IsFalse(PgUpdateCheck.IsNewer("0.1.0", "0.1.0"));
        }

        [Test]
        public void AnOlderTagIsNotAnUpdate()
        {
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.1.0", "0.2.0"));
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.9.9", "1.0.0"));
        }

        [Test]
        public void ComponentsCompareNumericallyRatherThanAsText()
        {
            Assert.IsTrue(PgUpdateCheck.IsNewer("v0.1.10", "0.1.9"),
                "0.1.10 is newer than 0.1.9; a string compare would say otherwise");
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.1.9", "0.1.10"));
        }

        [Test]
        public void MissingComponentsCountAsZero()
        {
            Assert.IsTrue(PgUpdateCheck.IsNewer("v0.2", "0.1.9"));
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.1", "0.1.0"));
        }

        [Test]
        public void PreReleaseSuffixesAreIgnored()
        {
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.1.0-pre.1", "0.1.0"));
            Assert.IsTrue(PgUpdateCheck.IsNewer("v0.2.0-rc.1", "0.1.0"));
        }

        [Test]
        public void GarbageNeverProducesAnUpdatePrompt()
        {
            Assert.IsFalse(PgUpdateCheck.IsNewer("", "0.1.0"));
            Assert.IsFalse(PgUpdateCheck.IsNewer(null, "0.1.0"));
            Assert.IsFalse(PgUpdateCheck.IsNewer("v0.2.0", ""));
            Assert.IsFalse(PgUpdateCheck.IsNewer("not-a-version", "0.1.0"));
        }

        [Test]
        public void TheTagIsReadFromAReleasePayload()
        {
            var json = "{\"url\":\"https://api.github.com/x\",\"tag_name\": \"v0.3.1\",\"name\":\"v0.3.1\"}";
            Assert.AreEqual("v0.3.1", PgUpdateCheck.TagFrom(json));
        }

        [Test]
        public void APayloadWithoutATagYieldsNothing()
        {
            Assert.IsNull(PgUpdateCheck.TagFrom("{\"message\":\"Not Found\"}"));
            Assert.IsNull(PgUpdateCheck.TagFrom(""));
            Assert.IsNull(PgUpdateCheck.TagFrom(null));
        }
    }
}
