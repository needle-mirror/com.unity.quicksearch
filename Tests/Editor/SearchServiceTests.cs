using System.IO;
using NUnit.Framework;
using Unity.QuickSearch;

namespace Unity.QuickSearch
{
    internal class SearchServiceTests
    {
        const string k_TestFileName = "Packages/com.unity.quicksearch/Tests/Editor/Content/test_material_42.mat";

        [SetUp]
        public void EnableService()
        {
            SearchService.Enable(SearchContext.Empty);
            SearchService.Filter.ResetFilter(true);
        }

        [TearDown]
        public void DisableService()
        {
            SearchService.Disable(SearchContext.Empty);
        }

        [Test]
        public void FetchItems()
        {
            var ctx = new SearchContext {searchText = "test", wantsMore = true};
            Assert.AreEqual(0, ctx.searchId);

            var fetchedItems = SearchService.GetItems(ctx);

            Assert.AreEqual(1, ctx.searchId);
            Assert.IsNotEmpty(fetchedItems);
            var foundItem = fetchedItems.Find(item => item.label == Path.GetFileName(k_TestFileName));
            Assert.IsNotNull(foundItem.id);
            Assert.IsNull(foundItem.description);

            Assert.IsNotNull(foundItem.provider);
            Assert.IsNotNull(foundItem.provider.fetchDescription);
            var fetchedDescription = foundItem.provider.fetchDescription(foundItem, ctx);
            Assert.AreEqual("Packages/com.unity.quicksearch/Tests/Editor/Content/test_material_42.mat (2.0 KB)", fetchedDescription);
        }
    }
}
