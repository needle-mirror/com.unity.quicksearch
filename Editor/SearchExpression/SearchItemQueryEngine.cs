using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;

namespace UnityEditor.Search
{
    readonly struct PropertyRange
    {
        public readonly double min;
        public readonly double max;

        public PropertyRange(double min, double max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Contains(double f)
        {
            if (f >= min && f <= max)
                return true;
            return false;
        }
    }

    readonly struct SearchColor : IEquatable<SearchColor>, IComparable<SearchColor>
    {
        public readonly byte r;
        public readonly byte g;
        public readonly byte b;
        public readonly byte a;

        public SearchColor(Color c)
        {
            r = (byte)Mathf.RoundToInt(c.r * 255f);
            g = (byte)Mathf.RoundToInt(c.g * 255f);
            b = (byte)Mathf.RoundToInt(c.b * 255f);
            a = (byte)Mathf.RoundToInt(c.a * 255f);
        }

        public SearchColor(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public byte this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default:
                        throw new IndexOutOfRangeException("Invalid Color index(" + index + ")!");
                }
            }
        }

        public bool Equals(SearchColor other)
        {
            for (var i = 0; i < 4; ++i)
            {
                if (this[i] != other[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            if (obj is SearchColor ic)
                return base.Equals(ic);
            return false;
        }

        public override int GetHashCode()
        {
            return r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1);
        }

        public int CompareTo(SearchColor other)
        {
            for (var i = 0; i < 4; ++i)
            {
                if (this[i] > other[i])
                    return 1;
                if (this[i] < other[i])
                    return -1;
            }

            return 0;
        }

        public static bool operator==(SearchColor lhs, SearchColor rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator!=(SearchColor lhs, SearchColor rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static bool operator>(SearchColor lhs, SearchColor rhs)
        {
            return lhs.CompareTo(rhs) > 0;
        }

        public static bool operator<(SearchColor lhs, SearchColor rhs)
        {
            return lhs.CompareTo(rhs) < 0;
        }

        public static bool operator>=(SearchColor lhs, SearchColor rhs)
        {
            return lhs.CompareTo(rhs) >= 0;
        }

        public static bool operator<=(SearchColor lhs, SearchColor rhs)
        {
            return lhs.CompareTo(rhs) <= 0;
        }

        public override string ToString()
        {
            return $"RGBA({r}, {g}, {b}, {a})";
        }
    }

    public readonly struct SearchValue
    {
        public enum ValueType : byte
        {
            Nil = 0,
            Bool,
            Number,
            Text,
            Color
        }

        public readonly ValueType type;
        public readonly double number;
        public readonly string text;
        internal readonly SearchColor color;
        public bool boolean => type == ValueType.Bool && number == 1d;

        public bool valid => type != ValueType.Nil;

        public static SearchValue invalid = new SearchValue();

        public SearchValue(bool v)
        {
            this.type = ValueType.Bool;
            this.number = v ? 1d : 0f;
            this.text = null;
            this.color = default;
        }

        public SearchValue(float number)
        {
            this.type = ValueType.Number;
            this.number = Convert.ToDouble(number);
            this.text = null;
            this.color = default;
        }

        public SearchValue(double number)
        {
            this.type = ValueType.Number;
            this.number = number;
            this.text = null;
            this.color = default;
        }

        public SearchValue(string text)
        {
            this.type = ValueType.Text;
            this.number = float.NaN;
            this.text = text;
            this.color = default;
        }

        public SearchValue(Color color)
        {
            this.type = ValueType.Color;
            this.number = float.NaN;
            this.text = null;
            this.color = new SearchColor(color);
        }

        internal SearchValue(SearchColor color)
        {
            this.type = ValueType.Color;
            this.number = float.NaN;
            this.text = null;
            this.color = color;
        }

        public SearchValue(object v)
        {
            if (v == null)
            {
                this.type = ValueType.Nil;
                this.number = float.NaN;
                this.text = null;
                this.color = default;
            }
            else if (v is bool b)
            {
                this.type = ValueType.Bool;
                this.number = b ? 1 : 0;
                this.text = null;
                this.color = default;
            }
            else if (v is string s)
            {
                this.type = ValueType.Text;
                this.number = float.NaN;
                this.text = s;
                this.color = default;
            }
            else if (v is Color c)
            {
                this.type = ValueType.Color;
                this.number = float.NaN;
                this.text = null;
                this.color = new SearchColor(c);
            }
            else if (Utils.TryGetNumber(v, out var d))
            {
                this.type = ValueType.Number;
                this.number = (float)d;
                this.text = null;
                this.color = default;
            }
            else
            {
                this.type = ValueType.Text;
                this.number = float.NaN;
                this.text = v.ToString();
                this.color = default;
            }
        }

        public override string ToString()
        {
            switch (type)
            {
                case ValueType.Bool: return $"{boolean} [{type}]";
                case ValueType.Number: return $"{number} [{type}]";
                case ValueType.Text: return $"{text} [{type}]";
                case ValueType.Color: return $"{color} [{type}]";
            }

            return "nil";
        }

        public static SearchValue ConvertPropertyValue(in SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer: return new SearchValue(Convert.ToDouble(sp.intValue));
                case SerializedPropertyType.Boolean: return new SearchValue(sp.boolValue);
                case SerializedPropertyType.Float: return new SearchValue(sp.floatValue);
                case SerializedPropertyType.String: return new SearchValue(sp.stringValue);
                case SerializedPropertyType.Enum: return new SearchValue(sp.enumNames[sp.enumValueIndex]);
                case SerializedPropertyType.ObjectReference: return new SearchValue(sp.objectReferenceValue?.name);
                case SerializedPropertyType.Bounds: return new SearchValue(sp.boundsValue.size.magnitude);
                case SerializedPropertyType.BoundsInt: return new SearchValue(sp.boundsIntValue.size.magnitude);
                case SerializedPropertyType.Rect: return new SearchValue(sp.rectValue.size.magnitude);
                case SerializedPropertyType.Color: return new SearchValue(sp.colorValue);
                case SerializedPropertyType.Generic: break;
                case SerializedPropertyType.LayerMask: break;
                case SerializedPropertyType.Vector2: break;
                case SerializedPropertyType.Vector3: break;
                case SerializedPropertyType.Vector4: break;
                case SerializedPropertyType.ArraySize: break;
                case SerializedPropertyType.Character: break;
                case SerializedPropertyType.AnimationCurve: break;
                case SerializedPropertyType.Gradient: break;
                case SerializedPropertyType.Quaternion: break;
                case SerializedPropertyType.ExposedReference: break;
                case SerializedPropertyType.FixedBufferSize: break;
                case SerializedPropertyType.Vector2Int: break;
                case SerializedPropertyType.Vector3Int: break;
                case SerializedPropertyType.RectInt: break;
                case SerializedPropertyType.ManagedReference: break;
            }

            if (sp.isArray)
                return new SearchValue(sp.arraySize);

            return SearchValue.invalid;
        }

        internal static bool TryParseRange(in string arg, out PropertyRange range)
        {
            range = default;
            if (arg.Length < 2 || arg[0] != '[' || arg[arg.Length - 1] != ']')
                return false;

            var rangeMatches = s_RangeRx.Matches(arg);
            if (rangeMatches.Count != 1 || rangeMatches[0].Groups.Count != 3)
                return false;

            var rg = rangeMatches[0].Groups;
            if (!Utils.TryParse(rg[1].Value, out double min) || !Utils.TryParse(rg[2].Value, out double max))
                return false;

            range = new PropertyRange(min, max);
            return true;
        }

        public static void SetupEngine<T>(QueryEngine<T> queryEngine)
        {
            queryEngine.AddOperatorHandler(":", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => r.Contains(f)));
            queryEngine.AddOperatorHandler("=", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => r.Contains(f)));
            queryEngine.AddOperatorHandler("!=", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => !r.Contains(f)));
            queryEngine.AddOperatorHandler("<=", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f <= r.max));
            queryEngine.AddOperatorHandler("<", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f < r.min));
            queryEngine.AddOperatorHandler(">", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f > r.max));
            queryEngine.AddOperatorHandler(">=", (SearchValue v, PropertyRange range) => PropertyRangeCompare(v, range, (f, r) => f >= r.min));

            queryEngine.AddOperatorHandler(":", (SearchValue v, double number, StringComparison sc) => PropertyFloatCompare(v, number, (f, r) => Math.Abs(f - r) < double.Epsilon));
            queryEngine.AddOperatorHandler("=", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => Math.Abs(f - r) < double.Epsilon));
            queryEngine.AddOperatorHandler("!=", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => Math.Abs(f - r) >= double.Epsilon));
            queryEngine.AddOperatorHandler("<=", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => f <= r));
            queryEngine.AddOperatorHandler("<", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => f < r));
            queryEngine.AddOperatorHandler(">", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => f > r));
            queryEngine.AddOperatorHandler(">=", (SearchValue v, double number) => PropertyFloatCompare(v, number, (f, r) => f >= r));

            queryEngine.AddOperatorHandler("=", (SearchValue v, bool b) => PropertyBoolCompare(v, b, (f, r) => f == r));
            queryEngine.AddOperatorHandler(":", (SearchValue v, bool b) => PropertyBoolCompare(v, b, (f, r) => f == r));
            queryEngine.AddOperatorHandler("!=", (SearchValue v, bool b) => PropertyBoolCompare(v, b, (f, r) => f != r));

            queryEngine.AddOperatorHandler(":", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => StringContains(f, r, sc)));
            queryEngine.AddOperatorHandler("=", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Equals(f, r, sc)));
            queryEngine.AddOperatorHandler("!=", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => !string.Equals(f, r, sc)));
            queryEngine.AddOperatorHandler("<=", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) <= 0));
            queryEngine.AddOperatorHandler("<", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) < 0));
            queryEngine.AddOperatorHandler(">", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) > 0));
            queryEngine.AddOperatorHandler(">=", (SearchValue v, string s, StringComparison sc) => PropertyStringCompare(v, s, (f, r) => string.Compare(f, r, sc) >= 0));

            queryEngine.AddOperatorHandler(":", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f == r));
            queryEngine.AddOperatorHandler("=", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f == r));
            queryEngine.AddOperatorHandler("!=", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f != r));
            queryEngine.AddOperatorHandler("<=", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f <= r));
            queryEngine.AddOperatorHandler("<", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f < r));
            queryEngine.AddOperatorHandler(">", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f > r));
            queryEngine.AddOperatorHandler(">=", (SearchValue v, SearchColor c) => PropertyColorCompare(v, c, (f, r) => f >= r));

            queryEngine.AddTypeParser(arg =>
            {
                if (TryParseRange(arg, out var range))
                    return new ParseResult<PropertyRange>(true, range);
                return ParseResult<PropertyRange>.none;
            });

            queryEngine.AddTypeParser(s =>
            {
                if (!s.StartsWith("#"))
                    return new ParseResult<SearchColor?>(false, null);
                if (ColorUtility.TryParseHtmlString(s, out var color))
                    return new ParseResult<SearchColor?>(true, new SearchColor(color));
                return new ParseResult<SearchColor?>(false, null);
            });
        }

        private static readonly Regex s_RangeRx = new Regex(@"\[(-?[\d\.]+)[,](-?[\d\.]+)\s*\]");

        private static bool StringContains(string ev, string fv, StringComparison sc)
        {
            if (ev == null || fv == null)
                return false;
            return ev.IndexOf(fv, sc) != -1;
        }

        private static bool PropertyRangeCompare(in SearchValue v, in PropertyRange range, Func<double, PropertyRange, bool> comparer)
        {
            if (v.type != ValueType.Number)
                return false;
            return comparer(v.number, range);
        }

        private static bool PropertyFloatCompare(in SearchValue v, double value, Func<double, double, bool> comparer)
        {
            if (v.type != ValueType.Number)
                return false;
            return comparer(v.number, value);
        }

        private static bool PropertyBoolCompare(in SearchValue v, bool b, Func<bool, bool, bool> comparer)
        {
            if (v.type != ValueType.Bool)
                return false;
            return comparer(v.number == 1d, b);
        }

        private static bool PropertyStringCompare(in SearchValue v, string s, Func<string, string, bool> comparer)
        {
            if (v.type == ValueType.Bool)
            {
                if (v.boolean && string.Equals(s, "on", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!v.boolean && string.Equals(s, "off", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (v.type != ValueType.Text || string.IsNullOrEmpty(v.text))
                return false;
            return comparer(v.text, s);
        }

        private static bool PropertyColorCompare(in SearchValue v, SearchColor value, Func<SearchColor, SearchColor, bool> comparer)
        {
            if (v.type != ValueType.Color)
                return false;
            return comparer(v.color, value);
        }

        #if USE_PROPERTY_DATABASE
        [PropertyDatabaseSerializer(typeof(SearchValue))]
        internal static PropertyDatabaseRecordValue SearchValueSerializer(PropertyDatabaseSerializationArgs args)
        {
            var gop = (SearchValue)args.value;
            switch (gop.type)
            {
                case ValueType.Nil:
                    return new PropertyDatabaseRecordValue((byte)PropertyDatabaseType.GameObjectProperty, (byte)gop.type);
                case ValueType.Bool:
                case ValueType.Number:
                    return new PropertyDatabaseRecordValue((byte)PropertyDatabaseType.GameObjectProperty, (byte)gop.type, BitConverter.DoubleToInt64Bits(gop.number));
                case ValueType.Text:
                    var symbol = args.stringTableView.ToSymbol(gop.text);
                    return new PropertyDatabaseRecordValue((byte)PropertyDatabaseType.GameObjectProperty, (byte)gop.type, (int)symbol);
                case ValueType.Color:
                    return new PropertyDatabaseRecordValue((byte)PropertyDatabaseType.GameObjectProperty, (byte)gop.type, (byte)gop.color.r, (byte)gop.color.g, (byte)gop.color.b, (byte)gop.color.a);
            }

            return PropertyDatabaseRecordValue.invalid;
        }

        [PropertyDatabaseDeserializer(PropertyDatabaseType.GameObjectProperty)]
        internal static object SearchValueDeserializer(PropertyDatabaseDeserializationArgs args)
        {
            var gopType = (ValueType)args.value[0];
            switch (gopType)
            {
                case ValueType.Nil:
                    return new SearchValue();
                case ValueType.Bool:
                    return new SearchValue(BitConverter.Int64BitsToDouble(args.value.int64_1) == 1d);
                case ValueType.Number:
                    return new SearchValue(BitConverter.Int64BitsToDouble(args.value.int64_1));
                case ValueType.Text:
                    var symbol = args.value.int32_1;
                    var str = args.stringTableView.GetString(symbol);
                    return new SearchValue(str);
                case ValueType.Color:
                    return new SearchValue(new SearchColor(args.value[1], args.value[2], args.value[3], args.value[4]));
            }

            throw new Exception("Failed to deserialize game object property");
        }

        #endif
    }

    class SearchItemQueryEngine : QueryEngine<SearchItem>
    {
        static Regex PropertyFilterRx = new Regex(@"[\@\$]([#\w\d\.]+)");

        SearchExpressionContext m_Context;

        public SearchItemQueryEngine()
        {
            Setup();
        }

        public IEnumerable<SearchItem> Where(SearchExpressionContext context, IEnumerable<SearchItem> dataSet, string queryStr)
        {
            m_Context = context;
            var query = Parse(queryStr, true);
            if (query.errors.Count != 0)
            {
                foreach (var queryError in query.errors)
                {
                    Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null, $"Error parsing input at {queryError.index}: {queryError.reason}");
                }

                var errorStr = string.Join("\n", query.errors.Select(err => $"Error parsing input at {err.index}: {err.reason}"));
                context.ThrowError(errorStr);
            }

            foreach (var item in dataSet)
            {
                if (item != null)
                {
                    if (query.Test(item))
                        yield return item;
                }
                else
                    yield return null;
            }
            m_Context = default;
        }

        public IEnumerable<SearchItem> WhereMainThread(SearchExpressionContext context, IEnumerable<SearchItem> dataSet, string queryStr)
        {
            m_Context = context;
            var query = Parse(queryStr, true);
            if (query.errors.Count != 0)
            {
                foreach (var queryError in query.errors)
                {
                    Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null, $"Error parsing input at {queryError.index}: {queryError.reason}");
                }

                var errorStr = string.Join("\n", query.errors.Select(err => $"Error parsing input at {err.index}: {err.reason}"));
                context.ThrowError(errorStr);
            }

            var results =  TaskEvaluatorManager.EvaluateMainThread(dataSet, item =>
            {
                if (query.Test(item))
                    return item;
                return null;
            }, 25);
            m_Context = default;
            return results;
        }

        private void Setup()
        {
            AddFilter(PropertyFilterRx, GetValue);
            AddFilter("p", GetValue, s => s, StringComparison.OrdinalIgnoreCase);

            SearchValue.SetupEngine(this);

            SetSearchDataCallback(GetSearchableData, StringComparison.OrdinalIgnoreCase);
        }

        IEnumerable<string> GetSearchableData(SearchItem item)
        {
            yield return item.value.ToString();
            yield return item.id;
            if (item.label != null)
                yield return item.label;
        }

        SearchValue GetValue(SearchItem item, string selector)
        {
            var v = SelectorManager.SelectValue(item, m_Context.search, selector);
            return new SearchValue(v);
        }
    }
}
