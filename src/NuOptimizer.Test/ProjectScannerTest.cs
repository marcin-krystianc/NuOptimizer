using System.IO;
using System.Linq;
using DependencyGraphFlattener;
using NUnit.Framework;

namespace NuOptimizer.Test
{
    [TestFixture]
    public class ProjectScannerTest : BaseFixture
    {
        public ProjectScannerTest() : base(false)
        {
        }

        [Test]
        public void SmokeTest()
        {
            var testDataRoot = TestDataRoot;
            var _uut = new ProjectScanner();
            var actualProjects =
                _uut.EnumerateProjects(testDataRoot).Select(x => Path.GetRelativePath(testDataRoot, x));

            Assert.That(actualProjects, Is.EquivalentTo(new[] { "csproj\\Project1.csproj", "fsproj\\Project2.fsproj" }));
        }
    }
}
