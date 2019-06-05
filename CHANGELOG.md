## [1.2.3-preview] - 2019-06-06
- Add a new help search provider to guide the user how to use other search providers (type ? to get a list of search help items).
- [UX] Draw some nice up and down arrows to order the search provider priority.
- [UX] Add a status bar to give some context to the user what is going on and what is filtered or not.
- [UX] Add a Reset Priorities button in the Settings window to restore all default search provider priorities if messed up for whatever reason.
- [FIX] Fix the ordering of search provider priorities in the Settings window.
- [FIX] Fix the order of explicit providers in the filter popup window.
- [FIX] Fix a rare exception when the SearchIndexer is trying to write to the temporary index DB.
- [API] Add `SearchAction.closeWindowAfterExecution` to indicate if the window should be closed after executing an action. Now an action can append to the search text and show additional search items.
- [API] Add `ISearchView` interface for `SearchContext.searchView` to give access to some functionalities of the Quick Search host window.

## [1.2.2-preview] - 2019-06-05
- Add more performance, productivity and fun!
- Add fuzzy search to scene objects search provider.
- [UX] Separate regular search providers from explicit search providers in the filter popup window.
- [UX] Make the Quick Search window default size larger.
- [UX] Make the asset indexed search the default search for the asset search provider.
- [UX] Allow the user to change the default action to be executed for search item with multiple actions.
- [UX] Add more tooltips to some controls to indicate more advance features.
- [UX] Add ... to long search item description and show the entire description as a tooltip.
- [API] Expose the SearchIndexer API to easily add new search provider with indexed data.
- [API] Add SearchProvider.isExplicitProvider to mark a provider as explicit. An explicit provider only list search result if the the search query start with its filter id. In example, # will get into the calculator search provider and # will only return static method APIs.
- [API] Add Search.customDescriptionFormatter to allow a search provider to indicate that the item description was already done and should not be done generically (used by the scene object search provider fuzzy search). 

## [1.2.1-preview] - 2019-05-29
- Various domain reload fixes

## [1.2.0-preview.1] - 2019-05-28
- Add >COMMAND to execute a specific command on a specific item. (i.e. >reveal "Assets/My.asset", will reveal in the asset in the file explorer directly)
- Add a search provider to quickly install packages.
- Add a search provider to search and invoke any static method API.
- Add Alt+Shift+C shortcut to open the quick search contextually to the selected editor window.
- Add auto completion of asset type filter (i.e. start typing t:)
- Add contextual action support to search item (i.e. opened by right clicking on an asset)
- Add index acronym search result (i.e. WL will match Assets/WidgetLibrary.cs)
- Add indexing item scoring to sort them (items with a better match are sorted first)
- Add QuickSearchTool.OpenWithContextualProvider API to open the quick search for a specific type only.
- Add selected search settings to analytics.
- Add selection tracking fo the selected search item (can be turned off in the settings)
- Add support for package indexing
- Add support to sort search provider priorities in the user preferences
- Commands using > are shown using the auto-complete drop down.
- Do not show search provider in the filter window when opened for a specific type.
- Give a higher score to recently used items so they get sorted first when searched.
- Improve filter window styling
- Improve item hovering when moving the mouse over items.
- Improved fast typing debouncing
- Improved item description highlighting of matching keywords
- Launch Quick Search the first time it was installed from the an official 19.3 release.
- Potential fix for ThreadAbortException
- Remove type sub filters to the asset provider
- Skip root items starting with a .

## [1.1.0-preview.1] - 2019-05-14
- Add a switch to let search provider fetch more results if requested (e.g. `SearchContext.wantsMore`)
- Add selection tracking to the `SearchProvider` API.
- Fix search item caching.
- Improve quick search debouncing when typing.
- Ping scene objects when the selection changes to a scene provider search item in the Quick Search window.
- Track project changes so search provider can refresh themselves.
- Update file indexes when the project content changes.
- Update UI for Northstar retheming.
  
## [1.0.9-preview.2] - 2019-05-06
- Add editor analytics
- Add fast indexing of the project assets file system.
- Add option to open the quick search in a dockable window.
- Add search service tests
- Add shortcut key bindings to the menu item descriptor.
- Add typing debouncing (to prevent searching for nothing while typing fast)
- Add yamato-CI
- Format the search item description to highlight the search terms.
- Improve documentation
- Improve the performance of searches when folder filter is selected.
- Record average fetch time of search provider and display those times in the Filter Window.
- Remove assets and scene search provider default shortcuts. They were conflicting with other core shortcuts.
- Remove duplicate items from search result.

## [1.0.1-preview.1] - 2019-02-27
- Fix ReflectionTypeLoadException exception for certain user with invalid project assemblies
- Optimize fetching results from menu, asset and scene search providers (~50x boost).

## [1.0.0-preview.2] - 2019-02-26
- Update style to work better with the Northstar branch.
- Use Alt+Left to open the filter window box.
- Use Alt+Down to cycle to next recent search.
- Navigate the filter window using the keyboard only.
- Consolidate web search providers into a single one with sub categories.
- Update documentation to include new features.
- Set the game object as the selected active game object when activated.

## [0.9.95-preview] - 2019-02-25
- Add AssetStoreProvider example
- Add HTTP asynchronous item loading by querying the asset store.
- Add onEnable/onDisable API for Search provider.
- Add Page Up and Down support to scroll the item list faster.
- Add search provider examples
- Add support for async results
- Cycle too previous search query using ALT+UpArrow
- Fix various Mac UX issues
- New icons
- Select first item by default en pressing

## [0.9.9-preview.2] - 2019-02-21
- Added drag and drop support. You can drag an item from the quick search tool to a drop target.
- Open the item contextual menu by pressing the keyboard right arrow of the selected item.
- Fixed folder entry search results when only the folder filter is selected.
- Fixed showing popup when alt-tabbing between windows.
- Add support for rich text label and description.
- Updated documentation

## [0.9.7-preview.3] - 2019-02-20
- Fixed cursor blinking
- Improved fetched scene object results

## [0.9.6-preview.1] - 2019-02-20
- Moved menu items under Help/
- Added a warning when all filters are disabled.
- Added search field cursor blinking.
- Added search field placeholder text when not search is made yet.
- Fixed a layout scoping issue when scrolling.

## [0.9.5-preview.1] - 2019-02-19
- First Version
- Search menu items
- Search project assets
- Search current scene game objects
- Search project and preference settings
- Build a search query for  Unity Answers web site
- Build a search query for the Unity Documentation web site
- Build a search query for the Unity Store web site
