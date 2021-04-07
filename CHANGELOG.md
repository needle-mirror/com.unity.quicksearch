# Changelog

## [3.0.0-preview.8] - 2021-04-07
- Do not index objects with hide flags DontSave
- Do not keep full prefab asset path keywords (saving ~1 MB of memory in large projects)
- Fix object indexing with an invalid global object id.
- Fix unavailable search index artifact timeout check
- Scope property database code with USE_PROPERTY_DATABASE

## [3.0.0-preview.7] - 2021-03-27
- Minor UI tweaks

## [3.0.0-preview.6] - 2021-03-26
- Sync with 2021.2.0a12 (499bc7b9a4c9)
- Add [] to selector regex
- Add basic search expression keyword support (i.e. ASC, DESC, etc.)
- Add material texture property name indexation
- Add range{} evaluator
- Add support for item favorites
- Add support for synchronous resolution when using SearchService.Request
- Auto expansion of sample expression values
- Check for search query in package. Allow search extensions internal access
- Compress search indexes by ~60%
- Do not save search query if file path is not valid for asset db (case 1317448)
- Don't open multiple windows of Index Manager
- Enable boolean filters without operators.
- Fix asset store search provider carrousel
- Fix error reporting triggered by avg in main thread
- Fix floating number parsing with dots and comma
- Fix multiple incremental incoming updates
- Fix seach field text selection when executing a saved query
- Fix search index incremental update merge issue
- Fix search service request using FirstBatchAsync
- Fix SearchService loading itself too soon
- Fix sidebar horizontal scrollbar and splitter popping
- Fix slow find: ** globing pattern using regex (case 1323783)
- Fix status error false positive
- Fix the 2999 limit with multiple AND nodes
- Improve active search view query workflow
- Improve support for nested assets search indexing
- Keep track of the last selected search query to later save over it
- More compact syntax error message. ensure params numbering makes sense
- Optimize asset provider GID resolution
- Optimize AssetProvider.Search by ~4x
- Optimize search indexes merge
- Optimize search result path comparison
- Remove existing documents from index before merging new content
- Save all index strings in a table to save space
- Save item size as a global editor preference
- Support saving query in packages
- Update the search provider active state when toggled in the search view (case 1318459)

## [3.0.0-preview.5] - 2021-02-26
- Sync with 2021.2.0a8 (6eb956596132)
- Add new SearchService.ShowPicker API
- Add search expression language to evaluate multiple search queries and apply set operations, transformation or other user defined operation.
- Fix editor stall when the asset worker try to resolve a message log with an UnityEngine.Object in a non-main thread. (case 1316768)
- Create default index when opening the index manager if it was never created before.
- Do not save empty Roots/Includes/Excludes in the index settings file. (case 1307800)
- Show disable index in the index manager. (case 1307781)
- The Roots object field is changed for a TextField to allow selection of folders outside of Assets. (case 1307793)

## [3.0.0-preview.4] - 2021-02-02
- Sync with 2021.2.0a4 (864e4ed4e79c)
- Add index types to the filter menu items
- Display individual search indexes are the asset provider in the filter menu (case 1307787)
- Do not clear providers when disposing of the search context
- Do not close search window on ESC if it is docked (case 1311205)
- Do not index assets with ~ in the their file path.
- Do not index redundant Assets and Packages root words
- Fix fbx and obj mesh type indexing (case 1305383)
- Fix help provider using disposed search context (case 1309227)
- Fix minor search tab styling issue
- Fix package search provider Install, Update and Remove button availability (case 1309659)
- Fix scene provider conversion test
- Fix search view inspector wide mode issues (case 1299583)
- Fix Search window appears with the clipped header when opened after reset (case 1306463)
- Fix static API method name filtering
- Fix updating default search database roots were not re-indexed
- Ignore artifacts with an unresolved guid
- Merge the object and asset search provider using GlobalObjectId as the search item key

## [3.0.0-preview.3] - 2020-12-06
- Sync with 2021.1.0a10 (c69a17d70606)
- Add support to update the search view context providers (case 1296559)
- Change "scene" to "hierarchy" and "s:" to "h:"
- Do not show empty explicit search provider tabs (case 1296463)
- Filter providers when creating a new search context.
- Fix asset store settings actions.
- Fix IQueryHandler type constraint and word matcher naming.
- Improve the asynchronous resolution of search index artifacts.
- Persist the show status bar setting globally instead of per search context.
- Remove icon support from SearchQuery assets.
- Remove openContextual API.
- Remove support for 2020.1.
- Remove tab instead of shrinking.
- Rename context menu items for saved search query.
- Select which search tab to show in last place.
- Set search indexing task as low priority.
- Show a different help string if only the current search tab has no result.
- Show the search result total count in the status bar.

## [3.0.0-preview.2] - 2020-11-11
- Sync with 10fd0d6f6164
- Add a SearchQuery dedicated inspector that does a preview of the query.
- Add error highlighting when a query is illegal.
- Add grip icon in Index manager reorderable lists.
- Add i: as a new filter for interface searching.
- Add new Analytics: lots of new GenericEvent and a new ReportUsage
- Add new search tabs.
- Add support for subset of SearchItems during filtering.
- Better API on SearchItem to get preview and thumbnail.
- Colors are now identified with # sign for proper parsing.
- Do not fetch obsolete static APIs.
- Do not fetch preview in list view when file is over 16 mb.
- Fix asset store search result formatting.
- Fix context menu displaying empty tooltip.
- Fix package manager search provider always listing packages event if not needed.
- Fix Page-Up and Page-Down in grid view.
- Improve list, grid and details view and preview generation.
- Improve resizing of search tabs.
- Lots of UI tweaks.
- Optimize group item sorting.
- Optimize quick search window first load time from 270 ms to 90 ms.
- Optimize scene indexing.
- Persist ViewState in the SearchQuery to restore icon size and other view specific state.
- Search index document paths when looking for words
- Tweak UI so thumbnail view is closer to the Project Browser.
- Use Alt+Left/Right to cycle through search tabs.
- Various QueryEngine improvements and optimizations.
- Wrap around the list selection.
