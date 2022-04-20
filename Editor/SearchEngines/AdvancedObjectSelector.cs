#if USE_SEARCH_ENGINE_API
using System;
using UnityEditor.SearchService;

namespace UnityEditor.Search
{
    interface IAdvancedObjectSelector
    {
        string id { get; }
    }

    readonly struct AdvancedObjectSelectorValidator : IAdvancedObjectSelector, IEquatable<AdvancedObjectSelectorValidator>
    {
        public string id { get; }
        public readonly AdvancedObjectSelectorValidatorHandler handler;

        public AdvancedObjectSelectorValidator(string id, AdvancedObjectSelectorValidatorHandler handler)
        {
            this.id = id;
            this.handler = handler;
        }

        public bool Equals(AdvancedObjectSelectorValidator other)
        {
            return id.Equals(other.id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AdvancedObjectSelectorValidator other && Equals(other);
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }

    class AdvancedObjectSelector : IAdvancedObjectSelector, IEquatable<AdvancedObjectSelector>
    {
        public string id { get; }
        public string displayName { get; }
        public int priority { get; set; }
        public bool active { get; set; }
        public AdvancedObjectSelectorHandler handler { get; }
        public AdvancedObjectSelectorValidator validator { get; }

        public AdvancedObjectSelector(string id, string displayName, int priority, bool active, AdvancedObjectSelectorHandler handler, AdvancedObjectSelectorValidator validator)
        {
            this.id = id;
            this.displayName = displayName;
            this.priority = priority;
            this.handler = handler;
            this.active = active;
            this.validator = validator;
        }

        public bool Equals(AdvancedObjectSelector other)
        {
            if (other == null)
                return false;
            return id.Equals(other.id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AdvancedObjectSelector other && Equals(other);
        }

        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }
}
#endif
