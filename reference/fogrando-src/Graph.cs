using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SoulsIds;
using YamlDotNet.Serialization;

namespace FogMod;

public class Graph
{
	public class WarpPoint
	{
		public int ID { get; set; }

		public string Map { get; set; }

		public Vector3? Position { get; set; }

		public int Action { get; set; }

		public int Cutscene { get; set; }

		public int WarpFlag { get; set; }

		public int SitFlag { get; set; }

		public bool ToFront { get; set; }

		public float Height { get; set; }

		public int Player { get; set; }

		public int Region { get; set; }

		public int Retry { get; set; }

		public int OtherSide { get; set; }
	}

	public class Connection
	{
		public string A { get; set; }

		public string B { get; set; }

		public Connection(string a, string b)
		{
			A = ((a.CompareTo(b) < 0) ? a : b);
			B = ((a.CompareTo(b) < 0) ? b : a);
		}

		public override bool Equals(object obj)
		{
			if (obj is Connection o)
			{
				return Equals(o);
			}
			return false;
		}

		public bool Equals(Connection o)
		{
			if (A == o.A)
			{
				return B == o.B;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return A.GetHashCode() ^ B.GetHashCode();
		}

		public override string ToString()
		{
			return $"({A}, {B})";
		}
	}

	public class Node
	{
		public List<string> Items = new List<string>();

		public List<Edge> To = new List<Edge>();

		public List<Edge> From = new List<Edge>();

		public string Area { get; set; }

		public int Cost { get; set; }

		public string ScalingBase { get; set; }
	}

	public class Edge
	{
		private Edge pair;

		private Edge link;

		public EdgeType Type { get; set; }

		public string From { get; set; }

		public string To { get; set; }

		public AnnotationData.Expr Expr { get; set; }

		public AnnotationData.Expr LinkedExpr { get; set; }

		public bool IsFixed { get; set; }

		public bool IsWorld => Side.IsWorld;

		public string Name { get; set; }

		public AnnotationData.Side Side { get; set; }

		public string Text { get; set; }

		public Edge Pair
		{
			get
			{
				return pair;
			}
			set
			{
				if (value != null && Type == value.Type)
				{
					throw new Exception($"Cannot pair {this} and {value}");
				}
				pair = value;
			}
		}

		public Edge Link
		{
			get
			{
				return link;
			}
			set
			{
				if (value != null && Type == value.Type)
				{
					throw new Exception($"Cannot link {this} and {value}");
				}
				link = value;
			}
		}

		public Edge FixedLink { get; set; }

		public override string ToString()
		{
			return $"{(IsFixed ? "&" : "")}{((Side != null && Side.IsCore) ? "+" : "")}Edge[Name={Name}, {((Type == EdgeType.Exit) ? "*" : "")}From={From}, {((Type == EdgeType.Exit) ? "" : "*")}To={To}{((Expr == null) ? "" : $", Expr={Expr}")}]";
		}
	}

	public enum EdgeType
	{
		Unknown,
		Exit,
		Entrance
	}

	public class Segment
	{
		public string Name { get; set; }

		public string Text { get; set; }

		public string EntranceText { get; set; }

		public string ExitText { get; set; }

		public Edge Entrance { get; set; }

		public List<Edge> Exits { get; set; }

		public NexusSegment Gate { get; set; }

		public string Group { get; set; }

		public override string ToString()
		{
			return $"Segment[{Name}] {Entrance} -> {string.Join(" ", Exits)}";
		}
	}

	public class SegmentGroup
	{
		public List<Segment> Segments { get; set; }
	}

	public class SegmentConfig
	{
		public List<Nexus> Nexuses { get; set; }
	}

	public class Nexus
	{
		public string Areas { get; set; }

		public string Type { get; set; }

		public List<NexusSegment> Segments { get; set; }
	}

	public enum SegmentType
	{
		None,
		MajorBoss,
		MinorBoss,
		OverworldBoss,
		DungeonTraversal,
		MinidungeonTraversal,
		OverworldTraversal
	}

	public class NexusSegment : AnnotationData.Taggable
	{
		public string ID { get; set; }

		public string DebugText { get; set; }

		public string EntranceText { get; set; }

		public string ExitText { get; set; }

		public SegmentType Type { get; set; }

		public string Group { get; set; }

		[YamlIgnore]
		public List<int> BeforeWarpFlags { get; set; }

		public string Status { get; set; }

		[YamlIgnore]
		public Edge Entrance { get; set; }

		[YamlIgnore]
		public Edge Exit { get; set; }

		public bool Matches(AnnotationData.SegmentAmount amt, SegmentType typeFilter = SegmentType.None, bool debug = false)
		{
			bool flag = true;
			if (amt.Types != null)
			{
				flag &= amt.Types.Contains(Type);
				if (debug)
				{
					Console.WriteLine($"Matching types: {Type} in {string.Join(",", amt.Types)}, {flag}");
				}
			}
			if (typeFilter != SegmentType.None)
			{
				flag &= Type == typeFilter;
				if (debug)
				{
					Console.WriteLine($"Matching type filter: {Type} == {typeFilter}, {flag}");
				}
			}
			if (amt.TagSet != null)
			{
				flag &= amt.TagSet.IsSubsetOf(TagList);
				if (debug)
				{
					Console.WriteLine($"Matching tags: {base.Tags} in {string.Join(",", amt.TagSet)}, {flag}");
				}
			}
			if (debug)
			{
				Console.WriteLine($"Matching: {flag}");
			}
			return flag;
		}
	}

	public List<int> UnlockTiers = new List<int> { 1, 4, 7, 11, 15, 21, 27, 29, 32 };

	public static readonly List<SegmentType> SegmentTypes = new List<SegmentType>
	{
		SegmentType.MajorBoss,
		SegmentType.MinorBoss,
		SegmentType.OverworldBoss,
		SegmentType.DungeonTraversal,
		SegmentType.MinidungeonTraversal,
		SegmentType.OverworldTraversal
	};

	public Dictionary<string, AnnotationData.Area> Areas { get; set; }

	public Dictionary<string, AnnotationData.Entrance> EntranceIds { get; set; }

	public Dictionary<string, List<string>> ItemAreas { get; set; }

	public Dictionary<string, AnnotationData.Expr> ConfigExprs { get; set; }

	public HashSet<int> BossLots { get; set; } = new HashSet<int>();

	public Dictionary<string, Node> Nodes { get; set; }

	public AnnotationData.CustomStart Start { get; set; }

	public HashSet<(string, string)> Ignore { get; set; }

	public Dictionary<string, (float, float)> AreaRatios { get; set; }

	public Dictionary<string, int> AreaTiers { get; set; }

	public List<Segment> Segments { get; set; }

	public AnnotationData.AreaMode ExcludeMode { get; set; }

	public Edge AddEdge(AnnotationData.Side side, AnnotationData.Entrance e, bool isExit)
	{
		string name = e?.FullName;
		string text = ((!string.IsNullOrEmpty(side.Text)) ? side.Text : ((e != null) ? e.Text : (side.HasTag("hard") ? "hard skip" : "in map")));
		bool isFixed = e?.IsFixed ?? true;
		Edge edge = new Edge
		{
			Expr = side.Expr,
			Name = name,
			Text = text,
			IsFixed = isFixed,
			Side = side
		};
		if (isExit)
		{
			edge.Type = EdgeType.Exit;
			edge.From = side.Area;
			Nodes[side.Area].To.Add(edge);
		}
		else
		{
			edge.Type = EdgeType.Entrance;
			edge.To = side.Area;
			Nodes[side.Area].From.Add(edge);
		}
		return edge;
	}

	public (Edge, Edge) AddPairedEdges(AnnotationData.Side side, AnnotationData.Entrance e)
	{
		Edge edge = AddEdge(side, e, isExit: true);
		Edge edge2 = (edge.Pair = AddEdge(side, e, isExit: false));
		edge2.Pair = edge;
		return (edge, edge2);
	}

	public (Edge, Edge) AddNodeX(AnnotationData.Side side, AnnotationData.Entrance e, bool from, bool to)
	{
		string name = e?.FullName;
		string text = ((!string.IsNullOrEmpty(side.Text)) ? side.Text : ((e != null) ? e.Text : (side.HasTag("hard") ? "hard skip" : "in map")));
		bool isFixed = e?.IsFixed ?? true;
		Edge edge = (from ? new Edge
		{
			Expr = side.Expr,
			From = side.Area,
			Name = name,
			Text = text,
			IsFixed = isFixed,
			Side = side,
			Type = EdgeType.Exit
		} : null);
		Edge edge2 = (to ? new Edge
		{
			Expr = side.Expr,
			To = side.Area,
			Name = name,
			Text = text,
			IsFixed = isFixed,
			Side = side,
			Type = EdgeType.Entrance
		} : null);
		if (edge2 != null)
		{
			edge2.Pair = edge;
			Nodes[side.Area].From.Add(edge2);
		}
		if (edge != null)
		{
			edge.Pair = edge2;
			Nodes[side.Area].To.Add(edge);
		}
		return (edge, edge2);
	}

	public Edge DuplicateEntrance(Edge entrance)
	{
		if (entrance.Type != EdgeType.Entrance)
		{
			throw new Exception($"Invalid {entrance}");
		}
		AnnotationData.Side side = entrance.Side;
		Edge edge = new Edge
		{
			Expr = side.Expr,
			To = side.Area,
			Name = entrance.Name,
			Text = entrance.Text,
			IsFixed = entrance.IsFixed,
			Side = side,
			Type = EdgeType.Entrance
		};
		Nodes[side.Area].From.Add(edge);
		return edge;
	}

	public void Connect(Edge exit, Edge entrance, bool ignorePair = false)
	{
		if (exit.To != null || entrance.From != null || exit.Link != null || entrance.Link != null)
		{
			throw new Exception($"Already matched: {exit} (needs no To) --> {entrance} (needs no From) with links {exit.Link}, {entrance.Link}.");
		}
		exit.To = entrance.To;
		entrance.From = exit.From;
		entrance.Link = exit;
		exit.Link = entrance;
		AnnotationData.Expr linkedExpr = (exit.LinkedExpr = combineExprs(entrance.Expr, exit.Expr));
		entrance.LinkedExpr = linkedExpr;
		if (exit == entrance.Pair || ignorePair)
		{
			return;
		}
		if (exit.Pair != null)
		{
			if (exit.Pair.From != null || exit.Pair.Link != null)
			{
				throw new Exception("Already matched pair");
			}
			exit.Pair.From = exit.To;
		}
		if (entrance.Pair != null)
		{
			if (entrance.Pair.To != null || entrance.Pair.Link != null)
			{
				throw new Exception("Already matched pair");
			}
			entrance.Pair.To = entrance.From;
		}
		if (exit.Pair != null && entrance.Pair != null)
		{
			exit.Pair.Link = entrance.Pair;
			entrance.Pair.Link = exit.Pair;
			Edge pair = exit.Pair;
			linkedExpr = (entrance.Pair.LinkedExpr = combineExprs(entrance.Pair.Expr, exit.Pair.Expr));
			pair.LinkedExpr = linkedExpr;
		}
		static AnnotationData.Expr combineExprs(AnnotationData.Expr entranceExpr, AnnotationData.Expr exitExpr)
		{
			if (entranceExpr == null)
			{
				return exitExpr;
			}
			if (exitExpr == null)
			{
				return entranceExpr;
			}
			if (!(exitExpr.ToString() == entranceExpr.ToString()))
			{
				return new AnnotationData.Expr(new List<AnnotationData.Expr> { exitExpr, entranceExpr }).Simplify();
			}
			return exitExpr;
		}
	}

	public void Disconnect(Edge exit, bool ignorePair = false)
	{
		Edge link = exit.Link;
		if (link == null)
		{
			throw new Exception($"Can't disconnect {exit}{(ignorePair ? " as pair" : "")}");
		}
		exit.Link = null;
		exit.To = null;
		link.Link = null;
		link.From = null;
		AnnotationData.Expr linkedExpr = (exit.LinkedExpr = null);
		link.LinkedExpr = linkedExpr;
		if (!ignorePair && exit.Pair != null && link.Pair != null && exit.Pair != link)
		{
			Disconnect(link.Pair, ignorePair: true);
		}
	}

	public void SwapConnectedEdges(Edge oldExitEdge, Edge newEntranceEdge)
	{
		Edge link = newEntranceEdge.Link;
		Edge link2 = oldExitEdge.Link;
		Disconnect(link);
		Disconnect(oldExitEdge);
		if (newEntranceEdge == link.Pair && link2 == oldExitEdge.Pair)
		{
			Connect(oldExitEdge, newEntranceEdge);
		}
		else if (newEntranceEdge == link.Pair)
		{
			if (link2.Pair != null)
			{
				Connect(link2.Pair, link2);
				Connect(oldExitEdge, newEntranceEdge);
				return;
			}
			if (oldExitEdge.Pair == null)
			{
				throw new Exception($"Bad seed: Can't find edge to self-link to reach {newEntranceEdge}");
			}
			Connect(oldExitEdge, oldExitEdge.Pair);
			Connect(link, link2);
		}
		else if (link2 == oldExitEdge.Pair)
		{
			if (newEntranceEdge.Pair != null)
			{
				Connect(newEntranceEdge.Pair, newEntranceEdge);
				Connect(link, link2);
				return;
			}
			if (link.Pair == null)
			{
				throw new Exception($"Bad seed: Can't find edge to self-link to reach {newEntranceEdge}");
			}
			Connect(link, link.Pair);
			Connect(oldExitEdge, newEntranceEdge);
		}
		else
		{
			Connect(oldExitEdge, newEntranceEdge);
			Connect(link, link2);
		}
	}

	public void SwapConnectedAreas(string name1, string name2)
	{
		Node node = Nodes[name1];
		Node node2 = Nodes[name2];
		for (int i = 0; i <= 1; i++)
		{
			bool unpaired = i == 0;
			List<Edge> list = node.From.Where((Edge e) => !e.IsFixed && e.Pair == null == unpaired).ToList();
			List<Edge> list2 = node2.From.Where((Edge e) => !e.IsFixed && e.Pair == null == unpaired).ToList();
			list2.Reverse();
			for (int num = 0; num < Math.Min(list.Count, list2.Count); num++)
			{
				Edge edge = list[num];
				Edge newEntranceEdge = list2[num];
				SwapConnectedEdges(edge.Link, newEntranceEdge);
			}
		}
	}

	public bool MakeCore(string area, bool dryrun = false)
	{
		List<string> inward = new List<string>();
		addArea(area);
		List<Edge> list = (from e in inward.SelectMany((string a) => Nodes[a].From)
			where !e.IsWorld
			select e).ToList();
		if (list.Count == 0)
		{
			throw new Exception("Bad item placements. Can't make required area accessible in routing (" + string.Join(", ", inward) + ")");
		}
		if (list.Any((Edge e) => e.Side.IsCore))
		{
			return false;
		}
		Edge edge = list.Find((Edge e) => e.Pair != null);
		Edge select = edge;
		if (select == null)
		{
			select = list[0];
		}
		if (select.Side.Silo != null)
		{
			AnnotationData.Side? side = EntranceIds[select.Name].Sides().Find((AnnotationData.Side s) => s.Silo != select.Side.Silo);
			select.Side.Silo = null;
			side.Silo = null;
		}
		if (dryrun)
		{
			Console.WriteLine("Found key item in unselected area " + Areas[area].Text);
			return true;
		}
		Console.WriteLine($"Found key item in unselected area {Areas[area].Text}, so routing in an entrance ({select.Side.Text})");
		select.Side.IsCore = true;
		return true;
		void addArea(string a)
		{
			if (inward.Contains(a))
			{
				return;
			}
			inward.Add(a);
			foreach (Edge item in Nodes[a].From.Where((Edge e) => e.IsWorld))
			{
				addArea(item.From);
			}
		}
	}

	public (HashSet<string>, Dictionary<string, string>) MarkCoreAreas()
	{
		Dictionary<string, bool> core = new Dictionary<string, bool>();
		foreach (Node value2 in Nodes.Values)
		{
			if (value2.To.Any((Edge e) => e.Side.IsCore) || value2.From.Any((Edge e) => e.Side.IsCore))
			{
				core[value2.Area] = true;
			}
		}
		foreach (Node value3 in Nodes.Values)
		{
			calcCore(value3);
		}
		HashSet<string> hashSet = new HashSet<string>(from e in core
			where e.Value
			select e.Key);
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		foreach (Node value4 in Nodes.Values)
		{
			if (!hashSet.Contains(value4.Area))
			{
				continue;
			}
			Areas[value4.Area].IsCore = true;
			foreach (Edge item in value4.From)
			{
				if (hashSet.Contains(item.From) || !item.IsWorld)
				{
					continue;
				}
				dictionary[item.From] = item.To;
				foreach (Edge item2 in Nodes[item.From].To)
				{
					if (!hashSet.Contains(item2.To))
					{
						item2.Side.IsPseudoCore = true;
					}
				}
			}
		}
		foreach (Node value5 in Nodes.Values)
		{
			if (!Areas[value5.Area].HasTag("overworld"))
			{
				continue;
			}
			foreach (Edge item3 in value5.From)
			{
				if (item3.IsWorld && !Areas[item3.From].HasTag("overworld"))
				{
					Areas[item3.From].AddTag("overworld_adjacent");
				}
			}
		}
		return (hashSet, dictionary);
		bool calcCore(Node node)
		{
			if (core.TryGetValue(node.Area, out var value))
			{
				return value;
			}
			core[node.Area] = false;
			if (node.From.Any((Edge e) => e.IsWorld && !e.Side.HasTag("openonly") && calcCore(Nodes[e.From])))
			{
				value = (core[node.Area] = true);
			}
			return value;
		}
	}

	public bool IsMajorScalingBoss(AnnotationData.Area area)
	{
		if (area.IsBoss && area.IsCore && !area.IsExcluded && !area.HasTag("final") && !area.HasTag("optional") && !area.HasTag("minidungeon"))
		{
			return !area.HasTag("minor");
		}
		return false;
	}

	public HashSet<string> GetWorldConnections(string start)
	{
		HashSet<string> ret = new HashSet<string>();
		calc(start);
		return ret;
		void calc(string area)
		{
			if (ret.Contains(area))
			{
				return;
			}
			ret.Add(area);
			foreach (Edge item in Nodes[area].To)
			{
				if (item.Name == null)
				{
					calc(item.To);
				}
			}
		}
	}

	public void TagOpenStart()
	{
		Dictionary<string, int> directExits = new Dictionary<string, int>();
		foreach (Node value2 in Nodes.Values)
		{
			if (Areas[value2.Area].DefeatFlag <= 0)
			{
				directExits[value2.Area] = value2.To.Count((Edge e) => !e.IsWorld && isSimpleExit(e));
			}
		}
		foreach (Node value3 in Nodes.Values)
		{
			if (Areas[value3.Area].DefeatFlag == 0)
			{
				HashSet<string> hashSet = new HashSet<string>();
				visit(value3.Area, hashSet);
				if (!hashSet.All((string a) => Areas[a].HasTag("trivial")) && hashSet.Sum((string a) => directExits.TryGetValue(a, out var value) ? value : 0) >= 2)
				{
					continue;
				}
			}
			Areas[value3.Area].AddTag("avoidstart");
		}
		foreach (AnnotationData.Entrance value4 in EntranceIds.Values)
		{
			if (value4.HasTag("unused"))
			{
				continue;
			}
			foreach (AnnotationData.Side item in value4.Sides())
			{
				if (Areas[item.Area].HasTag("avoidstart") && !item.HasTag("avoidstart"))
				{
					item.AddTag("avoidstart");
				}
			}
		}
		static bool isSimpleExit(Edge e)
		{
			if (e.Expr != null)
			{
				return e.Expr.ToString() == e.From;
			}
			return true;
		}
		void visit(string area, HashSet<string> visited)
		{
			visited.Add(area);
			foreach (Edge item2 in Nodes[area].To)
			{
				if (item2.IsWorld && isSimpleExit(item2) && !visited.Contains(item2.To))
				{
					visit(item2.To, visited);
				}
			}
		}
	}

	public void Construct(RandomizerOptions opt, AnnotationData ann)
	{
		if (opt["dlconly"])
		{
			ExcludeMode = AnnotationData.AreaMode.Base;
		}
		else if (!opt["dlc"])
		{
			ExcludeMode = AnnotationData.AreaMode.DLC;
		}
		Areas = ann.Areas.ToDictionary((AnnotationData.Area a) => a.Name, (AnnotationData.Area a) => a);
		ItemAreas = ann.KeyItems.ToDictionary((AnnotationData.Item item6) => item6.Name, (AnnotationData.Item item6) => new List<string>());
		ConfigExprs = ((ann.ConfigVars == null) ? new Dictionary<string, AnnotationData.Expr>() : ann.ConfigVars.ToDictionary((KeyValuePair<string, string> keyValuePair) => keyValuePair.Key, (KeyValuePair<string, string> keyValuePair) => AnnotationData.ParseExpr(keyValuePair.Value)));
		foreach (AnnotationData.Area area2 in ann.Areas)
		{
			if (opt["crawl"] && area2.HasTag("openonly"))
			{
				area2.AddTag("optional");
			}
			area2.Mode = (area2.HasTag("dlc") ? AnnotationData.AreaMode.DLC : ((!area2.HasTag("start")) ? AnnotationData.AreaMode.Base : AnnotationData.AreaMode.Both));
			area2.IsExcluded = area2.Mode == ExcludeMode;
			if (area2.IsExcluded)
			{
				area2.AddTag("optional");
			}
			if (area2.To == null)
			{
				continue;
			}
			foreach (AnnotationData.Side item6 in area2.To)
			{
				if (!Areas.ContainsKey(item6.Area))
				{
					throw new Exception(area2.Name + " goes to nonexistent " + item6.Area);
				}
				item6.Expr = getExpr(item6.Cond);
			}
		}
		EntranceIds = new Dictionary<string, AnnotationData.Entrance>();
		foreach (AnnotationData.Entrance item7 in ann.Entrances.Concat(ann.Warps))
		{
			string text;
			if (opt.Game == GameSpec.FromGame.DS1)
			{
				text = item7.Name ?? item7.ID.ToString();
			}
			else if (opt.Game == GameSpec.FromGame.DS3)
			{
				text = item7.Area + "_" + item7.ID;
			}
			else
			{
				if (opt.Game != GameSpec.FromGame.ER)
				{
					throw new ArgumentException();
				}
				text = item7.Area + "_" + item7.Name;
			}
			item7.FullName = text;
			if (EntranceIds.ContainsKey(text))
			{
				throw new Exception("Duplicate id " + text);
			}
			EntranceIds[text] = item7;
			if (item7.Silo != null)
			{
				if (item7.Sides().Count < 2)
				{
					throw new Exception(text + " with silo " + item7.Silo + " must have both sides");
				}
				AnnotationData.Side aSide = item7.ASide;
				string silo = (item7.BSide.LinkedSilo = item7.Silo);
				aSide.Silo = silo;
				if (item7.Silo.StartsWith("from"))
				{
					AnnotationData.Side bSide = item7.BSide;
					silo = (item7.ASide.LinkedSilo = "to" + item7.Silo.Substring(4));
					bSide.Silo = silo;
				}
				else
				{
					if (!item7.Silo.StartsWith("to"))
					{
						throw new Exception("Unknown silo type " + item7.Silo + " in " + text);
					}
					AnnotationData.Side bSide2 = item7.BSide;
					silo = (item7.ASide.LinkedSilo = "from" + item7.Silo.Substring(2));
					bSide2.Silo = silo;
				}
			}
			if (!item7.HasTag("unused") && item7.Sides().Count < 2)
			{
				throw new Exception(item7.FullName + " has insufficient sides");
			}
		}
		foreach (AnnotationData.Entrance warp in ann.Warps)
		{
			if (!warp.HasTag("unused") && (warp.ASide == null || warp.BSide == null))
			{
				throw new Exception(warp.FullName + " warp missing both sides");
			}
		}
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();
		foreach (AnnotationData.Entrance entrance2 in ann.Entrances)
		{
			if (entrance2.HasTag("unused"))
			{
				continue;
			}
			if (opt.Game == GameSpec.FromGame.DS3)
			{
				if (entrance2.HasTag("norandom"))
				{
					entrance2.IsFixed = true;
				}
				else if (entrance2.HasTag("door"))
				{
					entrance2.IsFixed = true;
				}
				else if (opt["lords"] && entrance2.HasTag("kiln"))
				{
					entrance2.IsFixed = true;
				}
				else if (!opt["dlc1"] && entrance2.HasTag("dlc1"))
				{
					entrance2.IsFixed = true;
				}
				else if (!opt["dlc2"] && entrance2.HasTag("dlc2"))
				{
					entrance2.IsFixed = true;
				}
				else if (!opt["boss"] && entrance2.HasTag("boss"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "boss", entrance2.Text);
					}
				}
				else if (!opt["pvp"] && entrance2.HasTag("pvp"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "pvp", entrance2.Text);
					}
				}
			}
			else if (opt.Game == GameSpec.FromGame.DS1)
			{
				if (!opt["lordvessel"] && entrance2.HasTag("lordvessel"))
				{
					entrance2.Tags += " door";
					entrance2.DoorCond = "AND lordvessel kiln_start";
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "lordvessel", entrance2.Text);
					}
				}
				if (entrance2.HasTag("door"))
				{
					entrance2.IsFixed = true;
				}
				else if (opt["lords"] && entrance2.Area == "kiln")
				{
					entrance2.IsFixed = true;
				}
				else if (!opt["world"] && entrance2.HasTag("world"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "world", entrance2.Text);
					}
				}
				else if (!opt["boss"] && entrance2.HasTag("boss"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "boss", entrance2.Text);
					}
				}
				else if (!opt["minor"] && entrance2.HasTag("pvp") && !entrance2.HasTag("major"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "minor", entrance2.Text);
					}
				}
				else if (!opt["major"] && entrance2.HasTag("pvp") && entrance2.HasTag("major"))
				{
					entrance2.IsFixed = true;
					if (opt["dumptext"])
					{
						Util.AddMulti(dictionary, "major", entrance2.Text);
					}
				}
			}
			else
			{
				if (opt.Game != GameSpec.FromGame.ER)
				{
					continue;
				}
				if (entrance2.HasTag("norandom") || entrance2.HasTag("door") || entrance2.HasTag("dlcend"))
				{
					entrance2.IsFixed = true;
				}
				else if (entrance2.HasTag("trivial") && !opt[Feature.Segmented])
				{
					entrance2.IsFixed = true;
				}
				else if (opt["crawl"] ? entrance2.HasTag("nocrawl") : entrance2.HasTag("crawlonly"))
				{
					if (!entrance2.HasTag("newgate"))
					{
						throw new Exception();
					}
					entrance2.AddTag("unused");
				}
				else if (opt[Feature.SegmentFortresses] ? entrance2.HasTag("nofortress") : entrance2.HasTag("fortressonly"))
				{
					entrance2.AddTag("unused");
				}
			}
		}
		foreach (AnnotationData.Entrance warp2 in ann.Warps)
		{
			if (warp2.HasTag("highwall"))
			{
				if (!opt["pvp"] && !opt["boss"])
				{
					warp2.TagList.Add("norandom");
				}
				else
				{
					warp2.TagList.Add("unused");
				}
			}
			if (warp2.HasTag("unused"))
			{
				continue;
			}
			if (warp2.HasTag("norandom"))
			{
				warp2.IsFixed = true;
			}
			else if (!opt["warp"] && opt.Game != GameSpec.FromGame.ER)
			{
				warp2.IsFixed = true;
				if (opt["dumptext"])
				{
					Util.AddMulti(dictionary, "warp", warp2.Text);
				}
			}
			if (opt["lords"] && warp2.HasTag("kiln"))
			{
				warp2.IsFixed = true;
			}
			else if (!opt["dlc1"] && warp2.HasTag("dlc1"))
			{
				warp2.IsFixed = true;
			}
			else if (!opt["dlc2"] && warp2.HasTag("dlc2"))
			{
				warp2.IsFixed = true;
			}
			if (warp2.HasTag("uniquegate") && !opt["coupledwarp"] && !opt[Feature.Segmented])
			{
				warp2.AddTag("unique");
			}
			if (warp2.HasTag("uniqueminor") && !opt["coupledminor"] && !opt[Feature.Segmented])
			{
				warp2.AddTag("unique");
			}
			else if (warp2.HasTag("uniqueminor") && opt["crawl"] && !opt["req_minorwarp"])
			{
				warp2.AddTag("unique");
			}
			if (!opt["crawl"] && warp2.HasTag("crawlonly"))
			{
				warp2.AddTag("unused");
			}
			if (opt[Feature.SegmentFortresses] ? warp2.HasTag("nofortress") : warp2.HasTag("fortressonly"))
			{
				warp2.AddTag("unused");
			}
			if (opt[Feature.Segmented] ? warp2.HasTag("nosegment") : warp2.HasTag("segmentonly"))
			{
				warp2.AddTag("unused");
			}
			if (opt["crawl"])
			{
				if (warp2.HasTag("openremove"))
				{
					warp2.AddTag("unused");
					warp2.AddTag("remove");
				}
				else
				{
					foreach (string item8 in new List<string> { "cave", "catacomb", "forge", "gaol" })
					{
						if (warp2.HasTag(item8 + "only") && (!opt["req_" + item8] || opt["req_backportal"]))
						{
							warp2.AddTag("unused");
							break;
						}
					}
				}
			}
			if (warp2.HasTag("backportal"))
			{
				bool flag = opt["req_backportal"] || (opt["crawl"] && warp2.HasTag("forge"));
				if (opt[Feature.Segmented])
				{
					warp2.HasTag("unique");
				}
				else if (flag)
				{
					warp2.BSide = new AnnotationData.Side
					{
						Area = warp2.ASide.Area,
						Text = warp2.ASide.Text,
						DestinationMap = warp2.Area
					};
					warp2.AddTag("selfwarp");
				}
				else
				{
					warp2.AddTag("unused");
				}
			}
			if (opt[Feature.Segmented] && warp2.HasTag("dungeon") && warp2.Area == "m30_13_00_00")
			{
				warp2.IsFixed = true;
			}
		}
		if (opt["dumptext"] && dictionary.Count > 0)
		{
			foreach (KeyValuePair<string, List<string>> item9 in dictionary)
			{
				Console.WriteLine(item9.Key);
				foreach (string item10 in item9.Value)
				{
					Console.WriteLine("- " + item10);
				}
				Console.WriteLine();
			}
		}
		Ignore = new HashSet<(string, string)>();
		List<string> list = new List<string> { "underground", "colosseum", "divine", "belfries", "graveyard", "evergaol" };
		if (opt["crawl"])
		{
			list.Add("open");
			list.AddRange(new string[7] { "cave", "tunnel", "catacomb", "grave", "cellar", "gaol", "forge" });
		}
		else
		{
			list.AddRange(new string[1] { "dungeon" });
		}
		if (opt["shuffle"] && opt["req_dungeon"] && !opt["req_graveyard"] && ExcludeMode != AnnotationData.AreaMode.Base)
		{
			throw new Exception("Error: Stranded Graveyard should be marked as required if Mini-dungeons are also required");
		}
		foreach (AnnotationData.Entrance item11 in ann.Entrances.Concat(ann.Warps))
		{
			AnnotationData.Entrance e = item11;
			if (e.HasTag("unused"))
			{
				continue;
			}
			List<AnnotationData.Side> list2 = e.Sides();
			foreach (AnnotationData.Side item12 in list2)
			{
				AnnotationData.Side side = item12;
				if (side.Area == "unused" && side.HasTag("unused"))
				{
					Ignore.Add((e.FullName, side.Area));
					continue;
				}
				if (!Areas.TryGetValue(side.Area, out var area))
				{
					throw new Exception(e.FullName + " goes to nonexistent " + side.Area);
				}
				side.Expr = getExpr(side.Cond);
				if (opt.Game == GameSpec.FromGame.ER)
				{
					bool flag2 = list.Any(hasTag);
					bool flag3 = flag2 && list.Any(tagIsCore);
					bool isCore = true;
					if (hasTag("minorwarp"))
					{
						isCore = tagIsCore("minorwarp") && (!flag2 || flag3);
					}
					else if (flag2)
					{
						isCore = flag3;
					}
					if (opt["crawl"])
					{
						if (hasTag("open"))
						{
							isCore = false;
						}
						else if (hasTag("neveropen"))
						{
							isCore = true;
						}
						else if (!opt["req_rauhruins"] && hasTag("rauhruins"))
						{
							isCore = false;
						}
					}
					side.IsCore = isCore;
				}
				else
				{
					side.IsCore = true;
				}
				side.IsExcluded = area.IsExcluded;
				if (!e.IsFixed && side.ExcludeIfRandomized != null && !EntranceIds[side.ExcludeIfRandomized].IsFixed)
				{
					Ignore.Add((e.FullName, side.Area));
				}
				if (ExcludeMode == AnnotationData.AreaMode.Base && side.HasTag("afterstart"))
				{
					Ignore.Add((e.FullName, side.Area));
				}
				else if (side.AlternateOf != null && shouldIgnoreAlt())
				{
					Ignore.Add((e.FullName, side.Area));
				}
				else if (side.HasTag("unused"))
				{
					Ignore.Add((e.FullName, side.Area));
				}
				if (area.HasTag("avoidstart"))
				{
					side.AddTag("avoidstart");
				}
				bool hasTag(string tag)
				{
					if (!e.HasTag(tag))
					{
						return side.HasTag(tag);
					}
					return true;
				}
				bool shouldIgnoreAlt()
				{
					if (side.HasTag("altlogic") && opt[Feature.Segmented])
					{
						return false;
					}
					if (area.IsExcluded)
					{
						return false;
					}
					return true;
				}
				bool tagIsCore(string tag)
				{
					if (hasTag(tag))
					{
						if (!opt["req_" + tag])
						{
							return opt["req_all"];
						}
						return true;
					}
					return false;
				}
			}
			if (e.HasTag("backportal") && !opt[Feature.Segmented] && list2.Any((AnnotationData.Side s) => !s.IsCore))
			{
				e.AddTag("unused");
				continue;
			}
			if (opt["crawl"])
			{
				list2.All((AnnotationData.Side s) => !s.IsCore);
			}
			if (opt["crawl"] && e.HasTag("unique") && list2.Count == 2)
			{
				AnnotationData.Side side2 = list2.Find((AnnotationData.Side s) => s.IsCore);
				AnnotationData.Side side3 = list2.Find((AnnotationData.Side s) => !s.IsCore);
				if (side2 != null && side3 != null)
				{
					if (e.HasTag("opensplit") && !side2.IsExcluded)
					{
						side3.AddTag("unused");
						side3.AddTag("remove");
						Ignore.Add((e.FullName, side3.Area));
						if (e.ASide.HasTag("remove"))
						{
							e.AddTag("remove");
						}
					}
					else
					{
						e.AddTag("unused");
						e.AddTag("remove");
					}
				}
				else if (e.HasTag("opensplit") && list2.All((AnnotationData.Side s) => !s.IsCore))
				{
					e.AddTag("unused");
					e.AddTag("remove");
				}
			}
			if (e.HasTag("baseonly") && ExcludeMode == AnnotationData.AreaMode.Base)
			{
				e.AddTag("unused");
				e.AddTag("remove");
			}
		}
		if (opt["crawl"] && ann.DungeonItems != null)
		{
			foreach (AnnotationData.DungeonItem dungeonItem in ann.DungeonItems)
			{
				dungeonItem.IsExcluded = ExcludeMode != AnnotationData.AreaMode.None && ExcludeMode == AnnotationData.AreaMode.Base != dungeonItem.HasTag("dlc");
				if (dungeonItem.HasTag("rauhruins") && opt["req_rauhruins"])
				{
					dungeonItem.IsExcluded = true;
				}
			}
		}
		if (opt["crawl"] && ExcludeMode != AnnotationData.AreaMode.None)
		{
			int num = ((ExcludeMode != AnnotationData.AreaMode.Base) ? 5 : 0);
			int num2 = ((ExcludeMode == AnnotationData.AreaMode.Base) ? 5 : 9);
			for (int num3 = num; num3 < num2; num3++)
			{
				UnlockTiers[num3] = -1;
			}
		}
		Nodes = Areas.ToDictionary((KeyValuePair<string, AnnotationData.Area> keyValuePair) => keyValuePair.Key, (KeyValuePair<string, AnnotationData.Area> keyValuePair) => new Node
		{
			Area = keyValuePair.Key,
			Cost = areaCost(keyValuePair.Value),
			ScalingBase = ((opt["dumpdist"] || opt["scalingbase"]) ? keyValuePair.Value.ScalingBase : null)
		});
		foreach (AnnotationData.Area area3 in ann.Areas)
		{
			if (area3.To == null)
			{
				continue;
			}
			foreach (AnnotationData.Side item13 in area3.To)
			{
				if (item13.HasTag("temp") || (item13.HasTag("hard") && !opt["hard"]) || (opt["crawl"] ? item13.HasTag("nocrawl") : item13.HasTag("crawlonly")) || (opt["crawl"] && opt["req_rauhruins"] && item13.HasTag("norauhruins")) || (ExcludeMode != AnnotationData.AreaMode.Base && item13.HasTag("dlconly")) || (opt[Feature.SegmentFortresses] ? item13.HasTag("nofortress") : item13.HasTag("fortressonly")) || (opt[Feature.Segmented] ? item13.HasTag("nosegment") : item13.HasTag("segmentonly")) || (item13.HasTag("treeskip") && !opt["treeskip"]) || (item13.HasTag("instawarp") && !opt["instawarp"]))
				{
					continue;
				}
				AnnotationData.Side side4 = new AnnotationData.Side
				{
					Area = area3.Name,
					Text = item13.Text,
					Expr = item13.Expr,
					Tags = item13.Tags,
					IgnoreCondFlag = item13.IgnoreCondFlag
				};
				AnnotationData.Side side5 = new AnnotationData.Side
				{
					Area = item13.Area,
					Text = item13.Text,
					Tags = item13.Tags,
					IgnoreCondFlag = item13.IgnoreCondFlag
				};
				bool isWorld = (side5.IsWorld = true);
				side4.IsWorld = isWorld;
				if (item13.HasTag("shortcut"))
				{
					AnnotationData.Expr expr = AnnotationData.Expr.Named(area3.Name);
					if (item13.Expr != null)
					{
						expr = new AnnotationData.Expr(new List<AnnotationData.Expr> { expr, item13.Expr }).Simplify();
					}
					side5.Expr = expr;
					Edge item = AddPairedEdges(side4, null).Item1;
					Edge item2 = AddPairedEdges(side5, null).Item2;
					Connect(item, item2);
				}
				else
				{
					Edge exit = AddEdge(side4, null, isExit: true);
					Edge entrance = AddEdge(side5, null, isExit: false);
					Connect(exit, entrance);
				}
			}
		}
		Dictionary<Connection, List<(Edge, Edge)>> dictionary2 = new Dictionary<Connection, List<(Edge, Edge)>>();
		foreach (AnnotationData.Entrance warp3 in ann.Warps)
		{
			if (warp3.ASide.HasTag("temp") || warp3.HasTag("unused"))
			{
				continue;
			}
			Edge edge = null;
			Edge edge2 = null;
			if (!Ignore.Contains((warp3.FullName, warp3.ASide.Area)))
			{
				edge = AddEdge(warp3.ASide, warp3, isExit: true);
			}
			if (!Ignore.Contains((warp3.FullName, warp3.BSide.Area)))
			{
				edge2 = AddEdge(warp3.BSide, warp3, isExit: false);
			}
			if (edge == null || edge2 == null)
			{
				if (!warp3.HasTag("unique") || warp3.IsFixed)
				{
					throw new Exception($"Unsupported warp configuration for {warp3}: not marked unique or not randomized");
				}
				continue;
			}
			edge.FixedLink = edge2;
			edge2.FixedLink = edge;
			if (warp3.IsFixed)
			{
				Connect(edge, edge2);
			}
			else if (warp3.HasTag("selfwarp"))
			{
				edge.Pair = edge2;
				edge2.Pair = edge;
			}
			else if (!warp3.HasTag("unique"))
			{
				Connection key = ((warp3.PairWith == null) ? new Connection(warp3.ASide.Area, warp3.BSide.Area) : new Connection(warp3.FullName, warp3.PairWith));
				Util.AddMulti(dictionary2, key, (edge, edge2));
			}
		}
		foreach (KeyValuePair<Connection, List<(Edge, Edge)>> item14 in dictionary2)
		{
			if (item14.Value.Count != 2)
			{
				throw new Exception($"Bidirectional warp expected for {item14.Key} - non-bidirectional should be marked unique");
			}
			if (!opt["unconnected"])
			{
				var (edge3, edge4) = item14.Value[0];
				var (edge5, edge6) = item14.Value[1];
				if (edge3.From == edge5.From && edge3.Name != item14.Key.A && edge5.Name != item14.Key.A)
				{
					throw new Exception($"Duplicate warp {edge3} and {edge5} - should be marked unique in connection {item14.Key}");
				}
				if (edge3.From != edge6.To || edge5.From != edge4.To)
				{
					throw new Exception($"Internal error: warp {edge3} and {edge6} not equivalent");
				}
				edge3.Pair = edge6;
				edge6.Pair = edge3;
				edge5.Pair = edge4;
				edge4.Pair = edge5;
			}
		}
		HashSet<Connection> hashSet = new HashSet<Connection>();
		foreach (AnnotationData.Entrance entrance3 in ann.Entrances)
		{
			if (entrance3.HasTag("unused"))
			{
				continue;
			}
			List<AnnotationData.Side> list3 = entrance3.Sides();
			if (list3.Count == 1)
			{
				if (entrance3.HasTag("door"))
				{
					throw new Exception(entrance3.FullName + " has one-sided door");
				}
				AddPairedEdges(list3[0], entrance3);
				continue;
			}
			AnnotationData.Side side6 = list3[0];
			AnnotationData.Side side7 = list3[1];
			if (entrance3.HasTag("door") && (Ignore.Contains((entrance3.FullName, side7.Area)) || Ignore.Contains((entrance3.FullName, side6.Area))))
			{
				continue;
			}
			if (entrance3.HasTag("door"))
			{
				bool isWorld = (side7.IsWorld = true);
				side6.IsWorld = isWorld;
				Connection item3 = new Connection(side6.Area, side7.Area);
				if (!hashSet.Contains(item3))
				{
					hashSet.Add(item3);
					AnnotationData.Expr expr2 = getExpr(entrance3.DoorCond);
					if (side6.Expr != null || side7.Expr != null)
					{
						throw new Exception($"Door cond {expr2} and cond {side6.Expr} {side7.Expr} together for {entrance3.FullName}");
					}
					side6.Expr = ((!side6.HasTag("dnofts")) ? expr2 : ((expr2 == null) ? AnnotationData.Expr.Named(side7.Area) : new AnnotationData.Expr(new List<AnnotationData.Expr>
					{
						expr2,
						AnnotationData.Expr.Named(side7.Area)
					}).Simplify()));
					side7.Expr = ((!side7.HasTag("dnofts")) ? expr2 : ((expr2 == null) ? AnnotationData.Expr.Named(side6.Area) : new AnnotationData.Expr(new List<AnnotationData.Expr>
					{
						expr2,
						AnnotationData.Expr.Named(side6.Area)
					}).Simplify()));
					Edge item4 = AddPairedEdges(side6, entrance3).Item1;
					Edge item5 = AddPairedEdges(side7, entrance3).Item2;
					Connect(item4, item5);
				}
			}
			else if (entrance3.IsFixed || !opt["unconnected"])
			{
				Edge edge7 = null;
				Edge edge8 = null;
				Edge edge9 = null;
				Edge edge10 = null;
				if (!Ignore.Contains((entrance3.FullName, side7.Area)))
				{
					(edge7, edge8) = AddPairedEdges(side7, entrance3);
				}
				if (!Ignore.Contains((entrance3.FullName, side6.Area)))
				{
					(edge10, edge9) = AddPairedEdges(side6, entrance3);
				}
				if (edge7 != null && edge9 != null)
				{
					edge7.FixedLink = edge9;
					edge9.FixedLink = edge7;
					edge8.FixedLink = edge10;
					edge10.FixedLink = edge8;
					if (entrance3.IsFixed)
					{
						Connect(edge7, edge9);
					}
				}
			}
			else
			{
				if (!Ignore.Contains((entrance3.FullName, side7.Area)))
				{
					AddEdge(side7, entrance3, isExit: true);
					AddEdge(side7, entrance3, isExit: false);
				}
				if (!Ignore.Contains((entrance3.FullName, side6.Area)))
				{
					AddEdge(side6, entrance3, isExit: true);
					AddEdge(side6, entrance3, isExit: false);
				}
			}
		}
		int areaCost(AnnotationData.Area a)
		{
			if (a.HasTag("trivial"))
			{
				return 0;
			}
			if (opt.Game == GameSpec.FromGame.ER)
			{
				if (a.HasTag("minor") || a.HasTag("minidungeon"))
				{
					return 1;
				}
				if (a.DefeatFlag > 0 || a.HasTag("overworld"))
				{
					return 3;
				}
				return 1;
			}
			if (a.HasTag("small"))
			{
				return 1;
			}
			return 3;
		}
		AnnotationData.Expr getExpr(string cond)
		{
			AnnotationData.Expr expr3 = AnnotationData.ParseExpr(cond);
			if (expr3 == null)
			{
				return null;
			}
			foreach (string item15 in expr3.FreeVars())
			{
				if (!Areas.ContainsKey(item15) && !ItemAreas.ContainsKey(item15) && !ConfigExprs.ContainsKey(item15))
				{
					throw new Exception("Condition " + cond + " has unknown variable " + item15);
				}
			}
			return expr3;
		}
	}
}
