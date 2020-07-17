//#define DEBUG_INDEXING

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.QuickSearch
{
    internal enum FilePattern
    {
        Extension,
        Folder,
        File
    }

    /// <summary>
    /// Specialized <see cref="SearchIndexer"/> used to index Unity Assets. See <see cref="AssetIndexer"/> for a specialized SearchIndexer used to index simple assets and
    /// see <see cref="SceneIndexer"/> for an indexer used to index scene and prefabs.
    /// </summary>
    public abstract class ObjectIndexer : SearchIndexer
    {
        internal SearchDatabase.Settings settings { get; private set; }

        /// <summary>
        /// Event that triggers while indexing is happening to report progress. The event signature is
        /// <code>event(int progressId, string value, float progressReport, bool finished).</code>
        /// </summary>
        public event Action<int, string, float, bool> reportProgress;

        internal ObjectIndexer(string name, SearchDatabase.Settings settings)
            : base(name)
        {
            this.settings = settings;
        }

        /// <summary>
        /// Run a search query in the index.
        /// </summary>
        /// <param name="searchQuery">Search query to look out for. If if matches any of the indexed variations a result will be returned.</param>
        /// <param name="maxScore">Maximum score of any matched Search Result. See <see cref="SearchResult.score"/>.</param>
        /// <param name="patternMatchLimit">Maximum number of matched Search Result that can be returned. See <see cref="SearchResult"/>.</param>
        /// <returns>Returns a collection of Search Result matching the query.</returns>
        public override IEnumerable<SearchResult> Search(string searchQuery, int maxScore = int.MaxValue, int patternMatchLimit = 2999)
        {
            if (settings.options.disabled)
                return Enumerable.Empty<SearchResult>();
            return base.Search(searchQuery, maxScore, patternMatchLimit);
        }

        /// <summary>
        /// Build the index into a separate thread.
        /// </summary>
        public override void Build()
        {
            throw new NotSupportedException("Object indexer uses asset workers to produce index artifacts.");
        }

        /// <summary>
        /// Called when the index is built to see if a specified document needs to be indexed. See <see cref="SearchIndexer.skipEntryHandler"/>
        /// </summary>
        /// <param name="path">Path of a document</param>
        /// <param name="checkRoots"></param>
        /// <returns>Returns true if the document doesn't need to be indexed.</returns>
        public override bool SkipEntry(string path, bool checkRoots = false)
        {
            if (String.IsNullOrEmpty(path))
                return true;

            if (checkRoots)
            {
                if (!GetRoots().Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            var ext = Path.GetExtension(path);

            // Exclude some file extensions by default
            if (ext.Equals(".meta", StringComparison.Ordinal) ||
                ext.Equals(".index", StringComparison.Ordinal))
                return true;

            if (settings.includes?.Length > 0 || settings.excludes?.Length > 0)
            {
                var dir = Path.GetDirectoryName(path).Replace("\\", "/");

                if (settings.includes?.Length > 0 && !settings.includes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                    return true;

                if (settings.excludes?.Length > 0 && settings.excludes.Any(pattern => PatternChecks(pattern, ext, dir, path)))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///  Get all this indexer root paths.
        /// </summary>
        /// <returns>Returns a list of root paths.</returns>
        public abstract IEnumerable<string> GetRoots();

        /// <summary>
        /// Get all documents that would be indexed.
        /// </summary>
        /// <returns>Returns a list of file paths.</returns>
        public abstract List<string> GetDependencies();

        /// <summary>
        /// Compute the hash of a specific document id. Generally a file path.
        /// </summary>
        /// <param name="id">Document id.</param>
        /// <returns>Returns the hash of this document id.</returns>
        public abstract Hash128 GetDocumentHash(string id);

        /// <summary>
        /// Function to override in a concrete SearchIndexer to index the content of a document.
        /// </summary>
        /// <param name="id">Path of the document to index.</param>
        /// <param name="checkIfDocumentExists">Check if the document actually exists.</param>
        public abstract override void IndexDocument(string id, bool checkIfDocumentExists);

        /// <summary>
        /// Split a word into multiple components.
        /// </summary>
        /// <param name="documentIndex">Document where the indexed word was found.</param>
        /// <param name="word">Word to add to the index.</param>
        public void IndexWordComponents(int documentIndex, string word)
        {
            int scoreModifier = 0;
            foreach (var c in GetEntryComponents(word, documentIndex))
                IndexWord(documentIndex, c, scoreModifier: scoreModifier++);
        }

        /// <summary>
        /// Split a value into multiple components.
        /// </summary>
        /// <param name="documentIndex">Document where the indexed word was found.</param>
        /// <param name="name">Key used to retrieve the value. See <see cref="SearchIndexer.AddProperty"/></param>
        /// <param name="value">Value to add to the index.</param>
        public void IndexPropertyComponents(int documentIndex, string name, string value)
        {
            int scoreModifier = 0;
            foreach (var c in GetEntryComponents(value, documentIndex))
                AddProperty(name, c, settings.baseScore + scoreModifier++, documentIndex, saveKeyword: true, exact: false);
            AddExactProperty(name, value.ToLowerInvariant(), settings.baseScore, documentIndex, saveKeyword: false);
        }

        /// <summary>
        /// Splits a string into multiple words that will be indexed.
        /// It works with paths and UpperCamelCase strings.
        /// </summary>
        /// <param name="entry">The string to be split.</param>
        /// <param name="documentIndex">The document index that will index that entry.</param>
        /// <returns>The entry components.</returns>
        protected virtual IEnumerable<string> GetEntryComponents(string entry, int documentIndex)
        {
            return SearchUtils.SplitFileEntryComponents(entry, SearchUtils.entrySeparators);
        }

        /// <summary>
        /// Add a new word coming from a specific document to the index. The word will be added with multiple variations allowing partial search. See <see cref="SearchIndexer.AddWord"/>.
        /// </summary>
        /// <param name="word">Word to add to the index.</param>
        /// <param name="documentIndex">Document where the indexed word was found.</param>
        /// <param name="maxVariations">Maximum number of variations to compute. Cannot be higher than the length of the word.</param>
        /// <param name="exact">If true, we will store also an exact match entry for this word.</param>
        /// <param name="scoreModifier">Modified to apply to the base score for a specific word.</param>
        public void IndexWord(int documentIndex, string word, int maxVariations, bool exact, int scoreModifier = 0)
        {
            var modifiedScore = settings.baseScore + scoreModifier;
            AddWord(word.ToLowerInvariant(), minWordIndexationLength, maxVariations, modifiedScore, documentIndex);
            if (exact)
                AddExactWord(word.ToLowerInvariant(), modifiedScore, documentIndex);
        }

        /// <summary>
        /// Add a new word coming from a specific document to the index. The word will be added with multiple variations allowing partial search. See <see cref="SearchIndexer.AddWord"/>.
        /// </summary>
        /// <param name="word">Word to add to the index.</param>
        /// <param name="documentIndex">Document where the indexed word was found.</param>
        /// <param name="exact">If true, we will store also an exact match entry for this word.</param>
        /// <param name="scoreModifier">Modified to apply to the base score for a specific word.</param>
        public void IndexWord(int documentIndex, string word, bool exact = false, int scoreModifier = 0)
        {
            IndexWord(documentIndex, word, word.Length, exact, scoreModifier: scoreModifier);
        }

        /// <summary>
        /// Add a property value to the index. A property is specified with a key and a string value. The value will be stored with multiple variations. See <see cref="SearchIndexer.AddProperty"/>.
        /// </summary>
        /// <param name="name">Key used to retrieve the value. See <see cref="SearchIndexer.AddProperty"/></param>
        /// <param name="value">Value to add to the index.</param>
        /// <param name="documentIndex">Document where the indexed word was found.</param>
        /// <param name="saveKeyword">Define if we store this key in the keyword registry of the index. See <see cref="SearchIndexer.GetKeywords"/>.</param>
        /// <param name="exact">If exact is true, only the exact match of the value will be stored in the index (not the variations).</param>
        public void IndexProperty(int documentIndex, string name, string value, bool saveKeyword, bool exact = false)
        {
            if (String.IsNullOrEmpty(value))
                return;
            var valueLower = value.ToLowerInvariant();
            if (exact)
            {
                AddProperty(name, valueLower, valueLower.Length, valueLower.Length, settings.baseScore, documentIndex, saveKeyword: saveKeyword, exact: true);
            }
            else
                AddProperty(name, valueLower, settings.baseScore, documentIndex, saveKeyword: saveKeyword);
        }

        /// <summary>
        /// Add a key-number value pair to the index. The key won't be added with variations. See <see cref="SearchIndexer.AddNumber"/>.
        /// </summary>
        /// <param name="name">Key used to retrieve the value.</param>
        /// <param name="number">Number value to store in the index.</param>
        /// <param name="documentIndex">Document where the indexed value was found.</param>
        public void IndexNumber(int documentIndex, string name, double number)
        {
            AddNumber(name, number, settings.baseScore, documentIndex);
        }

        /// <summary>
        /// Report progress of indexing.
        /// </summary>
        /// <param name="progressId">Progress id.</param>
        /// <param name="value">Progress description.</param>
        /// <param name="progressReport">Progress report value (between 0 and 1).</param>
        /// <param name="finished">Is the indexing done?</param>
        protected void ReportProgress(int progressId, string value, float progressReport, bool finished)
        {
            reportProgress?.Invoke(progressId, value, progressReport, finished);
        }

        [Obsolete("Async index builds are not supported anymore.")]
        protected virtual System.Collections.IEnumerator BuildAsync(int progressId, object userData = null)
        {
            throw new NotSupportedException();
        }

        internal static FilePattern GetFilePattern(string pattern)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                if (pattern[0] == '.')
                    return FilePattern.Extension;
                if (pattern[pattern.Length - 1] == '/')
                    return FilePattern.Folder;
            }
            return FilePattern.File;
        }

        private bool PatternChecks(string pattern, string ext, string dir, string filePath)
        {
            var filePattern = GetFilePattern(pattern);

            switch (filePattern)
            {
                case FilePattern.Extension:
                    return ext.Equals(pattern, StringComparison.OrdinalIgnoreCase);
                case FilePattern.Folder:
                    var icDir = pattern.Substring(0, pattern.Length - 1);
                    return dir.IndexOf(icDir, StringComparison.OrdinalIgnoreCase) != -1;
                case FilePattern.File:
                    return filePath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) != -1;
            }

            return false;
        }

        /// <summary>
        /// Get the property value of a specific property. This will converts the property to either a double or a string.
        /// </summary>
        /// <param name="property">Property to get value from.</param>
        /// <param name="saveKeyword">If set to true, this means we need to save the property name in the index.</param>
        /// <returns>Property value as a double or a string or null if we weren't able to convert it.</returns>
        [Obsolete("This override is not supported anymore")]
        protected object GetPropertyValue(SerializedProperty property, ref bool saveKeyword)
        {
            throw new NotSupportedException("This not supported anymore");
        }

        /// <summary>
        /// Index all the properties of an object.
        /// </summary>
        /// <param name="obj">Object to index.</param>
        /// <param name="documentIndex">Document where the indexed object was found.</param>
        /// <param name="dependencies">Index dependencies.</param>
        protected void IndexObject(int documentIndex, Object obj, bool dependencies = false, bool recursive = false)
        {
            using (var so = new SerializedObject(obj))
            {
                var p = so.GetIterator();
                var next = p.NextVisible(true);
                while (next)
                {
                    var fieldName = p.displayName.Replace("m_", "").Replace(" ", "").ToLowerInvariant();
                    if (p.propertyPath[p.propertyPath.Length-1] != ']')
                        IndexProperty(documentIndex, fieldName, p);

                    if (dependencies)
                        AddReference(documentIndex, p);

                    next = p.NextVisible(ShouldIndexChildren(p, recursive));
                }
            }
        }

        private bool ShouldIndexChildren(SerializedProperty p, bool recursive)
        {
            if ((p.isArray || p.isFixedBuffer) && !recursive)
                return false;

            if (p.propertyType == SerializedPropertyType.Vector2 ||
                p.propertyType == SerializedPropertyType.Vector3 ||
                p.propertyType == SerializedPropertyType.Vector4)
                return false;

            return true;
        }

        private void IndexProperty(int documentIndex, string fieldName, SerializedProperty p)
        {
            #if DEBUG_INDEXING
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, $"[DEBUG] {p.serializedObject.targetObject.name}, " +
                $"array={p.isArray || p.isFixedBuffer}, children={p.hasVisibleChildren}, {p.propertyPath}, {fieldName}({p.propertyType}/{p.type})");
            #endif

            if (p.isArray && p.propertyType != SerializedPropertyType.String)
                IndexNumber(documentIndex, fieldName, p.arraySize);

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    IndexNumber(documentIndex, fieldName, (double)p.intValue);
                    break;
                case SerializedPropertyType.Boolean:
                    IndexProperty(documentIndex, fieldName, p.boolValue.ToString().ToLowerInvariant(), saveKeyword: false, exact: true);
                    break;
                case SerializedPropertyType.Float:
                    IndexNumber(documentIndex, fieldName, (double)p.floatValue);
                    break;
                case SerializedPropertyType.String:
                    if (!string.IsNullOrEmpty(p.stringValue) || p.stringValue.Length <= 16 || p.stringValue.LastIndexOf(' ') == -1)
                        IndexProperty(documentIndex, fieldName, p.stringValue.ToLowerInvariant(), saveKeyword: false, exact: false);
                    break;
                case SerializedPropertyType.Enum:
                    if (p.enumValueIndex >= 0 && p.type == "Enum")
                        IndexProperty(documentIndex, fieldName, p.enumNames[p.enumValueIndex].Replace(" ", "").ToLowerInvariant(), saveKeyword: true, exact: false);
                    break;
                case SerializedPropertyType.Color:
                    IndexerExtensions.IndexColor(fieldName, p.colorValue, this, documentIndex);
                    break;
                case SerializedPropertyType.Vector2:
                    IndexerExtensions.IndexVector(fieldName, p.vector2Value, this, documentIndex);
                    break;
                case SerializedPropertyType.Vector3:
                    IndexerExtensions.IndexVector(fieldName, p.vector3Value, this, documentIndex);
                    break;
                case SerializedPropertyType.Vector4:
                    IndexerExtensions.IndexVector(fieldName, p.vector4Value, this, documentIndex);
                    break;
                case SerializedPropertyType.Quaternion:
                    IndexerExtensions.IndexVector(fieldName,  p.quaternionValue.eulerAngles, this, documentIndex);
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (p.objectReferenceValue && !string.IsNullOrEmpty(p.objectReferenceValue.name))
                        IndexProperty(documentIndex, fieldName, p.objectReferenceValue.name.ToLowerInvariant(), saveKeyword: false, exact: true);
                    break;
                case SerializedPropertyType.Generic:
                    IndexProperty(documentIndex, "has", p.type.ToLowerInvariant(), saveKeyword: true, exact: true);
                    break;
            }

            MapKeyword(fieldName+":", $"{p.displayName} ({p.propertyType})");
        }

        private void AddReference(int documentIndex, SerializedProperty p)
        {
            if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue)
                return;

            AddReference(documentIndex, AssetDatabase.GetAssetPath(p.objectReferenceValue));
        }

        internal void AddReference(int documentIndex, string assetPath, bool saveKeyword = false)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            assetPath = assetPath.ToLowerInvariant();
            IndexProperty(documentIndex, "ref", assetPath, saveKeyword: false, exact: true);
            IndexProperty(documentIndex, "ref", Path.GetFileName(assetPath), saveKeyword);
        }

        /// <summary>
        /// Call all the registered custom indexer for a specific object. See <see cref="CustomObjectIndexerAttribute"/>.
        /// </summary>
        /// <param name="documentId">Document index.</param>
        /// <param name="documentIndex">Document where the indexed object was found.</param>
        /// <param name="obj">Object to index.</param>
        protected void IndexCustomProperties(string documentId, int documentIndex, Object obj)
        {
            using (var so = new SerializedObject(obj))
            {
                CallCustomIndexers(documentId, documentIndex, obj, so);
            }
        }

        /// <summary>
        /// Call all the registered custom indexer for an object of a specific type. See <see cref="CustomObjectIndexerAttribute"/>.
        /// </summary>
        /// <param name="documentId">Document id.</param>
        /// <param name="obj">Object to index.</param>
        /// <param name="documentIndex">Document where the indexed object was found.</param>
        /// <param name="so">SerializedObject representation of obj.</param>
        /// <param name="multiLevel">If true, calls all the indexer that would fit the type of the object (all assignable type). If false only check for an indexer registered for the exact type of the Object.</param>
        protected void CallCustomIndexers(string documentId, int documentIndex, Object obj, SerializedObject so, bool multiLevel = true)
        {
            var objectType = obj.GetType();
            List<CustomIndexerHandler> customIndexers;
            if (!multiLevel)
            {
                if (!CustomIndexers.TryGetValue(objectType, out customIndexers))
                    return;
            }
            else
            {
                customIndexers = new List<CustomIndexerHandler>();
                foreach (var indexerType in CustomIndexers.types)
                {
                    if (indexerType.IsAssignableFrom(objectType))
                        customIndexers.AddRange(CustomIndexers.GetHandlers(indexerType));
                }
            }

            var indexerTarget = new CustomObjectIndexerTarget
            {
                id = documentId,
                documentIndex = documentIndex,
                target = obj,
                serializedObject = so,
                targetType = objectType
            };

            foreach (var customIndexer in customIndexers)
            {
                customIndexer(indexerTarget, this);
            }
        }

        /// <summary>
        /// Checks if we have a custom indexer for the specified type.
        /// </summary>
        /// <param name="type">Type to lookup</param>
        /// <param name="multiLevel">Check for subtypes too.</param>
        /// <returns>True if a custom indexer exists, otherwise false is returned.</returns>
        protected bool HasCustomIndexers(Type type, bool multiLevel = true)
        {
            return CustomIndexers.HasCustomIndexers(type, multiLevel);
        }
    }
}
