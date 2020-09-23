# Changelog

## [3.0.0-preview.1] - 2020-09-23
- [UX] Brand new UI
- [API] Align with new API in Unity 2021.1

## [2.1.0] - 2020-08-25
- [UX] Improve search database import pipeline by producing index artifacts per asset.
- [UX] Add support to search asset file paths using regex (i.e. `.*\d{2,}.\w+`)
- [UX] Add support to index asset import settings (using the extended index setting options).
- [UX] Add new dependency provider sample.
- [UX] Add new analytics for all interactions possible with the Quick Search ecosystem.
- [UX] Add an auto-complete dropdown to find properties using TAB.
- [FIX] Save enabled providers per context instead of globally.
- [FIX] Fix corrupted SJSON settings parsing (case 1260242)
- [DOC] Add a cheat sheet that covers all filters for all providers.
- [API] Add supports for dots (`.`) in QueryEngine filter ids.
- [API] Add QueryEngine supports to return the initial payload when the query is empty.
- [DOC] Add documentation for Find and Dependencies provider.

## [2.0.2] - 2020-07-01
- [UX] Remove the package provider browse action.
- [UX] Add search expression Map node to output X/Y value pairs.
- [UX] Add many new asset and scene filters for material and prefabs.
- [FIX] Fix various search expression issues.
- [FIX] Fix searching saved queried within a contextual search.
- [FIX] Execute first item when pressing enter even if no selection (case 1258382).
- [API] Add support for doubles with the Query Engine default operators.
- [API] Add API to get the query node located at a specified position in a query string.

## [2.0.1] - 2020-06-30
- [FIX] Fix AssetImporters experimental issues with 2020.2

## [2.0.0] - 2020-06-11
- [UX] Save Quick Search settings per project instead of globally for all projects.
- [UX] Remove support for 2018.4.
- [UX] Remove search provider sub categories. It simplifies the search view filter window.
- [UX] Remove basic file indexer support. Now indexing only works with .index files and fallback is using the AssetDatabase.FindAsset API.
- [UX] Improve the saved search query workflow (less field to fulfill).
- [UX] Change Reset priorities button in Preferences to Reset to providers Defaults (which reset priority, active and default actions).
- [UX] Add the total asynchronous time a query took for all provider sessions in the Quick Search status bar.
- [UX] Add the ability to fetch items on a specific provider.
- [UX] Add support to override the default object picker using Quick Search.
- [UX] Add support for regular selection as well as multi selection using end and home keys.
- [UX] Add support for multiple asset indexes.
- [UX] Add Search Engines for Unity's Search API.
- [UX] Add scene property filtering support (i.e. `t:light2d p(intensity)>=0.5`)
- [UX] Add prefab asset indexing support.
- [UX] Add onboarding workflow the first time you launch Quick Search
- [UX] Add new scene provider filters (i.e. id:<string>, path:<string/string> size:<number>, layer:<number>, tag:<string>, t:<type>, is:[visible|hidden|leaf|root|child])
- [UX] Add new create Search Query Button. If search queries exist in the project, this is what we show instead of hardcoded help string.
- [UX] Add nested search expression nodes support in the Expression Builder.
- [UX] Add multi selection support
- [UX] Add more error reporting for invalid queries with the `QueryEngine`.
- [UX] Add index manager to manage your project asset, prefab and scene indexes.
- [UX] Add grid view support to display search results in a grid of thumbnails.
- [UX] Add Creation Window to for Search Query.
- [UX] Add background scene asset indexing.
- [UX] Add an embedded inspector for objects returned by the resource and scene search providers.
- [UX] Add a search expression builder to create complex queries.
- [UX] Add a compact list view.
- [UX] Add `dir:DIR_NAME` to asset indexing to filter assets by their direct parent folder name.
- [UX] Add "Show all results..." checkbox to run per search provider more queries to find even more results. In example for the AssetProvider, if this is checked we try to find more assets by using AssetDatabase.FindAssets. This can be unchecked in large project where the asset database can be very slow.
- [FIX] Remove the asset store provider for Unity version before 2019.3.
- [FIX] Optimize the search menu and scene providers (about 4-5x faster).
- [FIX] Fix Unity crash when dragging and dropping from quick search (case 1215420)
- [FIX] Fix the search field initial styling and position when opening Quick Search.
- [FIX] Fix scrollbar overflow (more visible in the light theme).
- [FIX] Fix Quick Search fails to find assets when more than 16 characters are entered into the search field (case 1225947)
- [FIX] Fix Progress API usage.
- [FIX] Fix one letter word query that breaks searching the index.
- [FIX] Fix NullReferenceException thrown When "Disabled" option is toggle from "Search Index Manager" window (case 1252291)
- [FIX] Fix filter override application.
- [FIX] Fix drag and drop paths for the asset search provider.
- [FIX] Fix details view min and max size.
- [FIX] Fix complete file name indexing (case 1214270)
- [FIX] Fix an issue tracking selection of item at index 1.
- [FIX] Fix actions sorting on SearchService init and in SearchSettings window.
- [FIX] Add support for any characters in word searches.
- [FIX] Add better support for startup incremental update.
- [FIX] Add better sorting for assets based on file path matches.
- [DOC] Update API documentation.
- [API] Remove the `SearchFilter` class.
- [API] Optimize call to operator handlers when in fallback mode.
- [API] Improve the `SearchContext` API in order to keep track of filtered providers.
- [API] Improve support for simultaneous calls to `SearchService.GetItems` with different search contexts.
- [API] Improve build time of a `QueryEngine` search query.
- [API] Fix calling onEnable/onDisable multiple time when doing multiple simultaneous searches with a provider.
- [API] Allow skipping words when parsing a query with the QueryEngine.
- [API] Allow retrieval of tokens used to generate a query.
- [API] Add the ability to customize a query engine with filters using method attributes. Used by the Scene Provider.
- [API] Add the ability for the QueryEngine to skip unknown filters in a query.
- [API] Add support to remove filters on the `QueryEngine`.
- [API] Add support to override the string comparison options for word/phrase matching with the `QueryEngine`.
- [API] Add support for spaces inside nested queries.
- [API] Add support for nested queries with the `QueryEngine`.
- [API] Add support for custom object indexers.
- [API] Add support for concurrent calls to the SearchApi engines with different SearchApi contexts.
- [API] Add support for a search word transformer with the `QueryEngine`.
- [API] Add a websocket client called SearchChannel to do search from a web application (20.3 or 21.1 required).
