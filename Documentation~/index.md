# Unity Quick Search

Unity QuickSearch **preview** 3.0.X is a mirror of the [Search Tool](https://docs.unity3d.com/2022.1/Documentation/Manual/search-overview.html) you can find in Unity 21.1 and onward. This preview package allow clients to test new Search workflows in the Unity 2020.3 stream. 

*If you need a stable version of QuickSearch for Unity 2020.3 used the verified release of QuickSearch 2.0.*

This package doesn't contain local documentation. Since QuickSearch **preview** 3.0.X is a mirror of the latest Search Tool, the Search documentation available on the main Unity Documentation site should be considered the official documentation of this package.

- [Search Overview](https://docs.unity3d.com/2022.1/Documentation/Manual/search-overview.html)
- [Search Usage](https://docs.unity3d.com/2022.1/Documentation/Manual/search-usage.html)
- [Search Providers](https://docs.unity3d.com/2022.1/Documentation/Manual/search-providers.html)
- [Search Expression](https://docs.unity3d.com/2022.1/Documentation/Manual/search-expressions.html)

Here is a list of some useful Search related classes if you want to leverage the Search API to run queries from code.

- [SearchService](https://docs.unity3d.com/2022.1/Documentation/ScriptReference/Search.SearchService.html) : Starting point to issue any queries by code and how to iterate the results asynchronously.
- [SearchContext](https://docs.unity3d.com/2022.1/Documentation/ScriptReference/Search.SearchContext.html) : Encapsulate a query and all its various View and Processing options.
- [SearchItem](https://docs.unity3d.com/2022.1/Documentation/ScriptReference/Search.SearchItem.html) : these are the results of a Query. SearchItems can encapsulate any Unity object (asset, scene object, menu, settings, custom user defined concept).
- [SearchProvider](https://docs.unity3d.com/2022.1/Documentation/ScriptReference/Search.SearchProvider.html) : API to implement if you want to write your own SearchProvider. 
- [QueryEngine](https://docs.unity3d.com/2022.1/Documentation/ScriptReference/Search.QueryEngine.html) : Utility class that can be use to filter a set of items according to a textual query. The Query Engine parses and validate the query itself an applies user define filters and operators to filter any kind of items. The QueryEngine itself is independant of the Search ecosystem but most SearchProviders are using QueryEngines to process queries.