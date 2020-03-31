using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Unity.QuickSearch
{
    [DebuggerDisplay("{DisplayName} - {Id}")]
    public class SearchAction
    {
        /// <summary>
        /// Default constructor to build a search action.
        /// </summary>

        public SearchAction(string providerId, GUIContent content)
        {
            this.providerId = providerId;
            this.content = content;
            handler = null;
            execute = null;
            enabled = (a, b) => true;
        }

        public SearchAction(string providerId, GUIContent content, Action<SearchContext, SearchItem[]> handler)
            : this(providerId, content)
        {
            execute = handler;
        }

        public SearchAction(string providerId, GUIContent content, Action<SearchItem, SearchContext> handler)
            : this(providerId, content)
        {
            this.handler = handler;
        }

        /// <summary>
        /// Extended constructor to build a search action.
        /// </summary>
        public SearchAction(string providerId, string name, Texture2D icon, string tooltip, Action<SearchContext, SearchItem[]> handler)
            : this(providerId, new GUIContent(name, icon, tooltip ?? name), handler)
        {
        }

        public SearchAction(string providerId, string name, Texture2D icon, string tooltip, Action<SearchItem, SearchContext> handler)
            : this(providerId, new GUIContent(name, icon, tooltip ?? name), handler)
        {
        }

        public SearchAction(string providerId, string name, Texture2D icon = null, string tooltip = null)
            : this(providerId, new GUIContent(name, icon, tooltip ?? name))
        {
        }

        /// <summary>
        /// Action unique identifier.
        /// </summary>
        public string Id => content.text;

        /// <summary>
        /// Name used to display
        /// </summary>
        public string DisplayName => content.tooltip;

        /// <summary>
        /// Indicates if the search view should be closed after the action execution.
        /// </summary>
        public bool closeWindowAfterExecution = true;

        /// <summary>
        /// Unique (for a given provider) id of the action
        /// </summary>
        internal string providerId;

        /// <summary>
        /// GUI content used to display the action in the search view.
        /// </summary>
        internal GUIContent content;

        /// <summary>
        /// Callback used to check if the action is enabled based on the current context.
        /// </summary>
        public Func<SearchContext, IReadOnlyCollection<SearchItem>, bool> enabled;

        /// <summary>
        /// Execute a action on a set of items.
        /// </summary>
        public Action<SearchContext, SearchItem[]> execute;

        /// <summary>
        /// This handler is used for action that do not support multi selection.
        /// </summary>
        public Action<SearchItem, SearchContext> handler;
    }
}