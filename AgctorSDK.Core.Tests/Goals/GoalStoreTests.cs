using System;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Goals
{
    [TestClass]
    public class GoalStoreTests
    {
        private InMemoryGoalStore CreateStore()
        {
            // Use a unique temp file so each test starts with a clean slate and avoids cross-test interference.
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goals-{Guid.NewGuid()}.json");
            return new InMemoryGoalStore(path);
        }

        [TestMethod]
        public async Task CreateGoal_ShouldStoreCorrectData()
        {
            var store = CreateStore();
            var goal = new Goal { Title = "Test", Description = "Desc" };

            var created = await store.CreateAsync(goal);
            var fetched = await store.GetAsync(created.Id);

            Assert.IsNotNull(fetched);
            Assert.AreEqual("Test", fetched!.Title);
            Assert.AreEqual("Desc", fetched.Description);
            Assert.AreEqual(GoalStatus.Pending, fetched.Status);
        }

        [TestMethod]
        public async Task GetGoals_ShouldReturnAllStoredGoals()
        {
            var store = CreateStore();
            await store.CreateAsync(new Goal { Title = "A" });
            await store.CreateAsync(new Goal { Title = "B" });

            var all = (await store.GetAllAsync()).ToList();
            Assert.AreEqual(2, all.Count);
        }

        [TestMethod]
        public async Task UpdateGoal_ShouldChangeFields()
        {
            var store = CreateStore();
            var goal = await store.CreateAsync(new Goal { Title = "Old" });

            goal.Title = "New";
            await store.UpdateAsync(goal);

            var fetched = await store.GetAsync(goal.Id);
            Assert.AreEqual("New", fetched!.Title);
        }

        [TestMethod]
        public async Task DeleteGoal_ShouldRemoveCorrectGoal()
        {
            var store = CreateStore();
            var g1 = await store.CreateAsync(new Goal { Title = "A" });
            await store.CreateAsync(new Goal { Title = "B" });

            await store.DeleteAsync(g1.Id);
            var remaining = (await store.GetAllAsync()).ToList();
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual("B", remaining[0].Title);
        }
    }
} 