#if USE_GRAPH_VIEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Search
{
    class GraphViewerQueryHandler : IQueryHandler<object, object>
    {
        public IEnumerable<object> Eval(object payload)
        {
            return new object[] {};
        }

        bool IQueryHandler<object, object>.Eval(object element)
        {
            return false;
        }
    }

    class GraphViewerQueryHandlerFactory : IQueryHandlerFactory<object, GraphViewerQueryHandler, IEnumerable<object>>
    {
        public GraphViewerQueryHandler Create(QueryGraph graph, ICollection<QueryError> errors)
        {
            return new GraphViewerQueryHandler();
        }
    }

    struct LayoutPosition
    {
        public int level;
        public int index;

        public LayoutPosition(int level, int index)
        {
            this.level = level;
            this.index = index;
        }
    }

    enum GraphType
    {
        Evaluation,
        Query
    }

    class QueryGraphViewWindow : GraphViewEditorWindow
    {
        QueryEngine m_QueryEngine;
        GraphViewerQueryHandlerFactory m_QueryHandlerFactory;

        [SerializeField] public string searchQuery;
        [SerializeField] public GraphType graphType = GraphType.Evaluation;

        public GraphView graphView { get; private set; }

        RadioButtonGroup m_GraphTypeButtonGroup;
        TextField m_SearchQueryTextField;

        public override IEnumerable<GraphView> graphViews
        {
            get { yield return graphView; }
        }

        [MenuItem("Window/Search/Graph Viewer")]
        public static void ShowWindow()
        {
            GetWindow<QueryGraphViewWindow>();
        }

        public virtual void OnEnable()
        {
            SetupQueryEngine();

            graphView = new QueryGraphView(this) { name = "QueryGraph", viewDataKey = "QueryGraph" };
            graphView.style.flexGrow = 1;

            rootVisualElement.style.flexDirection = FlexDirection.Column;

            SetupQueryInputBox(rootVisualElement);
            SetupGraphToggleButton(rootVisualElement);

            rootVisualElement.Add(graphView);

            titleContent.text = "Query Graph View";
        }

        void SetupQueryInputBox(VisualElement root)
        {
            var queryInputRow = new VisualElement();
            queryInputRow.style.flexDirection = FlexDirection.Row;
            root.Add(queryInputRow);

            m_SearchQueryTextField = new TextField("Query");
            m_SearchQueryTextField.RegisterValueChangedCallback(evt =>
            {
                searchQuery = evt.newValue;
                UpdateGraphView();
            });
            m_SearchQueryTextField.style.flexGrow = 1;
            queryInputRow.Add(m_SearchQueryTextField);

            var optimizeButton = new Button(() =>
            {
                var queryGraphView = graphView as QueryGraphView;
                var queryGraph = queryGraphView?.graph;
                queryGraph?.Optimize(true, true);
                queryGraphView?.Reload();
            });
            optimizeButton.text = "Optimize";
            queryInputRow.Add(optimizeButton);
        }

        void SetupGraphToggleButton(VisualElement root)
        {
            var graphTypeNames = Enum.GetNames(typeof(GraphType)).ToList();
            m_GraphTypeButtonGroup = new RadioButtonGroup("Graphs", graphTypeNames);
            m_GraphTypeButtonGroup.RegisterValueChangedCallback(evt =>
            {
                var newValueIndex = evt.newValue;
                if (newValueIndex < 0 || newValueIndex >= graphTypeNames.Count)
                    return;
                var selectedName = graphTypeNames[newValueIndex];
                if (!Enum.TryParse(selectedName, out GraphType newValue))
                    return;
                graphType = newValue;
                UpdateGraphView();
            });
            root.Add(m_GraphTypeButtonGroup);

            UpdateGraphType();
        }

        void SetupQueryEngine()
        {
            m_QueryEngine = new QueryEngine(false);
            m_QueryHandlerFactory = new GraphViewerQueryHandlerFactory();
        }

        public void SetView(in string searchQuery, GraphType type)
        {
            graphType = type;
            if (m_GraphTypeButtonGroup != null)
                UpdateGraphType();
            if (m_SearchQueryTextField != null)
                m_SearchQueryTextField.value = searchQuery;
            UpdateGraphView();
        }

        void UpdateGraphView()
        {
            var query = m_QueryEngine.Parse(searchQuery, m_QueryHandlerFactory);
            if (!query.valid && !string.IsNullOrEmpty(searchQuery))
            {
                foreach (var error in query.errors)
                {
                    Debug.LogWarning(error.reason);
                }
                return;
            }

            var gv = (QueryGraphView)graphView;
            switch (graphType)
            {
                case GraphType.Evaluation:
                    gv.Reload(query.evaluationGraph);
                    break;
                case GraphType.Query:
                    gv.Reload(query.queryGraph);
                    break;
            }
        }

        void UpdateGraphType()
        {
            var currentGraphTypeName = graphType.ToString();
            var graphTypeNames = Enum.GetNames(typeof(GraphType)).ToList();
            m_GraphTypeButtonGroup.value = graphTypeNames.FindIndex(graphTypeName => graphTypeName == currentGraphTypeName);
        }
    }

    class QueryGraphView : GraphView
    {
        QueryGraphViewWindow m_GraphViewWindow;
        QueryGraph m_Graph;
        Dictionary<IQueryNode, Node> m_QueryNodesToViewNodes;

        const float k_WidthBetweenNodes = 1;
        const float k_HeightBetweenNodes = 5;

        public QueryGraph graph => m_Graph;

        public QueryGraphViewWindow window => m_GraphViewWindow;

        public override bool supportsWindowedBlackboard => true;

        public QueryGraphView(QueryGraphViewWindow graphViewWindow)
        {
            m_GraphViewWindow = graphViewWindow;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            focusable = true;

            Insert(0, new GridBackground());

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new FreehandSelector());
        }

        public void Reload()
        {
            Reload(m_Graph);
        }

        public void Reload(QueryGraph graph)
        {
            ClearContent();
            m_Graph = graph;

            if (graph?.root == null)
                return;

            var nodesToProcess = new Queue<IQueryNode>();
            nodesToProcess.Enqueue(graph.root);

            m_QueryNodesToViewNodes = new Dictionary<IQueryNode, Node>();

            while (nodesToProcess.Count > 0)
            {
                var currentNode = nodesToProcess.Dequeue();
                var viewNode = AddNode(currentNode, m_QueryNodesToViewNodes);
                m_QueryNodesToViewNodes.Add(currentNode, viewNode);

                if (currentNode.leaf || currentNode.children == null)
                    continue;

                foreach (var child in currentNode.children)
                {
                    nodesToProcess.Enqueue(child);
                }
            }

            EditorApplication.delayCall += DoLayouting;
        }

        Node AddNode(IQueryNode node, Dictionary<IQueryNode, Node> queryNodesToViewNodes)
        {
            var visualElementNode = new Node();
            switch (node.type)
            {
                case QueryNodeType.And:
                case QueryNodeType.Or:
                case QueryNodeType.Not:
                case QueryNodeType.Intersection:
                case QueryNodeType.Union:
                case QueryNodeType.Where:
                case QueryNodeType.Group:
                    visualElementNode.title = node.type.ToString();
                    break;
                case QueryNodeType.Search:
                case QueryNodeType.Filter:
                case QueryNodeType.NestedQuery:
                case QueryNodeType.Aggregator:
                case QueryNodeType.Comment:
                case QueryNodeType.Toggle:
                    visualElementNode.title = node.identifier;
                    break;
                case QueryNodeType.FilterIn:
                    var inFilterNode = node as InFilterNode;
                    var paramString = string.IsNullOrEmpty(inFilterNode.paramValue) ? "" : $"({inFilterNode.paramValue})";
                    visualElementNode.title = $"{inFilterNode.filter.token}{paramString}{inFilterNode.op.token} In";
                    break;
            }
            visualElementNode.expanded = false;

            AddElement(visualElementNode);

            if (node.parent != null)
            {
                var inputPort = visualElementNode.InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
                visualElementNode.inputContainer.Add(inputPort);

                var parentViewNode = queryNodesToViewNodes[node.parent];
                var parentOutputPort = parentViewNode.outputContainer[0] as Port;
                var edge = inputPort.ConnectTo(parentOutputPort);
                parentViewNode.RefreshPorts();
                AddElement(edge);
            }

            if (!node.leaf)
            {
                var outputPort = visualElementNode.InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
                visualElementNode.outputContainer.Add(outputPort);
            }

            visualElementNode.RefreshPorts();

            return visualElementNode;
        }

        void ClearContent()
        {
            m_Graph = null;
            var edgesToRemove = this.Query<Edge>().ToList();
            foreach (var edge in edgesToRemove)
            {
                RemoveElement(edge);
            }

            var nodesToRemove = this.Query<Node>().ToList();
            foreach (var node in nodesToRemove)
            {
                RemoveElement(node);
            }
        }

        void DoLayouting()
        {
            if (m_Graph == null)
                return;
            if (m_QueryNodesToViewNodes.Values.Any(node => float.IsNaN(node.GetPosition().width)))
            {
                EditorApplication.delayCall += DoLayouting;
                return;
            }
            LayoutGraphNodes(m_Graph, m_QueryNodesToViewNodes);
            FrameAll();
        }

        void LayoutGraphNodes(QueryGraph graph, Dictionary<IQueryNode, Node> queryNodesToViewNodes)
        {
            var levelIndexByNode = new Dictionary<IQueryNode, LayoutPosition>();
            var nodesByLevel = new Dictionary<int, List<IQueryNode>>();
            var nodesToProcess = new Queue<IQueryNode>();
            nodesToProcess.Enqueue(graph.root);

            while (nodesToProcess.Count > 0)
            {
                int currentLevel;
                int currentIndex;
                var currentNode = nodesToProcess.Dequeue();
                if (currentNode.parent == null)
                {
                    currentLevel = 0;
                    currentIndex = 0;
                }
                else
                {
                    var parentLevel = levelIndexByNode[currentNode.parent].level;
                    var parentIndex = levelIndexByNode[currentNode.parent].index;
                    currentLevel = parentLevel + 1;
                    currentIndex = parentIndex * 2 + (currentNode.parent.children[0] == currentNode ? 0 : 1);
                }
                levelIndexByNode.Add(currentNode, new LayoutPosition(currentLevel, currentIndex));

                if (!nodesByLevel.ContainsKey(currentLevel))
                {
                    nodesByLevel.Add(currentLevel, new List<IQueryNode>());
                }
                nodesByLevel[currentLevel].Add(currentNode);

                if (currentNode.leaf || currentNode.children == null)
                    continue;

                foreach (var child in currentNode.children)
                {
                    nodesToProcess.Enqueue(child);
                }
            }

            var maxLevel = nodesByLevel.Keys.Max();
            var maxNumber = 1 << maxLevel;
            var numberOfSpaces = maxNumber - 1;
            var nodeMaxWidth = queryNodesToViewNodes.Values.Select(node => node.GetPosition().size.x).Max();
            var nodeMaxHeight = queryNodesToViewNodes.Values.Select(node => node.GetPosition().size.y).Max();

            var totalLength = maxNumber * nodeMaxWidth + numberOfSpaces * k_WidthBetweenNodes;
            var totalHeight = (maxLevel + 1) * nodeMaxHeight + maxLevel * k_HeightBetweenNodes;
            var positionXStart = 0.0f - totalLength / 2.0f;
            var positionYStart = 0.0f + totalHeight / 2.0f;

            Vector2 GetNodePosition(int level, int index)
            {
                if (level == maxLevel)
                {
                    return new Vector2(positionXStart + index * (nodeMaxWidth + k_WidthBetweenNodes), positionYStart);
                }

                var posChildLeft = GetNodePosition(level + 1, index * 2);
                var posChildRight = GetNodePosition(level + 1, index * 2 + 1);
                return new Vector2((posChildLeft.x + posChildRight.x) / 2.0f, posChildLeft.y - k_HeightBetweenNodes - nodeMaxHeight);
            }

            for (var level = maxLevel; level >= 0; --level)
            {
                foreach (var queryNode in nodesByLevel[level])
                {
                    var viewNode = queryNodesToViewNodes[queryNode];
                    var oldRect = viewNode.GetPosition();
                    var newPos = GetNodePosition(level, levelIndexByNode[queryNode].index);
                    var newRect = new Rect(newPos, oldRect.size);
                    viewNode.SetPosition(newRect);
                }
            }
        }
    }
}
#endif
