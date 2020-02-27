using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.QuickSearch
{
    internal readonly struct FilterOperatorTypes : IEquatable<FilterOperatorTypes>
    {
        public readonly Type leftHandSideType;
        public readonly Type rightHandSideType;

        public FilterOperatorTypes(Type lhs, Type rhs)
        {
            leftHandSideType = lhs;
            rightHandSideType = rhs;
        }

        public bool Equals(FilterOperatorTypes other)
        {
            return leftHandSideType == other.leftHandSideType && rightHandSideType == other.rightHandSideType;
        }

        public override bool Equals(object obj)
        {
            return obj is FilterOperatorTypes other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((leftHandSideType != null ? leftHandSideType.GetHashCode() : 0) * 397) ^ (rightHandSideType != null ? rightHandSideType.GetHashCode() : 0);
            }
        }
    }

    internal class FilterOperator
    {
        private IQueryEngineImplementation m_EngineImplementation;

        public string token { get; }
        public Dictionary<FilterOperatorTypes, Delegate> handlers { get; }

        public FilterOperator(string token, IQueryEngineImplementation engine)
        {
            this.token = token;
            handlers = new Dictionary<FilterOperatorTypes, Delegate>();
            m_EngineImplementation = engine;
        }

        public FilterOperator AddHandler<TLhs, TRhs>(Func<TLhs, TRhs, bool> handler)
        {
            return AddHandler((TLhs l, TRhs r, StringComparison sc) => handler(l, r));
        }

        public FilterOperator AddHandler<TLhs, TRhs>(Func<TLhs, TRhs, StringComparison, bool> handler)
        {
            var operatorTypes = new FilterOperatorTypes(typeof(TLhs), typeof(TRhs));
            if (handlers.ContainsKey(operatorTypes))
                handlers[operatorTypes] = handler;
            else
                handlers.Add(operatorTypes, handler);
            m_EngineImplementation.AddFilterOperationGenerator<TRhs>();
            return this;
        }

        public Func<TLhs, TRhs, StringComparison, bool> GetHandler<TLhs, TRhs>()
        {
            var lhsType = typeof(TLhs);
            var rhsType = typeof(TRhs);
            foreach (var kvp in handlers)
            {
                if (kvp.Key.leftHandSideType == lhsType && kvp.Key.rightHandSideType == rhsType)
                    return (Func<TLhs, TRhs, StringComparison, bool>)kvp.Value;
            }
            return null;
        }
    }
}
