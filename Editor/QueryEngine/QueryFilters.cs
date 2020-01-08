using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Unity.QuickSearch
{
    internal interface IFilter
    {
        string token { get; }
        IEnumerable<string> supportedFilters { get; }
        Type type { get; }
        bool SupportsValue(string value);
        bool paramFilter { get; }
        Type paramType { get; }
        bool resolver { get; }
    }

    internal abstract class BaseFilter<TFilter> : IFilter
    {
        public string token { get; protected set; }
        public IEnumerable<string> supportedFilters { get; protected set; }
        public Type type => typeof(TFilter);
        public virtual bool paramFilter => false;
        public virtual Type paramType => typeof(object);

        public bool resolver { get; protected set; }

        protected BaseFilter(string token, IEnumerable<string> supportedOperatorType)
        {
            this.token = token;
            supportedFilters = supportedOperatorType ?? new string[] { };
            resolver = false;
        }

        public bool SupportsValue(string value)
        {
            var converter = TypeDescriptor.GetConverter(type);
            return converter.IsValid(value);
        }
    }

    internal class Filter<TObject, TFilter> : BaseFilter<TFilter>
    {
        private Func<TObject, TFilter> m_GetDataCallback;
        private Func<TObject, string, TFilter, bool> m_FilterResolver;

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, TFilter> getDataCallback)
            : base(token, supportedOperatorType)
        {
            m_GetDataCallback = getDataCallback;
        }

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, string, TFilter, bool> resolver)
            : base(token, supportedOperatorType)
        {
            m_FilterResolver = resolver;
            this.resolver = true;
        }

        public TFilter GetData(TObject o)
        {
            return m_GetDataCallback(o);
        }

        public bool Resolve(TObject data, FilterOperator op, TFilter value)
        {
            if (!resolver)
                return false;
            return m_FilterResolver(data, op.token, value);
        }
    }

    internal class Filter<TObject, TParam, TFilter> : BaseFilter<TFilter>
    {
        public override bool paramFilter => true;
        public override Type paramType => typeof(TParam);

        private Func<TObject, TParam, TFilter> m_GetDataCallback;
        private Func<TObject, TParam, string, TFilter, bool> m_FilterResolver;
        private Func<string, TParam> m_ParameterTransformer;

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, TParam, TFilter> getDataCallback)
            : base(token, supportedOperatorType)
        {
            m_GetDataCallback = getDataCallback;
        }

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, TParam, TFilter> getDataCallback, Func<string, TParam> parameterTransformer)
            : base(token, supportedOperatorType)
        {
            m_GetDataCallback = getDataCallback;
            m_ParameterTransformer = parameterTransformer;
        }

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, TParam, string, TFilter, bool> resolver)
            : base(token, supportedOperatorType)
        {
            m_FilterResolver = resolver;
            this.resolver = true;
        }

        public Filter(string token, IEnumerable<string> supportedOperatorType, Func<TObject, TParam, string, TFilter, bool> resolver, Func<string, TParam> parameterTransformer)
            : base(token, supportedOperatorType)
        {
            m_FilterResolver = resolver;
            m_ParameterTransformer = parameterTransformer;
            this.resolver = true;
        }

        public TFilter GetData(TObject o, TParam p)
        {
            return m_GetDataCallback(o, p);
        }

        public bool Resolve(TObject data, TParam param, FilterOperator op, TFilter value)
        {
            if (!resolver)
                return false;
            return m_FilterResolver(data, param, op.token, value);
        }

        public TParam TransformParameter(string param)
        {
            if (m_ParameterTransformer != null)
                return m_ParameterTransformer(param);

            return Utils.ConvertValue<TParam>(param);
        }
    }

    internal class DefaultFilter<TObject> : Filter<TObject, string>
    {
        public DefaultFilter(string token, Func<TObject, string, string, string, bool> handler)
            : base(token, null, (o, op, value) => handler(o, token, op, value))
        { }
    }

    internal class DefaultParamFilter<TObject> : Filter<TObject, string, string>
    {
        public DefaultParamFilter(string token, Func<TObject, string, string, string, string, bool> handler)
            : base(token, null, (o, param, op, value) => handler(o, token, param, op, value))
        { }
    }
}
