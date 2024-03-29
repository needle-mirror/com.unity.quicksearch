# Changelog

## [3.0.0-preview.26] - 2022-06-29
- Add .buginfo.
- Fix exception with Transactions.
- Fix SearchSettings fetching Search Engines.
- Remove ParsedQuery api upgrade.

## [3.0.0-preview.25] - 2022-04-21
- Fix search indexer concurrent issue when asset are deleted and the index is being updated at the same time.

## [3.0.0-preview.24] - 2022-04-20
- Add error reporting when a search index fails to load.
- Fix auto-complete text overlapping.
- Fix how the global search window is reused in different context.
- Fix nested search expression constants evaluation (i.e. constant{...})
- Fix property database concurrency under certain conditions.
- Fix query string expression parsing.
- Fix scene search proposition enumeration for large scenes.
- Fix search picker persisted search flags.
- Improve asset provider preview generation.
- Update default Temp* pattern exclusion of folders.

## [3.0.0-preview.23] - 2022-02-15
- Add new Search APIs
- Add +fuzzy switch for the scene and asset providers.
- Add InitTestScene as default exclude pattern.
- Add missing adb provider for advanced object selector search engine (case 1398289)
- Add search result refresh when executing CTRL+Z to undo last block modifications (case 1394732)
- Add support to hide the search group tabs.
- Fix minor project saved search query UX issues.
- Fix moved file not being re-indexed with the proper file path.
- Fix scene provider incremental reference indexing exception.
- Fix search indexer lock threading issue.
- Improve how the search table is reset.
- Index properties by default.
- Minor UI style updates.
- Update package documention to point to official 2022.1 documentation.
- Use SearchViewState.group to initialize initial results tab when creating a new search window (case 1400665)

## [3.0.0-preview.22] - 2021-11-04
- Add scene provider support for property matching =none
- Enable Show Package Results for the find provider (find:)
- Fix user search query table config initialization on save.

## [3.0.0-preview.21] - 2021-10-28
- Add support to cancel search providers with threading capabilities.
- Fix search table serialization with user search queries.
- Fix select{} selector resolution with more than 100 elements.
- Optimize scene provider `ref=` using global object id.

## [3.0.0-preview.20] - 2021-10-18
- Add support to drag sub asset results to ObjectField (case 1374002)
- Add support to filter by static states (i.e. `h: static=[navigationstatic, batchingstatis]`)
- Fix New table view columns should be based on the selected search group (case 1371705)
- Fix NullReferenceException after switching Description column format to Performance metric (case 1364701)
- Fix search picker fails to filter sprite assets (case 1371778)
- Fix searching sub asset by asset container path.
- Fix sub asset type resolution (i.e. `select{p: t:sprite, @name, @type}`)
- Fix Utils.GetAssetPreviewFromPath when used with invalid paths.

## [3.0.0-preview.19] - 2021-10-04
- Add debug search preference dropdown to disable specific custom indexers.
- Fix indexation of removed properties (case 1370415)
- Fix QuickSearch window icon in dark theme.
- Fix search table serialization.
- Fix SearchQueryProvider query enumeration (i.e. `q:...`)
- Fix text not selected when search view opens.
- Optimize the amount of material and shader properties that get indexed.
- Refresh search views when new data gets indexed.
- Remove the Help provider (?) which wasn't used and will be replaced with the new Query Builder UI

## [3.0.0-preview.18] - 2021-09-27
- Add `ref=<global object id>` asset reference indexing to have nested queries work with `h: ref={p: t:material}`
- Add new prefab asset selectors, (i.e. `select{t:prefab, @label, @prefabbase, @prefabtype}`
- Add scene object name indexing if no asset path resolves.
- Add search action Properties for the asset provider
- Add support for the dependency manager
- Add support for Vector4 filtering (i.e. `h: position<(,0,)` to find objects below the ground at 0)
- Add table view support for 2020.3
- Do not stall asset provider request when done through a search expression if the indexes are not ready.
- Fix `prefab:` variants indexing
- Fix {t:prefab} not being evaluated as an union expression
- Fix asset store provider display in compact view.
- Fix asset type selector for GameObject/Prefabs, @type will report Prefab for GameObject assets (i.e. .prefab files)
- Fix integer property filter using an floating number (i.e. `h: vertices>900.1`)
- Fix long error string display in search view status bar.
- Fix performance issue with DateTime.Now and GetDocumentKey with scene objects (~10x)
- Fix Quick Search package 3.0 can not open prefab/scene on alt+enter (case 1362526)
- Fix saving query doesn't save customized column layout (case 1365672)
- Fix scene provider GameObject serialized property filtering (i.e. `#m_Layer=0`)
- Fix scene provider item display in compact view.
- Fix search column serialization using path info instead of selector.
- Fix search file checkout when using SCM (i.e. perforce)
- Fix search table construction alias initialization using a select{} statement
- Fix single search index entry less than or equal number comparison.
- Fix sort{} with duplicate values
- Fix threaded evaluation of search expression handle leaks
- Fix where{} search expression error logging.
- Guard API attributes from user code exceptions
- Improve material and shader asset indexing. 
- Improve search auto-completion when query includes commas (,)
- Improve search indexing keyword mapping.
- Improve search proposition for upcoming Query Builder.
- Improve Search UI localization
- Improve Search UI responsiveness.
- Improve search view undo manager (i.e. CTRL+Z and CTRL+Y)
- Optimize scene ref: search queries, ~10% faster.
- Optimize search proposition enumeration
- Process search task dispatcher when running background search expressions (fix some query stalling when using SearchService.Request)
- Reduce index size by 10-20% by removing unused filters, name: and id:
- Search expression can now be nested in a filtered query, i.e. `h: ref=select{p: t:texture icons, @path}`

## [3.0.0-preview.12] - 2021-06-25
- Add API SearchService.CreateIndex to dynamically create a new index.
- Add new SearchColumn API
- Add new SearchProposition API
- Add new SearchValue API to extended QueryEngine
- Add SearchService.CreateIndex to dynamically create new search indexes
- Add SearchService.IsIndexReady to check what is the state of an index.
- Add support for read-only search table view.
- Enable PropertyTable for package version
- Filter scene properties using `#<property_name>:<value>`
- Fix search expression spreading when space in element values.
- Fix SearchService.Request with non removed delegates.
- Fix unloading prefab objects when indexing.
- Fix various UX issues related to saved search query tree view.
- Fixed Console Window log hyperlinks cursor hovering.
- Fixed package visibility when searching in the Project window when using the advanced search engine.
- Fixed search by type tags are not adapted for use with the Quick Search engine.
- Fixed text in the search field of the Search window doesn't get selected after focusing the window and clicking on the field once.
- Improve asset indexer performances and index size.
- Improve the search query tree view UI and UX
- Index scene object references using hashes.
- Mark some API as Obsolete(error)
- Reduced the size of the asset index when using Types and Dependencies options only.
- Search index combining is 175% faster
- Some new icons are not final yet.
- Update saved search panel icon

## [3.0.0-preview.9] - 2021-05-12
- Add support to save local search (not shared as an asset in the project)
- Fix prefab indexing performance issues and out of memory exception.
- Improve saved search UX.
- Improve search picker API and workflows.
- Include many minor UI and UX improvements.
- Include many QueryEngine improvements and new APIs.

## [3.0.0-preview.8] - 2021-04-07
- Do not index objects with hide flags DontSave
- Do not keep full prefab asset path keywords (saving ~1 MB of memory in large projects)
- Fix object indexing with an invalid global object id.
- Fix unavailable search index artifact timeout check
- Scope property database code with USE_PROPERTY_DATABASE

## [3.0.0-preview.7] - 2021-03-27
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
- Fix search field text selection when executing a saved query
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
- Minor UI tweaks
- More compact syntax error message. ensure params numbering makes sense
- Optimize asset provider GID resolution
- Optimize AssetProvider.Search by ~4x
- Optimize search indexes merge
- Optimize search result path comparison
- Remove existing documents from index before merging new content
- Save all index strings in a table to save space
- Save item size as a global editor preference
- Support saving query in packages
- Sync with 2021.2.0a12 (499bc7b9a4c9)
- Update the search provider active state when toggled in the search view (case 1318459)

## [3.0.0-preview.5] - 2021-02-26
- Add index types to the filter menu items
- Add new SearchService.ShowPicker API
- Add search expression language to evaluate multiple search queries and apply set operations, transformation or other user defined operation.
- Create default index when opening the index manager if it was never created before.
- Display individual search indexes are the asset provider in the filter menu (case 1307787)
- Do not clear providers when disposing of the search context
- Do not close search window on ESC if it is docked (case 1311205)
- Do not index assets with ~ in the their file path.
- Do not index redundant Assets and Packages root words
- Do not save empty Roots/Includes/Excludes in the index settings file. (case 1307800)
- Fix editor stall when the asset worker try to resolve a message log with an UnityEngine.Object in a non-main thread. (case 1316768)
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
- Show disable index in the index manager. (case 1307781)
- Sync with 2021.2.0a4 (864e4ed4e79c)
- Sync with 2021.2.0a8 (6eb956596132)
- The Roots object field is changed for a TextField to allow selection of folders outside of Assets. (case 1307793)

## [3.0.0-preview.3] - 2020-12-06
- Add a SearchQuery dedicated inspector that does a preview of the query.
- Add error highlighting when a query is illegal.
- Add grip icon in Index manager reorderable lists.
- Add i: as a new filter for interface searching.
- Add new Analytics: lots of new GenericEvent and a new ReportUsage
- Add new search tabs.
- Add support for subset of SearchItems during filtering.
- Add support to update the search view context providers (case 1296559)
- Better API on SearchItem to get preview and thumbnail.
- Change "scene" to "hierarchy" and "s:" to "h:"
- Colors are now identified with # sign for proper parsing.
- Do not fetch obsolete static APIs.
- Do not fetch preview in list view when file is over 16 mb.
- Do not show empty explicit search provider tabs (case 1296463)
- Filter providers when creating a new search context.
- Fix asset store search result formatting.
- Fix asset store settings actions.
- Fix context menu displaying empty tooltip.
- Fix IQueryHandler type constraint and word matcher naming.
- Fix package manager search provider always listing packages event if not needed.
- Fix Page-Up and Page-Down in grid view.
- Improve list, grid and details view and preview generation.
- Improve resizing of search tabs.
- Improve the asynchronous resolution of search index artifacts.
- Lots of UI tweaks.
- Optimize group item sorting.
- Optimize quick search window first load time from 270 ms to 90 ms.
- Optimize scene indexing.
- Persist the show status bar setting globally instead of per search context.
- Persist ViewState in the SearchQuery to restore icon size and other view specific state.
- Remove icon support from SearchQuery assets.
- Remove openContextual API.
- Remove support for 2020.1.
- Remove tab instead of shrinking.
- Rename context menu items for saved search query.
- Search index document paths when looking for words
- Select which search tab to show in last place.
- Set search indexing task as low priority.
- Show a different help string if only the current search tab has no result.
- Show the search result total count in the status bar.
- Sync with 10fd0d6f6164
- Sync with 2021.1.0a10 (c69a17d70606)
- Tweak UI so thumbnail view is closer to the Project Browser.
- Use Alt+Left/Right to cycle through search tabs.
- Various QueryEngine improvements and optimizations.
- Wrap around the list selection.
