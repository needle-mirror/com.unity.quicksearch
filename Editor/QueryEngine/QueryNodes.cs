using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.QuickSearch
{
    internal enum QueryNodeType
    {
        And,
        Or,
        Filter,
        Search,
        Not,
        NoOp
    }

    internal interface IQueryNode
    {
        IQueryNode parent { get; set; }
        QueryNodeType type { get; }
        List<IQueryNode> children { get; }
        bool leaf { get; }
        string identifier { get; }
        int QueryHashCode();
    }

    internal class FilterNode : IQueryNode
    {
        public IFilterOperation filterOperation;

        public IQueryNode parent { get; set; }
        public QueryNodeType type => QueryNodeType.Filter;
        public List<IQueryNode> children => new List<IQueryNode>();
        public bool leaf => true;
        public string identifier { get; }

        public FilterNode(IFilterOperation operation, string filterString)
        {
            filterOperation = operation;
            identifier = filterString;
        }

        public int QueryHashCode()
        {
            return identifier.GetHashCode();
        }
    }

    internal class SearchNode : IQueryNode
    {
        public bool exact { get; }
        public string searchValue { get; }

        public IQueryNode parent { get; set; }
        public QueryNodeType type => QueryNodeType.Search;
        public List<IQueryNode> children => new List<IQueryNode>();
        public bool leaf => true;
        public string identifier { get; private set; }

        public SearchNode(string searchValue, bool isExact)
        {
            this.searchValue = searchValue;
            exact = isExact;
            identifier = exact ? ("!" + searchValue) : searchValue;
        }

        public int QueryHashCode()
        {
            return identifier.GetHashCode();
        }
    }

    internal abstract class CombinedNode : IQueryNode
    {
        public IQueryNode parent { get; set; }
        public abstract QueryNodeType type { get; }
        public List<IQueryNode> children { get; }
        public bool leaf => children.Count == 0;
        public abstract string identifier { get; }

        protected CombinedNode()
        {
            children = new List<IQueryNode>();
        }

        public void AddNode(IQueryNode node)
        {
            children.Add(node);
            node.parent = this;
        }

        public void RemoveNode(IQueryNode node)
        {
            if (!children.Contains(node))
                return;

            children.Remove(node);
            if (node.parent == this)
                node.parent = null;
        }

        public void Clear()
        {
            foreach (var child in children)
            {
                if (child.parent == this)
                    child.parent = null;
            }
            children.Clear();
        }

        public abstract void SwapChildNodes();

        public int QueryHashCode()
        {
            var hc = 0;
            foreach (var child in children)
            {
                hc ^= child.GetHashCode();
            }
            return hc;
        }
    }

    internal class AndNode : CombinedNode
    {
        public override QueryNodeType type => QueryNodeType.And;
        public override string identifier => "(" + children[0].identifier + " " + children[1].identifier + ")";

        public override void SwapChildNodes()
        {
            if (children.Count != 2)
                return;

            var tmp = children[0];
            children[0] = children[1];
            children[1] = tmp;
        }
    }

    internal class OrNode : CombinedNode
    {
        public override QueryNodeType type => QueryNodeType.Or;
        public override string identifier => "(" + children[0].identifier + " or " + children[1].identifier + ")";

        public override void SwapChildNodes()
        {
            if (children.Count != 2)
                return;

            var tmp = children[0];
            children[0] = children[1];
            children[1] = tmp;
        }
    }

    internal class NotNode : CombinedNode
    {
        public override QueryNodeType type => QueryNodeType.Not;
        public override string identifier => "-" + children[0].identifier;

        public override void SwapChildNodes()
        { }
    }

    internal sealed class NoOpNode : IQueryNode
    {
        public IQueryNode parent { get; set; }
        public QueryNodeType type => QueryNodeType.NoOp;
        public List<IQueryNode> children => new List<IQueryNode>();
        public bool leaf => true;
        public string identifier { get; }

        public NoOpNode(string identifier)
        {
            this.identifier = identifier;
        }

        public int QueryHashCode()
        {
            return identifier.GetHashCode();
        }
    }
}
