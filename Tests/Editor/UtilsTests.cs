using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal class UtilsTests
    {
        [Test]
        public void GetPackagesPaths()
        {
            var packagePaths = Utils.GetPackagesPaths();
            Assert.Contains("Packages/com.unity.quicksearch", packagePaths);
        }

        [Test]
        public void FindTextureForType()
        {
            var texture = Utils.FindTextureForType(typeof(Texture2D));
            Assert.IsNotNull(texture);
        }

        [Test]
        public void GetIconForObject()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>($"{QuickSearch.packageFolderName}/Tests/Editor/Content/test_monobehavior.cs");
            var icon = Utils.GetIconForObject(script);
            Assert.IsNotNull(icon);
        }
    }
}
