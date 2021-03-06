using System.IO;
using NUnit.Framework;

namespace NuOptimizer.Test
{
    [TestFixture]
    public class DependencyGraphFlattenerTest : BaseFixture
    {
        private DependencyGraphFlattener _uut;

        [SetUp]
        public void SetUp()
        {
            _uut = new DependencyGraphFlattener();
        }

        [TestCase(1)]
        [TestCase(2)]
        public void CreatesDirectoryBuildTargets(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void UpdatesDirectoryBuildTargets(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void CreatesProjectProps(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void CreatesProjectPropsRelativeRoot(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                try
                {
                    Directory.SetCurrentDirectory(TestDataRoot);
                    _uut.Apply(".");
                }
                finally
                {
                    Directory.SetCurrentDirectory(currentDirectory);
                }
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void HandlesMissingProjects(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void HandlesSingleProject(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void HandlesProjectsOutsideRoot(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(Path.Combine(TestDataRoot, "Root"));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [Platform("Win32NT")]
        public void HandlesInconsistentCasing(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(TestDataRoot);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void HandlesDuplicatedProjectsOutsideRoot(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(Path.Combine(TestDataRoot, "Root"));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        public void HandlesNonCanonicalReferences(int repeats)
        {
            for (var i = 0; i < repeats; i++)
            {
                _uut.Apply(Path.Combine(TestDataRoot, "src"));
            }
        }
    }
}
