using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests
{
    /// <summary>
    /// Tests that correspond to Stage-1 of the Code Understanding subsystem: basic actor hierarchy & persistence.
    /// </summary>
    [TestClass]
    public class Stage1ActorHierarchyTests
    {
        [TestMethod]
        public void CanCreateSolutionAndAddProject()
        {
            // Arrange
            var solution = new SolutionActor("MySolution", "/path/to/MySolution.sln");
            var project = new ProjectActor("ProjectA", "/path/to/ProjectA/ProjectA.csproj");

            // Act
            solution.AddProject(project);

            // Assert
            Assert.AreEqual(1, solution.Children.Count);
            Assert.AreSame(project, solution.Children.Single());
        }

        [TestMethod]
        public void CanAddMultipleFileActorsUnderProject()
        {
            // Arrange
            var project = new ProjectActor("ProjectA", "/path/ProjectA.csproj");
            var file1 = new FileActor("File1.cs", "/path/File1.cs");
            var file2 = new FileActor("File2.cs", "/path/File2.cs");

            // Act
            project.AddFile(file1);
            project.AddFile(file2);

            // Assert
            Assert.AreEqual(2, project.Children.Count);
            CollectionAssert.Contains(project.Children.ToList(), file1);
            CollectionAssert.Contains(project.Children.ToList(), file2);
        }

        [TestMethod]
        public void CanAddAndRetrieveClassAndMethodActors()
        {
            // Arrange
            var file = new FileActor("File1.cs", "/file1.cs");
            var classA = new ClassActor("ClassA");
            var methodX = new MethodActor("MethodX");

            // Act
            classA.AddMethod(methodX);
            file.AddClass(classA);

            // Assert
            Assert.AreSame(classA, file.Children.Single());
            Assert.AreSame(methodX, classA.Children.Single());
        }

        [TestMethod]
        public void ActorsReturnCorrectMetadata()
        {
            var method = new MethodActor("DoWork");
            Assert.AreEqual("DoWork", method.Name);
            Assert.AreEqual(nameof(MethodActor), method.ActorType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(method.Id));
        }

        [TestMethod]
        public async Task ActorStateRoundTripSerialization()
        {
            // Arrange – build a small hierarchy
            var solution = new SolutionActor("MySolution", "/path/MySolution.sln");
            var project = new ProjectActor("ProjectA", "/path/ProjectA.csproj");
            var file = new FileActor("Program.cs", "/path/Program.cs");
            var @class = new ClassActor("Program");
            var method = new MethodActor("Main");

            @class.AddMethod(method);
            file.AddClass(@class);
            project.AddFile(file);
            solution.AddProject(project);

            // Save to a temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            await solution.SaveStateAsync(tempDir);

            // Act – load back
            var loaded = await SolutionActor.LoadStateAsync(tempDir);

            // Assert – structure preserved
            Assert.AreEqual(1, loaded.Children.Count);
            var loadedProject = (ProjectActor)loaded.Children.Single();
            var loadedFile = (FileActor)loadedProject.Children.Single();
            var loadedClass = (ClassActor)loadedFile.Children.Single();
            var loadedMethod = (MethodActor)loadedClass.Children.Single();

            Assert.AreEqual("Main", loadedMethod.Name);
            Assert.AreEqual(nameof(MethodActor), loadedMethod.ActorType);
        }
    }
} 