using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Build.Locator;
using Microsoft.VisualBasic.FileIO;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace NuOptimizer.Test
{
    public class BaseFixture
    {
        public string TestClassName => TestContext.CurrentContext.Test.ClassName?.Split('.').Last();
        public string TestMethodName => TestContext.CurrentContext.Test.MethodName;

        private AsyncLocal<string> _testRoot = new();

        private bool _validateOutput;

        protected string TestDataRoot
        {
            get { return _testRoot.Value; }
            set { _testRoot.Value = value; }
        }

        static BaseFixture()
        {
            MSBuildLocator.RegisterDefaults();
        }

        public BaseFixture(bool validateOutput = true)
        {
            _validateOutput = validateOutput;
        }

        [SetUp]
        public void TestSetUp()
        {
            var tmpRoot = Path.GetTempPath();
            var testDataInput = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "TestData", TestClassName,
                TestMethodName, "in");
            var testDataDestination = Path.Combine(tmpRoot, Assembly.GetExecutingAssembly().GetName().Name,
                TestClassName, TestMethodName);

            if (!Directory.Exists(testDataInput))
                Assert.Fail($"'{testDataInput}' doesn't exist.");

            if (Directory.Exists(testDataDestination))
                Directory.Delete(testDataDestination, recursive: true);

            FileSystem.CopyDirectory(testDataInput, testDataDestination);
            TestDataRoot = testDataDestination;
        }

        [TearDown]
        public void TestTearDown()
        {
            if (!_validateOutput)
                return;

            if (TestContext.CurrentContext.Result.Outcome != ResultState.Success)
                return;

            var testDataExpectedRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "TestData", TestClassName,
                TestMethodName, "out");

            // Take a snapshot of the file system.
            var expectedFiles = new DirectoryInfo(testDataExpectedRoot)
                .GetFiles("*", System.IO.SearchOption.AllDirectories)
                .ToDictionary(x => Path.GetRelativePath(testDataExpectedRoot, x.FullName));

            var actualFiles = new DirectoryInfo(TestDataRoot)
                .GetFiles("*", System.IO.SearchOption.AllDirectories)
                .ToDictionary(x => Path.GetRelativePath(TestDataRoot, x.FullName));

            Assert.That(actualFiles.Keys, Is.EquivalentTo(expectedFiles.Keys));

            foreach (var (fileKey, _) in actualFiles)
            {
                var actualFileInfo = actualFiles[fileKey];
                var expectedFileInfo = expectedFiles[fileKey];

                string ReadAndSanitize(string path) => string.Join(Environment.NewLine,
                    File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x)));

                var actualContent = ReadAndSanitize(actualFileInfo.FullName);
                var expectedContent = ReadAndSanitize(expectedFileInfo.FullName);
                var diff = InlineDiffBuilder.Diff(expectedContent, actualContent);
                if (diff.HasDifferences)
                {
                    var lines = diff.Lines.Select(x =>
                    {
                        switch (x.Type)
                        {
                            case ChangeType.Inserted:
                                return $"+ {x.Text}";

                            case ChangeType.Deleted:
                                return $"- {x.Text}";

                            default:
                                return $"  {x.Text}";
                        }
                    });

                    var txt = string.Join(Environment.NewLine, lines);
                    Assert.Fail($"Unexpected difference in {fileKey}:" + Environment.NewLine + txt);
                }
            }
        }
    }
}
