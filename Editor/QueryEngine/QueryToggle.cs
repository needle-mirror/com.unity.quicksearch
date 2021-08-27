namespace UnityEditor.Search
{
    readonly struct QueryToggle
    {
        public readonly StringView rawText;
        public readonly StringView value;

        public QueryToggle(in StringView rawText, in StringView value)
        {
            this.rawText = rawText;
            this.value = value;
        }
    }
}
